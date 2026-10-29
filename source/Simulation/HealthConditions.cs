namespace NoDwarf.Simulation;

public enum HealthRecovery { Injury, Immunity, Nutrition, Blood, Permanent, Environment }
public readonly record struct HealthEffects(double ConsciousnessLoss=0,double MovingLoss=0,double ManipulationLoss=0,double Pain=0)
{
    public static HealthEffects operator +(HealthEffects a,HealthEffects b)=>new(a.ConsciousnessLoss+b.ConsciousnessLoss,a.MovingLoss+b.MovingLoss,a.ManipulationLoss+b.ManipulationLoss,a.Pain+b.Pain);
}
public sealed record HealthStage(double Start,string Name,HealthEffects Effects);
public sealed record HealthConditionDefinition(string Id,string Name,HealthRecovery Recovery,double Maximum,HealthStage[] Stages,
    double Worsening=0,double ImmunityGain=0,double Healing=0,double TreatmentDuration=0)
{
    public int StageIndex(double severity){for(int i=Stages.Length-1;i>=0;i--)if(severity>=Stages[i].Start)return i;return 0;}
    public HealthStage Stage(double severity)=>Stages[StageIndex(severity)];
    public double StageProgress(double severity)
    {int index=StageIndex(severity);double start=Stages[index].Start,end=index+1<Stages.Length?Stages[index+1].Start:Maximum;return Math.Clamp((severity-start)/(end-start)*100,0,100);}
}
public sealed class HealthTreatment
{
    // Remaining simulation seconds, so pause/save cannot extend or consume a treatment.
    public double Quality {get;set;}
    public double Remaining {get;set;}
    public bool Valid()=>double.IsFinite(Quality)&&Quality>=0&&Quality<=1&&double.IsFinite(Remaining)&&Remaining>=0&&Remaining<=HealthConditions.All.Max(d=>d.TreatmentDuration);
}
public sealed record HealthConditionState(HealthConditionDefinition Definition,string Part,double Severity,double? Immunity,HealthTreatment? Treatment)
{
    public HealthStage Stage=>Definition.Stage(Severity);
}
public static class HealthConditions
{
    private static HealthStage[] InjuryStages()=>[new(0,"损伤",new())];
    public static readonly HealthConditionDefinition[] All=[
        new("fever","热病",HealthRecovery.Immunity,300,[new(0,"初期",new(.03,.03,.08,3)),new(100,"加重",new(.12,.1,.25,8)),new(200,"重症",new(.85,.25,.4,15))],.08,.06,.12,600),
        new("wound_infection","伤口感染",HealthRecovery.Immunity,300,[new(0,"初期",new(0,0,0,6)),new(100,"加重",new(.05,.15,.1,18)),new(200,"重症",new(.85,.35,.3,35))],.1,.05,.1,600),
        new("malnutrition","营养不良",HealthRecovery.Nutrition,300,[new(0,"轻度",new(0,.05,.05)),new(100,"衰弱",new(.1,.25,.2)),new(200,"重度",new(.85,.5,.4))]),
        new("dehydration","脱水",HealthRecovery.Environment,300,[new(0,"口渴",new(0,.05,.05)),new(100,"脱水",new(.1,.2,.2)),new(200,"危急",new(.85,.4,.4))]),
        new("cold","失温",HealthRecovery.Environment,300,[new(0,"寒冷",new(0,.1,.1)),new(100,"失温",new(.15,.3,.3)),new(200,"危急",new(.85,.5,.5))]),
        new("heat","中暑",HealthRecovery.Environment,300,[new(0,"炎热",new(0,.05,.05)),new(100,"中暑",new(.15,.2,.2)),new(200,"危急",new(.85,.4,.4))]),
        new("blood_loss","失血",HealthRecovery.Blood,100,[new(0,"轻微",new()),new(30,"中等",new()),new(60,"严重",new()),new(80,"危急",new())]),
        new("cut","割伤",HealthRecovery.Injury,100,InjuryStages(),TreatmentDuration:600),
        new("fracture","骨折",HealthRecovery.Injury,100,InjuryStages(),TreatmentDuration:1200),
        new("burn","烧伤",HealthRecovery.Injury,100,InjuryStages(),TreatmentDuration:600),
        new("missing","部位缺失",HealthRecovery.Permanent,100,[new(0,"永久",new())]),
        new("sequela","后遗症",HealthRecovery.Permanent,100,[new(0,"永久",new())])];
    private static readonly Dictionary<string,HealthConditionDefinition> ById=All.ToDictionary(d=>d.Id);
    public static HealthConditionDefinition? Find(string id)=>ById.GetValueOrDefault(id);
    // One view over existing authoritative data; do not duplicate blood or nutrition in saves.
    public static IEnumerable<HealthConditionState> States(ResidentProfile p)
    {
        foreach(var d in p.Diseases)yield return new(Find(d.Id)!,"",d.Progress,d.Immunity,d.Treatment);
        if(p.Malnutrition>0)yield return new(Find("malnutrition")!,"",p.Malnutrition,null,null);
        foreach(var (id,value) in LifeHealth.Exposures(p))if(value>0)yield return new(Find(id)!,"",value,null,null);
        if(p.Blood<100)yield return new(Find("blood_loss")!,"",100-p.Blood,null,null);
        foreach(var i in p.Injuries)
        {
            if(i.Severity>i.PermanentSeverity)yield return new(Find(i.Kind)!,i.Part,i.Severity-i.PermanentSeverity,null,i.Treatment);
            if(i.PermanentSeverity>0)yield return new(Find("sequela")!,i.Part,i.PermanentSeverity,null,null);
        }
    }
    // Tick/movement queries avoid the allocating UI projection above.
    public static HealthEffects Effects(ResidentProfile p)
    {
        var effects=new HealthEffects();
        foreach(var d in p.Diseases)effects+=Find(d.Id)!.Stage(d.Progress).Effects;
        if(p.Malnutrition>0)effects+=Find("malnutrition")!.Stage(p.Malnutrition).Effects;
        if(p.Dehydration>0)effects+=Find("dehydration")!.Stage(p.Dehydration).Effects;
        if(p.ColdExposure>0)effects+=Find("cold")!.Stage(p.ColdExposure).Effects;
        if(p.HeatExposure>0)effects+=Find("heat")!.Stage(p.HeatExposure).Effects;
        return effects;
    }
    public static string EffectText(HealthEffects e)
    {
        var terms=new List<string>();
        if(e.ConsciousnessLoss>0)terms.Add($"意识 −{e.ConsciousnessLoss:P0}");
        if(e.MovingLoss>0)terms.Add($"移动 −{e.MovingLoss:P0}");
        if(e.ManipulationLoss>0)terms.Add($"操作 −{e.ManipulationLoss:P0}");
        if(e.Pain>0)terms.Add($"疼痛 +{e.Pain:F0}%");
        return string.Join(" · ",terms);
    }
}
