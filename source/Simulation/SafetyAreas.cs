namespace NoDwarf.Simulation;

public sealed record SafetyVolume(Cell Min,Cell Max)
{
    public bool Contains(Cell c)=>c.X>=Min.X&&c.X<=Max.X&&c.Z>=Min.Z&&c.Z<=Max.Z&&c.Level>=Min.Level&&c.Level<=Max.Level;
    public static SafetyVolume Between(Cell a,Cell b)=>new(new(Math.Min(a.X,b.X),Math.Min(a.Z,b.Z),Math.Min(a.Level,b.Level)),new(Math.Max(a.X,b.X),Math.Max(a.Z,b.Z),Math.Max(a.Level,b.Level)));
}
public sealed record SafetyRules
{
    public bool Enabled {get;init;}=true;
    public bool StopOutsideSupplies {get;init;}
    public IReadOnlyList<SafetyVolume> WorkAreas {get;init;}=Array.Empty<SafetyVolume>();
    public IReadOnlyList<SafetyVolume> ForbiddenAreas {get;init;}=Array.Empty<SafetyVolume>();
    public SafetyRules CheckedCopy()
    {
        if(WorkAreas is null||ForbiddenAreas is null||WorkAreas.Count>64||ForbiddenAreas.Count>64||
            WorkAreas.Concat(ForbiddenAreas).Any(v=>v is null||!DemoWorld.Inside(v.Min)||!DemoWorld.Inside(v.Max)||v.Min.X>v.Max.X||v.Min.Z>v.Max.Z||v.Min.Level>v.Max.Level)||
            StopOutsideSupplies&&WorkAreas.Count==0)throw new InvalidDataException("安全范围无效：每类最多 64 个范围，停止外出取料需要作业区");
        return this with{WorkAreas=Array.AsReadOnly(WorkAreas.ToArray()),ForbiddenAreas=Array.AsReadOnly(ForbiddenAreas.ToArray())};
    }
}
public sealed partial class DemoWorld
{
    public SafetyRules Safety {get;private set;}=new();
    public bool SafetyTransitAllowed(Cell c)=>!Safety.Enabled||!Safety.ForbiddenAreas.Any(v=>v.Contains(c));
    public bool SafetyWorkAllowed(Cell c)=>SafetyTransitAllowed(c)&&(!Safety.Enabled||Safety.WorkAreas.Count==0||Safety.WorkAreas.Any(v=>v.Contains(c)));
    public bool SafetySupplyAllowed(Cell c)=>SafetyTransitAllowed(c)&&(!Safety.Enabled||!Safety.StopOutsideSupplies||SafetyWorkAllowed(c));
    internal bool SafetyEdgeAllowed(Cell a,Cell b)=>SafetyTransitAllowed(a)&&SafetyTransitAllowed(b)&&
        (Math.Abs(a.Level-b.Level)<2||SafetyTransitAllowed(new(b.X,b.Z,Math.Min(a.Level,b.Level)+1)));
    public WorldNavigation ResidentNavigation=>Safety.Enabled&&Safety.ForbiddenAreas.Count>0?Navigation.Residents:Navigation;
    private WorldNavigation NavigationFor(DemoWorker actor)=>Enemies.Contains(actor)?Navigation:ResidentNavigation;
    private readonly HashSet<string> _safetyLeaving=new();
    public ResidentResult ConfigureSafety(SafetyRules rules)
    {
        Safety=rules.CheckedCopy();Navigation.InvalidatePermissions();_accessVersion=-1;Revision++;
        RefreshSafetyJobs();
        foreach(var worker in Workers)
        {
            if(worker.Job is {} job&&(!SafetyJobAllowed(job)||!PrepareMaterials(worker,job)))Release(job);
            if(worker.Person.Activity is {} activity&&!SafetyActivityAllowed(worker,activity))CancelResidentActivity(worker,"安全范围已改变");
            int budget=1;if(worker.Path.Count>0&&SafetyTransitAllowed(worker.Tile))EnsureWorkerRoute(worker,ref budget);
            ValidateHomeBed(worker);worker.NextJobSearch=worker.NextCareCheck=worker.NextRescueCheck=worker.NextMedicalCheck=0;
        }
        RefreshLifeAlerts();return new(true,Safety.Enabled?"安全范围已应用；禁行优先，征召与手动命令也不能穿越禁行区":"已停用安全限制，居民可以重新前往原先禁止的区域");
    }
    private void RefreshSafetyJobs()
    {
        foreach(var job in Jobs)
        {
            bool allowed=SafetyJobAllowed(job);
            bool sourceBlocked=job.Materials.Any(m=>m.Stage==0&&Items.FirstOrDefault(s=>s.Id==m.StackId) is {} s&&!SafetySupplyAllowed(s.Cell));
            bool forbidden=job.Materials.Any(m=>m.Stage!=1&&Items.FirstOrDefault(s=>s.Id==m.StackId) is {Forbidden:true});
            if((!allowed||sourceBlocked||forbidden)&&(!job.MaterialsPaused||forbidden))
            {
                Release(job);job.SafetyPaused|=!allowed||sourceBlocked;job.SupplyPaused|=forbidden;job.OutputCell=null;
                foreach(var m in job.Materials.Where(m=>m.Stage==0||Items.FirstOrDefault(s=>s.Id==m.StackId) is {Forbidden:true})){m.Stage=0;m.StackId=0;m.Properties=new();}
                Revision++;
            }
            if(!job.MaterialsPaused||!allowed)continue;
            var replacement=new List<MaterialReservation>();bool ready=true;
            foreach(var group in job.Materials.Where(m=>m.Stage==0).GroupBy(m=>m.Item))
            {
                int left=group.Sum(m=>m.Count);
                if(AvailableForUse(group.Key,JobUse(job))<left){ready=false;break;}
                foreach(var stack in OrderSupplies(Items.Where(s=>s.Owner==""&&s.Item==group.Key&&BillAllowsSource(job,s)&&FoodSourceReachable(job,s)),job.Cell))
                {
                    int n=Math.Min(left,Math.Max(0,stack.Count-Reserved(stack.Id)));if(n==0)continue;
                    replacement.Add(new(){StackId=stack.Id,Item=stack.Item,Count=n});left-=n;if(left==0)break;
                }
                if(left>0){ready=false;break;}
            }
            if(!ready)continue;
            job.Materials.RemoveAll(m=>m.Stage==0);job.Materials.AddRange(replacement);job.SafetyPaused=false;job.SupplyPaused=false;Revision++;
        }
    }
    private bool SafetyJobAllowed(DemoJob job)=>SafetyWorkAllowed(job.Cell)&&
        (!job.Dig||Enumerable.Range(0,job.DigHeight).All(h=>SafetyWorkAllowed(job.Cell+new Cell(0,0,h))));
    private bool SafetyActivityAllowed(DemoWorker worker,ResidentActivity a)
    {
        if(a.ItemId>0&&Items.FirstOrDefault(s=>s.Id==a.ItemId) is {Forbidden:true})return false;
        bool supply=a.Kind is "food_supply" or "care_supply" or "life_supply" or "pickup" or "water_fill";
        bool selfCare=(a.Kind is "eat" or "rest"||a.Kind=="life_use"&&a.Target==worker.Id)&&a.Destination==worker.Tile&&worker.Path.Count==0;
        if(!(supply?SafetySupplyAllowed(a.Destination):selfCare?SafetyTransitAllowed(a.Destination):SafetyWorkAllowed(a.Destination)))return false;
        if(supply&&a.ItemId>0&&Items.FirstOrDefault(s=>s.Id==a.ItemId) is {} stack&&stack.Owner==""&&!SafetySupplyAllowed(stack.Cell))return false;
        if(a.Kind=="water_fill"&&a.SourceCell is Cell source&&!SafetySupplyAllowed(source))return false;
        if(a.RescueBed is Cell bed&&!SafetyWorkAllowed(bed))return false;
        if(a.Kind is "feed" or "food_supply" or "treat" or "care_supply" or "life_supply" or "life_use" or "rescue_pickup")
            if(Workers.FirstOrDefault(w=>w.Id==a.Target) is {} patient&&patient!=worker&&!SafetyWorkAllowed(patient.Tile))return false;
        return true;
    }
    private void AddSafetyAlerts()
    {
        if(!Safety.Enabled||Safety.WorkAreas.Count==0&&Safety.ForbiddenAreas.Count==0)return;
        foreach(var w in Workers.Where(w=>!w.Person.Dead))
        {
            var missing=new List<string>();
            bool SupplyReach(Cell c)=>SafetySupplyAllowed(c)&&ItemPositions(c).Any(p=>SafetySupplyAllowed(p)&&ResidentNavigation.CanReach(w.Tile,p));
            if(!SafetyTransitAllowed(w.Tile))missing.Add("位于禁行区，请撤出或调整范围后救援");
            else
            {
                if(PersonalFood(w) is null&&!Items.Any(s=>CanEat(s)&&FreeForUse(s,"food")>0&&SupplyReach(s.Cell)))missing.Add("无可达口粮");
                bool Has(string item)=>Inventory(w).Any(i=>i.Item==item&&!i.Forbidden)||Items.Any(s=>s.Item==item&&FreeForUse(s,"care")>0&&SupplyReach(s.Cell));
                if(!Has("water_bucket")&&!(Has("bucket")&&WaterPoints.Any(c=>Fluids.Amount(c)>=5&&SupplyReach(c))))missing.Add("无可达饮水");
                if(!Buildings.Values.Any(b=>b.Kind==BuildKind.Bed&&SafetyWorkAllowed(b.Cell)&&ItemPositions(b.Cell).Any(p=>SafetyWorkAllowed(p)&&ResidentNavigation.CanReach(w.Tile,p))))missing.Add("无可达床位");
            }
            if(missing.Count>0)LifeAlerts.Add(new("safety:"+w.Id,w.Name+" · 安全范围："+string.Join("、",missing),1,w.Id,w.Tile));
        }
        foreach(var j in Jobs.Where(j=>j.SafetyPaused).Take(32))LifeAlerts.Add(new("safety-job:"+j.Id,$"任务 #{j.Id} 安全暂停：检查范围与原料（点击定位）",2,Location:j.Cell));
    }
    private bool SafetyEscapeStep(Cell from,Cell to)=>SafetyTransitAllowed(to)||
        !SafetyTransitAllowed(from)&&Safety.ForbiddenAreas.Where(v=>v.Contains(to)).All(v=>v.Contains(from));
    private bool UpdateSafetyExit(DemoWorker worker,double dt)
    {
        if(worker.Person.Dead)return false;
        if(SafetyTransitAllowed(worker.Tile)&&!_safetyLeaving.Contains(worker.Id))return false;
        if(_safetyLeaving.Add(worker.Id))
        {if(worker.Job is {} job)Release(job);CancelResidentActivity(worker,"撤出禁行区");}
        if(SafetyTransitAllowed(worker.Tile))
        {_safetyLeaving.Remove(worker.Id);StopMovement(worker);worker.Status="已撤出禁行区";return true;}
        if(worker.Person.Incapacitated||worker.Person.Mobility<.05){worker.Status="禁行区受困，请调整范围后救援";return true;}
        bool Valid()
        {var from=worker.Tile;foreach(var to in worker.Path){if(!Connected(from,to)||!SafetyEscapeStep(from,to))return false;from=to;}return SafetyTransitAllowed(from);}
        if(worker.Path.Count>0&&!Valid())StopMovement(worker);
        if(worker.Path.Count==0)
        {
            if(SimulationTime<worker.NextPolicyCheck)return true;worker.NextPolicyCheck=SimulationTime+1;
            var seen=new Dictionary<Cell,Cell>{{worker.Tile,worker.Tile}};var queue=new Queue<Cell>();queue.Enqueue(worker.Tile);Cell? exit=null;
            while(queue.TryDequeue(out var cell)&&seen.Count<=512)
            {
                if(SafetyTransitAllowed(cell)&&DryRefuge(cell)){exit=cell;break;}
                foreach(var n in Neighbors(cell))if(!seen.ContainsKey(n)&&Connected(cell,n)&&SafetyEscapeStep(cell,n)&&Fluids.Amount(n)<EvacuationWater){seen[n]=cell;queue.Enqueue(n);}
            }
            if(exit is null){worker.Status="禁行区受困，未找到撤出路线，请调整范围";return true;}
            var path=new List<Cell>();for(var p=exit.Value;p!=worker.Tile;p=seen[p])path.Add(p);path.Reverse();foreach(var c in path)worker.Path.Enqueue(c);
        }
        worker.Status="撤出禁行区；暂停作业";if(worker.Path.Count>0)Move(worker,dt);return true;
    }
}
