namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    public HashSet<Cell> MedicalBeds {get;}=new();
    public bool IsMedicalBed(Cell c)=>MedicalBeds.Contains(c)&&Buildings.TryGetValue(c,out var b)&&b.Kind==BuildKind.Bed;
    public ResidentResult SetMedicalBed(Cell cell,bool enabled)
    {
        if(!Buildings.TryGetValue(cell,out var b)||b.Kind!=BuildKind.Bed)return new(false,"请选择已经建好的床");
        if(enabled){MedicalBeds.Add(cell);foreach(var w in Workers)if(w.Person.HomeBed==cell){w.Person.HomeBed=null;if(w.Person.Activity?.Kind=="rest")CancelResidentActivity(w);}}
        else {MedicalBeds.Remove(cell);foreach(var w in Workers){if(w.Person.MedicalBed==cell)w.Person.MedicalBed=null;if(w.Person.Activity?.RescueBed==cell)CancelResidentActivity(w);}}
        Revision++;return new(true,enabled?"已设为医疗床，保留给伤员":"已恢复为普通床，医疗预约已释放");
    }
    public bool NeedsRescue(DemoWorker w)=>!w.Person.Dead&&(w.Person.Incapacitated||w.Person.Mobility<.05)&&w.Person.MedicalBed is null;
    private DemoWorker? CarrierOf(DemoWorker patient)=>Workers.FirstOrDefault(w=>w!=patient&&w.Person.Activity is {Kind:"rescue_carry"} a&&a.Target==patient.Id);
    private bool RescueReserved(string id)=>Workers.Any(w=>w.Person.Activity is {Kind:"rescue_pickup" or "rescue_carry"} a&&a.Target==id);
    private bool BedReserved(Cell bed)=>Workers.Any(w=>!w.Person.Dead&&(w.Person.MedicalBed==bed||w.Person.Activity?.RescueBed==bed));
    private bool MedicalBedsideFree(Cell cell,DemoWorker helper)=>!Workers.Any(w=>w!=helper&&(w.Person.MedicalBed is not null&&w.Tile==cell||w.Person.Activity is {Kind:"rescue_carry"} a&&a.Destination==cell));
    private static bool MedicalRecovery(ResidentProfile p)=>p.Incapacitated||p.Mobility<.05||p.Blood<60;
    private static void CarryPosition(DemoWorker carrier,DemoWorker patient)
    {patient.X=carrier.X;patient.Z=carrier.Z;patient.Level=carrier.Level;patient.TravelFrom=carrier.TravelFrom;patient.ClimbProgress=carrier.ClimbProgress;}
    private void DropRescue(DemoWorker carrier)
    {
        if(carrier.Person.Activity is not {Kind:"rescue_carry"} a)return;
        var patient=Workers.FirstOrDefault(w=>w.Id==a.Target);if(patient is null)return;
        var cell=carrier.Tile;patient.X=cell.X;patient.Z=cell.Z;patient.Level=cell.Level;StopMovement(patient);patient.Status="搬运中断 · 原地等待照料";
    }
    private bool UpdateMedicalPatient(DemoWorker patient,double dt)
    {
        var carrier=CarrierOf(patient);
        if(carrier is not null){CarryPosition(carrier,patient);patient.Status="由"+carrier.Name+"搬运";return true;}
        if(patient.Person.MedicalBed is not Cell bed)
        {
            if(!MilitaryControlled(patient)&&HealthCare.Urgency(patient.Person)==0&&Workers.Any(m=>m!=patient&&m.Person.Activity is {Kind:"care_supply" or "treat"} a&&a.Target==patient.Id))
            {if(patient.Job is not null)Release(patient.Job);if(patient.Person.Activity is not null)CancelResidentActivity(patient);StopMovement(patient);patient.Status="原地等待紧急止血";return true;}
            return false;
        }
        if(!IsMedicalBed(bed)||patient.Person.Dead||!ItemPositions(bed).Contains(patient.Tile)||!MedicalRecovery(patient.Person))
        {patient.Person.MedicalBed=null;return false;}
        patient.Person.Rest=Math.Min(100,patient.Person.Rest+dt*1.8);
        if(patient.Person.Activity?.Kind=="eat"&&!patient.Person.Incapacitated)return false;
        if(patient.Job is not null)Release(patient.Job);
        if(patient.Person.Activity is not null)CancelResidentActivity(patient);
        StopMovement(patient);patient.Status="医疗床休养 · 等待护理或送饭";return true;
    }
    public ResidentResult RescueResident(string helperId,string patientId,string origin="manual")
    {
        var helper=Workers.FirstOrDefault(w=>w.Id==helperId);var patient=Workers.FirstOrDefault(w=>w.Id==patientId);
        if(helper is null||patient is null||helper==patient||helper.Person.Dead||helper.Person.Incapacitated||helper.Person.Mobility<.2||helper.Person.Manipulation<.2||MilitaryControlled(helper))return new(false,"需要一名能够移动和搬运、未征召的同伴");
        if(!helper.Person.Can("care"))return new(false,"该物种不具备照料能力");
        if(helper.WaterEvacuating||FloodThreat(helper))return new(false,"照料者需要先撤离水患");
        if(!NeedsRescue(patient))return new(false,"目标不需要搬运或已有医疗床");
        if(ResidentHealth.Bleeding(patient.Person)>0)return new(false,"先原地止血，再搬运到医疗床");
        if(LifeReserved(patientId)||RescueReserved(patientId)||Workers.Any(w=>w!=helper&&w.Person.Activity is {Kind:"treat" or "care_supply" or "feed" or "food_supply"} a&&a.Target==patientId))return new(false,"已有同伴正在照料或搬运");
        var approach=Navigation.FindNearest(helper.Tile,ItemPositions(patient.Tile).Where(Walkable),out var pickup);
        if(approach is null)return new(false,"无法到达伤员旁边");
        var beds=MedicalBeds.Where(c=>IsMedicalBed(c)&&!BedReserved(c)).OrderBy(c=>Math.Abs(c.X-patient.Tile.X)+Math.Abs(c.Z-patient.Tile.Z)).ToArray();
        foreach(var bed in beds)
        {
            var route=Navigation.FindNearest(pickup,ItemPositions(bed).Where(c=>Walkable(c)&&MedicalBedsideFree(c,helper)&&Navigation.CanReach(c,pickup)),out var bedside);
            if(route is null)continue;
            if(helper.Job is not null)Release(helper.Job);CancelResidentActivity(helper);
            helper.Person.Activity=new(){Kind="rescue_pickup",Target=patient.Id,Destination=pickup,RescueBed=bed,Origin=origin};
            foreach(var c in approach)helper.Path.Enqueue(c);helper.CheckedNavigationVersion=Navigation.Version;
            helper.Status="前往救援 → "+patient.Name;return new(true,helper.Status);
        }
        return new(false,beds.Length==0?"没有空闲医疗床；选中一张床设为医疗床":"医疗床不可达，请检查楼梯和通路");
    }
    private bool TryAssignRescue(DemoWorker helper,int ordinaryRank)
    {
        if(ResidentWork.Rank(helper.Person,"care")>=ordinaryRank||helper.Person.Hunger<25||helper.Person.Rest<10||_simulationTime<helper.NextRescueCheck)return false;
        helper.NextRescueCheck=_simulationTime+3;
        foreach(var patient in Workers.Where(NeedsRescue).OrderBy(w=>HealthCare.Urgency(w.Person)).ThenBy(w=>w.Id))
        {var result=RescueResident(helper.Id,patient.Id,"work");if(result.Ok){helper.MedicalIssue="";return true;}helper.MedicalIssue=result.Message;}
        return false;
    }
    private bool UpdateRescue(DemoWorker helper,double dt,ref int budget)
    {
        var a=helper.Person.Activity!;var patient=Workers.FirstOrDefault(w=>w.Id==a.Target);
        if(helper.Person.Mobility<.2||helper.Person.Manipulation<.2||patient is null||patient.Person.Dead||a.RescueBed is not Cell bed||!IsMedicalBed(bed)||ResidentHealth.Bleeding(patient.Person)>0||
            a.Kind=="rescue_pickup"&&(!NeedsRescue(patient)||!ItemPositions(patient.Tile).Contains(a.Destination)))
        {CancelResidentActivity(helper);helper.Status="救援条件改变，已停止搬运";return true;}
        if(helper.Path.Count>0)
        {
            if(!EnsureWorkerRoute(helper,ref budget)){if(helper.Path.Count==0)CancelResidentActivity(helper);return true;}
            if(helper.Path.Count>0)Move(helper,dt*.65);
            if(a.Kind=="rescue_carry")CarryPosition(helper,patient);
            return true;
        }
        if(helper.Tile!=a.Destination){CancelResidentActivity(helper);helper.Status="救援路线中断";return true;}
        if(a.Kind=="rescue_pickup")
        {
            var route=Navigation.FindNearest(helper.Tile,ItemPositions(bed).Where(c=>Walkable(c)&&MedicalBedsideFree(c,helper)&&Navigation.CanReach(c,helper.Tile)),out var destination);
            if(route is null){CancelResidentActivity(helper);return true;}
            if(patient.Job is not null)Release(patient.Job);CancelResidentActivity(patient);
            a.Kind="rescue_carry";a.Destination=destination;foreach(var c in route)helper.Path.Enqueue(c);
            helper.CheckedNavigationVersion=Navigation.Version;CarryPosition(helper,patient);helper.Status="搬运 → "+patient.Name;
        }
        else
        {
            if(!ItemPositions(bed).Contains(helper.Tile)){CancelResidentActivity(helper);return true;}
            DropRescue(helper);patient.Person.MedicalBed=bed;patient.Status="医疗床休养";
            CancelResidentActivity(helper);patient.Status="医疗床休养";Revision++;
        }
        return true;
    }
    private void ValidateRescueArchive()
    {
        if(Workers.Where(w=>w.Person.Activity is {Kind:"rescue_pickup" or "rescue_carry"}).GroupBy(w=>w.Person.Activity!.Target).Any(g=>g.Count()>1))throw new InvalidDataException("重复救援预约");
        var beds=new HashSet<Cell>();
        foreach(var w in Workers)
        {
            if(w.Person.MedicalBed is Cell bed)
            {if(!IsMedicalBed(bed)||!ItemPositions(bed).Contains(w.Tile))w.Person.MedicalBed=null;else if(!beds.Add(bed))throw new InvalidDataException("医疗床重复占用");}
            if(w.Person.Activity is not {Kind:"rescue_pickup" or "rescue_carry"} a)continue;
            var patient=Workers.FirstOrDefault(p=>p.Id==a.Target);
            if(patient is null||patient==w||a.Kind=="rescue_carry"&&(patient.Person.Activity is not null||patient.Job is not null)||a.RescueBed is not Cell reserved||!IsMedicalBed(reserved))
            {CancelResidentActivity(w);continue;}
            if(!beds.Add(reserved))throw new InvalidDataException("医疗床重复预约");
            if(a.Kind=="rescue_carry")CarryPosition(w,patient);
        }
    }
}
