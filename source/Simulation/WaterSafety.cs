namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    public const int EvacuationWater = 200; // litres: leave before the 500 L navigation cutoff
    private double _waterSafetyAt, _waterNoticeAt;

    // A confirmation covers this excavation and this risk envelope only. Tokens
    // are deliberately not saved: loading must assess current water again.
    public string DigWaterRisk(DemoJob job)
    {
        if (!job.Dig || job.Demolish || Fluids.Cells.Count==0&&!Fluids.Sources.Any(s=>s.Stored>0||s.Recharge>0)) return "";
        var openings = Enumerable.Range(0, job.DigHeight).Select(y => job.Cell + new Cell(0,0,y)).ToHashSet();
        var nearby = openings.SelectMany(c => FluidSimulation.Adjacent.Select(d => c+d).Append(c)).Distinct()
            .OrderBy(c=>c.Level).ThenBy(c=>c.X).ThenBy(c=>c.Z);
        var risks = new List<string>();
        foreach (var c in nearby)
        {
            int amount = Fluids.Amount(c);
            if (amount > 0 && (openings.Contains(c) || FluidPassable(c)))
                risks.Add($"w:{c.X},{c.Z},{c.Level}:{(amount >= 500 ? 2 : 1)}");
            foreach (var source in Fluids.Sources.Where(s=>s.Cell==c && (s.Stored>0 || s.Recharge>0)))
                risks.Add($"a:{c.X},{c.Z},{c.Level}:{Math.Ceiling(source.CurrentHead)}:{source.Recharge>0}");
        }
        return string.Join(";", risks);
    }
    private bool CheckDigWater(DemoJob job)
    {
        if (!job.Dig || job.Demolish) return true;
        if(job.WaterPaused)return false;
        string risk = DigWaterRisk(job);
        if (risk != "" && risk != job.AcceptedWaterRisk && !job.WaterPaused)
        {
            job.WaterPaused = true;
            Release(job); Revision++;
            // Log only the work face, never reveal an undiscovered aquifer.
            if(_simulationTime>=_waterNoticeAt)
            {Log("采掘因渗水风险暂停；请点击水患预警定位并逐项处理。");_waterNoticeAt=_simulationTime+1;}
        }
        return !job.WaterPaused;
    }
    public ResidentResult ResumeWaterDig(int jobId, string observedRisk)
    {
        var job = Jobs.FirstOrDefault(j=>j.Id==jobId && j.Dig && j.WaterPaused);
        if (job is null) return new(false,"任务已取消或不在水患暂停状态");
        string current = DigWaterRisk(job);
        if (observedRisk != current) return new(false,"水情已变化，请重新查看风险后确认");
        job.AcceptedWaterRisk=current; job.WaterPaused=false; Revision++;
        RefreshLifeAlerts();
        return new(true,current==""?"风险已消退，恢复这项采掘":"已确认这项采掘的涌水风险；水情变化时会再次暂停");
    }
    private void RefreshWaterSafety()
    {
        if (_simulationTime < _waterSafetyAt) return;
        _waterSafetyAt=_simulationTime+.25;
        foreach(var job in Jobs) CheckDigWater(job);
    }
    private bool FloodThreat(DemoWorker w) => Fluids.Amount(w.Tile)>=EvacuationWater ||
        w.Path.Any(c=>Fluids.Amount(c)>=500);
    private bool DryRefuge(Cell c) => Walkable(c) && Fluids.Amount(c)<EvacuationWater &&
        !FluidSimulation.Adjacent.Any(d=>Fluids.Amount(c+d)>=500) &&
        !Jobs.Any(j=>PlannedHazard(j,c));
    private bool EscapeStep(Cell from,Cell to) => ConnectedCore(from,to,true) &&
        Fluids.Amount(to)<500 && (Fluids.Amount(from)<EvacuationWater || Fluids.Amount(to)<=Fluids.Amount(from));
    private Queue<Cell>? FindWaterEscape(Cell start)
    {
        // Local, bounded emergency search. Only the first occupied cell may be
        // deep: the resident may step out to shore, never traverse deep water.
        var previous=new Dictionary<Cell,Cell>{{start,start}};
        var queue=new Queue<Cell>();queue.Enqueue(start);
        while(queue.TryDequeue(out var c) && previous.Count<=512)
        {
            if(DryRefuge(c))
            {
                var path=new List<Cell>();for(var p=c;p!=start;p=previous[p])path.Add(p);
                path.Reverse();return new(path);
            }
            foreach(var n in Neighbors(c))
                if(!previous.ContainsKey(n) && EscapeStep(c,n)) {previous[n]=c;queue.Enqueue(n);}
        }
        return null;
    }
    private bool WaterEscapeValid(DemoWorker w)
    {
        var from=w.Tile;
        foreach(var to in w.Path){if(!EscapeStep(from,to))return false;from=to;}
        return DryRefuge(from);
    }
    private bool UpdateWaterEscape(DemoWorker w,double dt,ref int budget)
    {
        if(w.Person.Dead){w.WaterEvacuating=false;w.WaterIssue="";return false;}
        if(CarrierOf(w) is {} carrier)
        {if(carrier.WaterEvacuating){CarryPosition(carrier,w);return true;}return false;}
        if(!w.WaterEvacuating)
        {
            if(!FloodThreat(w))return false;
            w.WaterEvacuating=true;w.WaterRetryAt=0;w.Person.MedicalBed=null;
            w.Status=w.WaterIssue="涨水待撤离";
            if(w.Job is {} job)Release(job);
            if(w.Person.Activity?.Kind!="rescue_carry")CancelResidentActivity(w);
            w.Person.PausedResearch=null;StopMovement(w);Revision++;
            Log(w.Name+"遭遇涨水，已中断任务并尝试撤离。");
        }
        if(w.Path.Count==0 && DryRefuge(w.Tile))
        {
            if(w.Person.Activity?.Kind=="rescue_carry")CancelResidentActivity(w);
            w.WaterEvacuating=false;w.WaterIssue="";w.Status="已离开水患，重新检查任务";w.NextJobSearch=_simulationTime+.25;Revision++;return true;
        }
        if(w.Person.Activity is not null && w.Person.Activity.Kind!="rescue_carry")CancelResidentActivity(w);
        if(w.Person.Activity?.Kind=="rescue_carry" && (w.Person.Incapacitated||w.Person.Mobility<.2||w.Person.Manipulation<.2))CancelResidentActivity(w);
        if(w.Person.Incapacitated || w.Person.Mobility<.05)
        {StopMovement(w);w.Status=w.WaterIssue="水患受困 · 无法自行移动，请从安全岸边救援或先排水";return true;}
        if(w.Path.Count>0 && !WaterEscapeValid(w)){StopMovement(w);w.WaterRetryAt=0;}
        if(w.Path.Count==0)
        {
            if(DryRefuge(w.Tile))
            {
                if(w.Person.Activity?.Kind=="rescue_carry")CancelResidentActivity(w);
                w.WaterEvacuating=false;w.WaterIssue="";w.Status="已撤离水患，重新检查任务";w.NextJobSearch=_simulationTime+.25;Revision++;
                return true;
            }
            if(_simulationTime<w.WaterRetryAt || budget<=0)return true;
            budget--;w.WaterRetryAt=_simulationTime+1;
            var route=FindWaterEscape(w.Tile);
            if(route is null){w.Status=w.WaterIssue="水患受困 · 附近未找到安全路线，请排水或开辟安全岸边救援通路";return true;}
            foreach(var c in route)w.Path.Enqueue(c);
        }
        w.Status=w.WaterIssue="涨水撤离中";
        if(w.Path.Count>0)Move(w,dt);
        if(w.Person.Activity is {Kind:"rescue_carry"} a && Workers.FirstOrDefault(p=>p.Id==a.Target) is {} patient)CarryPosition(w,patient);
        return true;
    }
    private void AddWaterAlerts()
    {
        foreach(var w in Workers.Where(w=>w.WaterEvacuating&&!w.Person.Dead))
            LifeAlerts.Add(new("flood:"+w.Id,w.Name+" · "+(w.WaterIssue==""?"涨水待撤离":w.WaterIssue),0,w.Id,w.Tile));
        foreach(var j in Jobs.Where(j=>j.WaterPaused))
            LifeAlerts.Add(new("dig-water:"+j.Id,$"采掘 #{j.Id} 已暂停 · 渗水风险（点击处理）",1,Location:j.Cell));
    }
}
