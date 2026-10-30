namespace NoDwarf.Simulation;
public static partial class HealthCare
{
    public static bool WoundNeedsCare(ResidentInjury i)=>i.Kind=="missing"?i.FreshAmputation&&!i.Treated:i.Severity>i.PermanentSeverity&&(!i.Treated||i.Treatment.Remaining<=0);
    public static int Urgency(ResidentProfile p)
    {
        double bleed=ResidentHealth.Bleeding(p);
        if(bleed>0&&(p.Blood<40||p.Blood/bleed<180))return 0;
        if(bleed>0)return 1;
        if(p.Incapacitated)return 2;
        if(p.Diseases.Any(d=>d.Stage>=2))return 3;
        return 4;
    }
    public static double TimeToBleedOut(ResidentProfile p)=>ResidentHealth.Bleeding(p)>0?p.Blood/ResidentHealth.Bleeding(p):double.MaxValue;
    public static bool DiseaseNeedsCare(ResidentDisease d)=>d.Progress>0&&d.Immunity<100&&d.Treatment.Remaining<=0;
    public static bool Needed(ResidentProfile p)=>!p.Dead&&(p.Injuries.Any(WoundNeedsCare)||p.Diseases.Any(DiseaseNeedsCare));
    public static double Quality(ResidentProfile medic)=>Math.Clamp((.35+.03*medic.Level("medicine"))*medic.Manipulation*medic.Consciousness,.1,1);
    // Bleeding/unhandled wounds first, then illnesses, then renewal of existing dressings.
    public static string Treat(ResidentProfile p,double quality)
    {
        var wound=p.Injuries.Where(WoundNeedsCare).OrderBy(i=>i.Treated?1:0).ThenByDescending(i=>i.Kind=="missing"?300:i.Kind=="cut"?i.Severity+100:i.Severity).FirstOrDefault();
        var disease=p.Diseases.Where(DiseaseNeedsCare).OrderByDescending(d=>d.Progress).FirstOrDefault();
        if(wound is not null&&(!wound.Treated||disease is null))
        {
            wound.Treated=true;wound.Treatment=new(){Quality=Math.Clamp(quality,0,1),Remaining=HealthConditions.Find(wound.Kind)!.TreatmentDuration};
            return ResidentCatalog.PartName(wound.Part)+"的"+ResidentCatalog.InjuryName(wound.Kind);
        }
        if(disease is not null)
        {
            disease.Treatment=new(){Quality=Math.Clamp(quality,0,1),Remaining=HealthConditions.Find(disease.Id)!.TreatmentDuration};
            return HealthConditions.Find(disease.Id)!.Name;
        }
        return "";
    }
}
