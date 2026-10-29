using System.Text.Json.Serialization;
namespace NoDwarf.Simulation;

public static class DiseaseCatalog
{
    public static readonly HealthConditionDefinition[] All=HealthConditions.All.Where(d=>d.Recovery==HealthRecovery.Immunity).ToArray();
    public static HealthConditionDefinition? Find(string id)=>All.FirstOrDefault(d=>d.Id==id);
}
public sealed class ResidentDisease
{
    public string Id {get;set;}="fever";
    // Continuous burden across three 100-point stages. Recovery crosses stages backwards.
    public double Progress {get;set;}=5;
    public double Immunity {get;set;}
    public HealthTreatment Treatment {get;set;}=new();
    [JsonIgnore] public int Stage=>DiseaseCatalog.Find(Id)!.StageIndex(Progress);
    [JsonIgnore] public double StageProgress=>DiseaseCatalog.Find(Id)!.StageProgress(Progress);
    [JsonIgnore] public string StageName=>DiseaseCatalog.Find(Id)!.Stage(Progress).Name;
}
public static class ResidentHealth
{
    public static double Bleeding(ResidentProfile p)
    {double rate=0;foreach(var i in p.Injuries)if(!i.Treated){if(i.Kind=="cut")rate+=Math.Max(0,i.Severity-i.PermanentSeverity)*.001;else if(i.Kind=="missing"&&i.FreshAmputation)rate+=i.Part.EndsWith("hand")||i.Part.EndsWith("foot")?.08:.2;}return rate;}
    public static double ConditionCapacity(ResidentProfile p)
    {
        return Math.Clamp(1-HealthConditions.Effects(p).ConsciousnessLoss,.15,1);
    }

    public static bool AddDisease(ResidentProfile p,string id)
    {
        if(p.Dead||DiseaseCatalog.Find(id) is null||p.Diseases.Any(d=>d.Id==id))return false;
        p.Diseases.Add(new(){Id=id});p.DecisionVersion++;return true;
    }
    public static void Step(ResidentProfile p,double seconds)
    {
        if(seconds<=0||!double.IsFinite(seconds)||p.Dead)return;
        // Hysteresis: low food worsens, sufficient food heals; intermediate hunger holds steady.
        p.Malnutrition=Math.Clamp(p.Malnutrition+seconds*(p.Hunger<10?.025:p.Hunger>=40?-.05:0),0,300);
        double bleeding=Bleeding(p);
        p.Blood=Math.Clamp(p.Blood+seconds*(bleeding>0?-bleeding:p.Hunger>=40?.015:.005),0,100);
        // Blood loss can be fatal. Do not heal through death in the same update.
        if(p.Dead)return;
        foreach(var d in p.Diseases)
        {
            var def=DiseaseCatalog.Find(d.Id)!;
            double gain=def.ImmunityGain*(p.Hunger>=40?1:.5)*(p.Activity?.Kind=="rest"||p.MedicalBed is not null?1.25:1);
            double beforeImmune=d.Immunity>=100?0:Math.Min(seconds,(100-d.Immunity)/gain);
            double protectedTime=Math.Min(beforeImmune,d.Treatment.Remaining);
            double growth=def.Worsening*(protectedTime*(1-.8*d.Treatment.Quality)+(beforeImmune-protectedTime));
            d.Progress=Math.Min(def.Maximum,d.Progress+growth);
            d.Immunity=Math.Min(100,d.Immunity+gain*seconds);
            d.Progress=Math.Max(0,d.Progress-def.Healing*(seconds-beforeImmune));
            d.Treatment.Remaining=Math.Max(0,d.Treatment.Remaining-seconds);
        }
        p.Diseases.RemoveAll(d=>d.Progress<=0&&d.Immunity>=100);
        foreach(var i in p.Injuries)
        {
            if(i.Kind=="missing")continue;
            if(!i.Treated&&i.Severity>=25&&!i.InfectionSeeded)
            {
                i.UntreatedSeconds=Math.Min(600,i.UntreatedSeconds+seconds);
                if(i.UntreatedSeconds>=600&&i.Kind is "cut" or "burn")
                {AddDisease(p,"wound_infection");i.InfectionSeeded=true;}
            }
            double protectedTime=Math.Min(seconds,i.Treatment.Remaining);
            double baseRate=i.Treated?.008:i.Kind=="fracture"?0:.002;
            double healing=(i.Kind=="fracture"&&!i.Fixed?.35:1)*(protectedTime*(.02+.03*i.Treatment.Quality)+(seconds-protectedTime)*baseRate)*
                (p.Traits.Contains("resilient")?1.2:1)*(1-.5*p.Malnutrition/300);
            i.Severity=Math.Max(i.PermanentSeverity,i.Severity-healing);
            i.Treatment.Remaining=Math.Max(0,i.Treatment.Remaining-seconds);
        }
        p.Injuries.RemoveAll(i=>i.Severity<=0&&i.Kind!="missing");
    }
    public static bool Valid(ResidentProfile p)
    {
        static bool Range(double n,double max)=>double.IsFinite(n)&&n>=0&&n<=max;
        return Range(p.Malnutrition,300)&&p.Diseases is not null&&p.Diseases.Count<=DiseaseCatalog.All.Length&&
            p.Diseases.All(d=>d is not null&&DiseaseCatalog.Find(d.Id) is not null&&Range(d.Progress,300)&&Range(d.Immunity,100)&&d.Treatment is not null&&d.Treatment.Valid())&&
            p.Diseases.Select(d=>d.Id).Distinct().Count()==p.Diseases.Count&&
            p.Injuries is not null&&p.Injuries.All(i=>i is not null&&Range(i.PermanentSeverity,20)&&i.PermanentSeverity<=i.Severity&&Range(i.UntreatedSeconds,600)&&i.Treatment is not null&&i.Treatment.Valid()&&
                (i.Kind!="missing"||i.PermanentSeverity==0));
    }
}
public sealed partial class DemoWorld
{
    public ResidentResult InfectResident(string id,string disease)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==id);
        if(w is null)return new(false,"居民不存在");
        if(!ResidentHealth.AddDisease(w.Person,disease))return new(false,"未知疾病、已经患病或居民已死亡");
        Log(w.Name+"出现"+DiseaseCatalog.Find(disease)!.Name);Revision++;
        return new(true,"已添加疾病，病情和免疫随模拟时间发展");
    }
}
