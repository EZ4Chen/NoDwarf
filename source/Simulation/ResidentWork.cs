namespace NoDwarf.Simulation;

public sealed record ResidentWorkType(string Id,string Name,string Skill,bool Available=true);
public static class ResidentWork
{
    public static readonly string[] Modes=["anything","sleep","work","recreation"];
    // This order is also the deterministic tie breaker shown in the work table.
    public static readonly ResidentWorkType[] Types=[
        new("doctor","医疗","medicine"),new("care","照料",""),new("construction","建造","construction"),new("mining","采矿","mining"),
        new("growing","种植","growing"),new("forestry","伐木","woodwork"),new("cooking","烹饪","cooking"),
        new("crafting","加工","woodwork"),new("smelting","冶炼","smelting"),new("smithing","锻造","smithing"),new("hauling","搬运",""),
        new("research","研究","research"),new("butchery","屠宰","cooking",false)];
    public static Dictionary<string,int> DefaultPriorities()=>Types.ToDictionary(t=>t.Id,t=>t.Id is "doctor" or "research" or "butchery"?0:t.Id=="care"?2:t.Id=="hauling"?4:3);
    public static string[] FreeDay()=>Enumerable.Repeat("anything",24).ToArray();
    public static string[] DayShift()=>Enumerable.Range(0,24).Select(h=>h<6||h>=22?"sleep":h>=8&&h<18?"work":h>=18&&h<20?"recreation":"anything").ToArray();
    public static string ModeName(string mode)=>mode switch{"sleep"=>"休息","work"=>"工作","recreation"=>"娱乐",_=>"无固定"};
    public static string JobType(DemoJob job)=>job.Processing?(ProcessingCatalog.Find(job.Recipe)?.Station switch {BuildKind.Stove=>"cooking",BuildKind.Anvil=>"smithing",BuildKind.Furnace=>"smelting",_=>"crafting"}):job.Haul||job.Refuel?"hauling":job.Farming||job.PlantTree?"growing":job.Chop?"forestry":job.Demolish||job.Build is not null?"construction":job.Dig?"mining":job.Cook?"cooking":job.Smelt?"smelting":"smithing";
    public static int Rank(ResidentProfile p,string type)
    {
        int index=-1;
        for(int i=0;i<Types.Length;i++)if(Types[i].Id==type){index=i;break;}
        if(index<0||!Types[index].Available)return int.MaxValue;
        int priority=p.WorkPriorities.GetValueOrDefault(type);
        return priority==0?int.MaxValue:priority*Types.Length+index;
    }
}

public sealed partial class DemoWorld
{
    public string ScheduledMode(DemoWorker w)=>w.Person.Schedule[Calendar.Hour];
    private ResidentResult SetResidentSchedule(DemoWorker w,ResidentCommand command)
    {
        string[] hours;
        if(command.Hours is not null)
        {
            if(command.Hours.Length!=24||command.Hours.Any(h=>!ResidentWork.Modes.Contains(h)))return new(false,"作息必须包含 24 个有效小时");
            hours=(string[])command.Hours.Clone();
        }
        else
        {
            if(command.Hour is <0 or >23||!ResidentWork.Modes.Contains(command.Target))return new(false,"请选择 0—23 点和有效作息");
            hours=(string[])w.Person.Schedule.Clone();hours[command.Hour]=command.Target;
        }
        w.Person.Schedule=hours;w.NextPolicyCheck=0;
        return new(true,"已更新每日作息");
    }
    private ResidentResult SetResidentPriority(DemoWorker w,ResidentCommand command)
    {
        var type=ResidentWork.Types.FirstOrDefault(t=>t.Id==command.Target);
        if(type is null||!type.Available)return new(false,"这类工作尚未开放");
        if(command.Priority is <0 or >4)return new(false,"优先级为 1—4，0 表示不做");
        w.Person.WorkPriorities[type.Id]=command.Priority;w.NextJobSearch=0;w.NextPolicyCheck=0;
        return new(true,type.Name+(command.Priority==0?"：不做":$"：优先级 {command.Priority}"));
    }
    // Called before activity/job execution. Pure checks on most ticks; route requests
    // happen only when entering a scheduled activity, never once per frame.
    private bool ApplyResidentSchedule(DemoWorker w,ref int budget)
    {
        var p=w.Person;if(p.Dead||p.Incapacitated)return false;
        string mode=ScheduledMode(w);var a=p.Activity;
        if(a?.Origin=="emergency")return false;
        if(w.Job is {} job&&ResidentWork.Rank(p,ResidentWork.JobType(job))==int.MaxValue)Release(job);
        if(a?.Origin=="work"&&ResidentWork.Rank(p,a.Kind=="research"?"research":a.Supply is "splint" or "prosthesis"?"doctor":a.Kind is "life_supply" or "life_use" or "water_fill" or "food_supply" or "feed" or "rescue_pickup" or "rescue_carry"?"care":"doctor")==int.MaxValue)
        {CancelResidentActivity(w);p.PausedResearch=null;a=null;}
        if(p.PausedResearch?.Origin=="work"&&p.WorkPriorities["research"]==0)p.PausedResearch=null;
        if(a?.Origin=="schedule"&&a.Kind!=(mode=="sleep"?"rest":mode=="recreation"?"relax":""))
        {
            // A free period allows an already sleeping resident to finish sleeping.
            if(!(mode=="anything"&&a.Kind=="rest")){CancelResidentActivity(w);a=null;}
        }
        if(mode is not ("sleep" or "recreation"))return false;
        // Direct player orders and essential self-care take precedence until complete.
        if(a is not null&&a.Origin is "manual" or "need")return false;
        if(w.Job is not null)Release(w.Job);
        if(a?.Origin is "work" or "personality")
        {
            if(a.Kind=="research")p.PausedResearch=a;
            CancelResidentActivity(w);a=null;
        }
        if(a is not null)return false;
        bool needsActivity=mode=="sleep"?p.Rest<95:p.Recreation<95;
        if(needsActivity&&_simulationTime>=w.NextPolicyCheck&&budget>0)
        {
            w.NextPolicyCheck=_simulationTime+2;budget--;
            var paused=p.PausedResearch;
            if(BeginResidentActivity(w,mode=="sleep"?"rest":"relax",origin:"schedule").Ok)
            {p.PausedResearch=paused;return false;}
        }
        w.Status=mode=="sleep"?"休息时段":"娱乐时段";return true;
    }
    private DemoWorker? AutomaticPatient(DemoWorker medic)
    {
        return Workers.Where(o=>!o.Person.Dead&&!RescueReserved(o.Id)&&!LifeReserved(o.Id)&&HealthCare.Needed(o.Person)&&
            (o==medic||o.Job is null&&o.Path.Count==0)&&
            !Workers.Any(other=>other!=medic&&other.Person.Activity is {Kind:"treat" or "care_supply"} care&&care.Target==o.Id)&&
            CanReachAny(medic.Tile,ItemPositions(o.Tile).Where(Walkable).ToArray()))
            .OrderBy(o=>HealthCare.Urgency(o.Person)).ThenBy(o=>HealthCare.TimeToBleedOut(o.Person)).ThenByDescending(o=>medic.Person.Affinity(o.Id)).ThenBy(o=>Math.Abs(o.Tile.X-medic.Tile.X)+Math.Abs(o.Tile.Z-medic.Tile.Z)).FirstOrDefault();
    }
    private bool AssignPersonalWork(DemoWorker w,int ordinaryRank)
    {
        int doctorRank=ResidentWork.Rank(w.Person,"doctor"),researchRank=ResidentWork.Rank(w.Person,"research");
        if(doctorRank==int.MaxValue||!Workers.Any(o=>!o.Person.Dead&&HealthCare.Needed(o.Person)))w.MedicalIssue="";
        DemoWorker? patient=doctorRank<ordinaryRank&&_simulationTime>=w.NextMedicalCheck?AutomaticPatient(w):null;
        bool research=Research.Active!=""&&researchRank<ordinaryRank;
        if(TryAssignFeeding(w,Math.Min(ordinaryRank,Math.Min(patient is null?int.MaxValue:doctorRank,research?researchRank:int.MaxValue))))return true;
        if(patient is not null&&(!research||doctorRank<researchRank))
        {
            var result=BeginResidentActivity(w,"treat",patient.Id,origin:"work");
            if(result.Ok){w.MedicalIssue="";return true;}
            w.MedicalIssue=result.Message;w.Status=result.Message;w.NextMedicalCheck=_simulationTime+3;
            // A failed care attempt must not monopolize the worker (including bandage production).
        }
        if(TryAssignFeeding(w,Math.Min(ordinaryRank,research?researchRank:int.MaxValue)))return true;
        if(TryAssignRescue(w,Math.Min(ordinaryRank,research?researchRank:int.MaxValue)))return true;
        if(!research)return false;
        if(w.Person.Activity is {Kind:"research",Origin:"work"})return true;
        var paused=w.Person.PausedResearch;
        if(BeginResidentActivity(w,"research",origin:"work").Ok&&paused is not null)w.Person.Activity!.Progress=paused.Progress;
        return true;
    }
}
