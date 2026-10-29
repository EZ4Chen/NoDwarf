using System.Text.Json.Serialization;
namespace NoDwarf.Simulation;

public sealed record ResidentSkill(string Id, string Name, bool HasWork);
public sealed record TalentNode(string Id, string Skill, int Rank, int Level, int Cost, string Research)
{
    public string Name => $"{ResidentCatalog.Skill(Skill).Name}专长 {Rank}";
    public string Effect => Skill=="combat"?$"近战伤害 +{Rank * 15}%（总计）":$"对应工作效率 +{Rank * 15}%（总计）";
}
public sealed record ResearchDefinition(string Id, string Name, int Cost, string[] Prerequisites);
public static class ResidentCatalog
{
    public const int ExperiencePerPoint = 1000;
    public static readonly ResidentSkill[] Skills = [
        new("mining","采矿",true), new("construction","建造",true), new("smelting","冶炼",true),
        new("smithing","锻造",true), new("woodwork","木工",true), new("growing","种植",true),
        new("cooking","烹饪",true), new("medicine","医疗",true), new("combat","战斗",true),
        new("social","交涉",true), new("research","研究",true)];
    public static readonly ResearchDefinition[] Research = [
        new("foundations","基础研究方法",80,[]), new("industry","工艺改良",150,["foundations"]),
        new("living","生活与协作",150,["foundations"]), new("precision","精密技术",250,["industry","living"])];
    public static readonly TalentNode[] Talents = Skills.SelectMany((s,i) => Enumerable.Range(1,3).Select(rank =>
        new TalentNode($"{s.Id}.{rank}",s.Id,rank,rank==1?3:rank==2?6:10,rank,
            rank==1?"":rank==3?"precision":i<5?"industry":"living"))).ToArray();
    public static readonly (string Id,string Name)[] Parts = [
        ("head","头部"),("torso","躯干"),("left_arm","左臂"),("right_arm","右臂"),
        ("left_hand","左手"),("right_hand","右手"),("left_leg","左腿"),("right_leg","右腿"),
        ("left_foot","左脚"),("right_foot","右脚")];
    public static readonly (string Id,string Name)[] Slots = [
        ("head","头部"),("body","身体"),("legs","腿部"),("hands","手部"),("feet","脚部"),("main","主手"),("off","副手")];
    public static readonly Dictionary<string,string> Traits = new()
    { ["diligent"]="勤快：工作效率 +5%",["studious"]="好学：工作经验 +10%",["calm"]="沉稳：疼痛造成的心情损失减半",
      ["sociable"]="合群：交流恢复更多娱乐",["resilient"]="坚韧：伤势恢复速度 +20%" };
    public static ResidentSkill Skill(string id) => Skills.First(s=>s.Id==id);
    public static int LevelCost(int level) => 100 + 20 * level * level;
    public static int Level(long xp) { int n=0; while(n<20 && xp>=LevelCost(n)) xp-=LevelCost(n++); return n; }
    public static long LevelStart(int level) => Enumerable.Range(0,level).Sum(i=>(long)LevelCost(i));
    public static string PartName(string id) => Parts.FirstOrDefault(p=>p.Id==id).Name ?? id;
    public static string InjuryName(string id) => id switch { "cut"=>"割伤", "fracture"=>"骨折", "burn"=>"烧伤", "missing"=>"缺失", _=>id };
}
public sealed class ResidentInjury
{
    public string Part {get;set;}="left_hand";
    public string Kind {get;set;}="cut";
    public double Severity {get;set;}
    public double PermanentSeverity {get;set;}
    public double UntreatedSeconds {get;set;}
    public bool InfectionSeeded {get;set;}
    public HealthTreatment Treatment {get;set;}=new();
    public bool Fixed {get;set;}
    public bool FreshAmputation {get;set;}
    public bool Treated {get;set;}
}
public sealed record ResidentMemory(string Key,string Text,double Time)
{
    public string EventId {get;init;}="";
    public string Kind {get;init;}="experience";
    public string Target {get;init;}="";
    public string Actor {get;init;}="";
    public string Source {get;init;}="simulation";
    public string Perspective {get;init;}="self";
    public int Importance {get;init;}=30;
    public int Count {get;init;}=1;
    public double FirstTime {get;init;}
    public double LastUsed {get;init;}
}
public sealed class ResidentRelation
{
    public string Target {get;set;}="";
    public string Kind {get;set;}="同伴";
    public int Opinion {get;set;}
    public double LastSocial {get;set;}=-10000;
}
public sealed record MoodEffect(string Key,string Text,int Amount,double Until);
public sealed class ResidentActivity
{
    public string Kind {get;set;}="rest";
    public string Origin {get;set;}="manual";
    public string Target {get;set;}="";
    public int ItemId {get;set;}
    public string BodyPart {get;set;}="";
    public string Supply {get;set;}="";
    public Cell? SourceCell {get;set;}
    public Cell? RescueBed {get;set;}
    public Cell Destination {get;set;}
    public double Progress {get;set;}
}
public sealed partial class ResidentProfile
{
    public int Age {get;set;}=25;
    public string Childhood {get;set;}="聚落孩子";
    public string Adulthood {get;set;}="新来的居民";
    public List<string> Traits {get;set;}=new();
    public Dictionary<string,long> SkillXp {get;set;}=ResidentCatalog.Skills.ToDictionary(s=>s.Id,_=>0L);
    public double ExperienceRemainder {get;set;}
    public long GrowthXp {get;set;}
    public int SpentPoints {get;set;}
    public Dictionary<string,int> Talents {get;set;}=new();
    public string AutoSkill {get;set;}="";
    public string[] Schedule {get;set;}=ResidentWork.FreeDay();
    public Dictionary<string,int> WorkPriorities {get;set;}=ResidentWork.DefaultPriorities();
    public long DecisionVersion {get;set;}
    public List<ResidentInjury> Injuries {get;set;}=new();
    public double Blood {get;set;}=100;
    public List<ResidentDisease> Diseases {get;set;}=new();
    public double Malnutrition {get;set;}
    public double Thirst {get;set;}=100;
    public double Dehydration {get;set;}
    public double ColdExposure {get;set;}
    public double HeatExposure {get;set;}
    public double CriticalSeconds {get;set;}
    public double InfectionExposure {get;set;}
    public string DeathCause {get;set;}="";
    public bool Isolated {get;set;}
    public Dictionary<string,double> Prostheses {get;set;}=new();
    public double Hunger {get;set;}=90;
    public Cell? HomeBed {get;set;}
    public Cell? MedicalBed {get;set;}
    public string FoodIssue {get;set;}="";
    public double Rest {get;set;}=90;
    public double Recreation {get;set;}=80;
    public List<MoodEffect> MoodEffects {get;set;}=new();
    public List<ResidentMemory> Memories {get;set;}=new();
    public List<ResidentMemorySummary> MemorySummaries {get;set;}=new();
    public Dictionary<string,double> HelpedAt {get;set;}=new();
    public List<ResidentRelation> Relations {get;set;}=new();
    public ResidentActivity? Activity {get;set;}
    // One suspended research activity only; ordinary needs remain locally controlled.
    public ResidentActivity? PausedResearch {get;set;}
    public int ResearchWork {get;set;}
    [JsonIgnore] public int Points => (int)(GrowthXp / ResidentCatalog.ExperiencePerPoint) - SpentPoints;
    [JsonIgnore] public Dictionary<string,double> GearBonuses {get;}=new();
    [JsonIgnore] public double CarriedWeight {get;set;}
    [JsonIgnore] public double Capacity => 30;
    [JsonIgnore] public double Manipulation => (Integrity("left_arm")*Integrity("left_hand") + Integrity("right_arm")*Integrity("right_hand"))/2*Math.Clamp(1-HealthConditions.Effects(this).ManipulationLoss,0,1);
    [JsonIgnore] public double Mobility => (Integrity("left_leg")*Integrity("left_foot") + Integrity("right_leg")*Integrity("right_foot"))/2*Math.Clamp(1-HealthConditions.Effects(this).MovingLoss,0,1);
    [JsonIgnore] public double Consciousness => Math.Min(Math.Min(Integrity("head"),Integrity("torso")),Math.Min(Blood/100,ResidentHealth.ConditionCapacity(this)));
    [JsonIgnore] public bool Dead => DeathCause!=""||Consciousness<=0;
    [JsonIgnore] public bool Incapacitated => Consciousness<.2||Pain>=85;
    [JsonIgnore] public double Pain
    {
        get { double sum=0; foreach(var injury in Injuries) if(injury.Kind!="missing") sum+=Math.Max(0,injury.Severity-injury.PermanentSeverity)*(injury.Fixed?.5:1); return Math.Min(100,sum*.5+HealthConditions.Effects(this).Pain); }
    }
    [JsonIgnore] public double Mood => Math.Clamp(70 + MoodEffects.Sum(e=>e.Amount) - Math.Max(0,40-Hunger)*.5 - Math.Max(0,40-Rest)*.5 - Math.Max(0,40-Recreation)*.4 - Pain*(Traits.Contains("calm")?.15:.3),0,100);
    [JsonIgnore] public double MoveRate
    {
        get { double consciousness=Consciousness; return Incapacitated ? 0 : Math.Clamp(Mobility * consciousness * Math.Min(1,Capacity/Math.Max(1,CarriedWeight)),.08,1); }
    }
    // Injuries are mutable (including treatment and recovery); compute from live data
    // without allocating a capturing predicate/iterator for every limb on every tick.
    public double Integrity(string part)
    {
        double severity=0;
        foreach(var injury in Injuries) if(injury.Part==part) severity+=injury.Kind=="fracture"&&injury.Fixed?(injury.Severity-injury.PermanentSeverity)*.85+injury.PermanentSeverity:injury.Severity;
        return Math.Max(Prostheses.GetValueOrDefault(part),Math.Clamp(1-severity/100,0,1));
    }
    public int Level(string skill) => ResidentCatalog.Level(SkillXp.GetValueOrDefault(skill));
    public double WorkRate(string skill)
    {
        double consciousness=Consciousness;
        if(Incapacitated)return 0;
        double manipulation=skill=="social"?1:Manipulation;
        if(skill!="social"&&manipulation<.05)return 0;
        return (1 + Level(skill)*.02 + Talents.GetValueOrDefault(skill)*.15) * manipulation * consciousness *
            (Traits.Contains("diligent")?1.05:1) * (Hunger<20?.75:1) * (Rest<20?.75:1);
    }
    public void Earn(string skill,int amount)
    {
        if(amount<=0 || !SkillXp.ContainsKey(skill))return;
        int previousLevel=Level(skill),previousPoints=Points;
        double raw=amount*(Traits.Contains("studious")?1.1:1)+ExperienceRemainder;
        int gained=(int)Math.Floor(raw+1e-9);ExperienceRemainder=Math.Max(0,raw-gained);
        SkillXp[skill]=Math.Min(1000000000,SkillXp[skill]+gained);
        GrowthXp=Math.Min(1000000000,GrowthXp+gained);
        // Small XP ticks do not invalidate an otherwise unchanged AI decision.
        if(Level(skill)!=previousLevel||Points!=previousPoints)DecisionVersion++;
    }
    public void Feeling(string key,string text,int amount,double until)
    { MoodEffects.RemoveAll(e=>e.Key==key);MoodEffects.Add(new(key,text,amount,until));if(MoodEffects.Count>8)MoodEffects.RemoveAt(0); }
    public static ResidentProfile Create(int index)
    {
        var p=new ResidentProfile {Age=24+Math.Abs(index%25)};
        switch(Math.Abs(index%3))
        {
            case 0:p.Childhood="矿镇孩子";p.Adulthood="铁匠学徒";p.Traits=["diligent","calm"];p.SkillXp["mining"]=400;p.SkillXp["smithing"]=680;break;
            case 1:p.Childhood="行商家庭";p.Adulthood="聚落抄写员";p.Traits=["studious","sociable"];p.SkillXp["research"]=680;p.SkillXp["social"]=400;break;
            default:p.Childhood="林场孩子";p.Adulthood="木匠帮工";p.Traits=["resilient","diligent"];p.SkillXp["woodwork"]=680;p.SkillXp["construction"]=400;break;
        }
        p.Remember("arrival","来到聚落，开始新的生活。",0);return p;
    }
    public void Validate()
    {
        static bool Valid(double v,double max=100)=>double.IsFinite(v)&&v>=0&&v<=max;
        static bool ValidOrigin(string origin)=>origin is "manual" or "need" or "schedule" or "work" or "emergency" or "personality";
        ValidateMemory();
        if(Age is <0 or >200 || Childhood is null || Adulthood is null || Childhood.Length>100 || Adulthood.Length>100 ||
            Schedule is null||Schedule.Length!=24||Schedule.Any(h=>!ResidentWork.Modes.Contains(h))||
            MedicalBed is Cell medicalBed&&!TerrainMap.InBounds(medicalBed)||
            FoodIssue is null||FoodIssue.Length>200||HomeBed is Cell bed&&!TerrainMap.InBounds(bed)||
            WorkPriorities is null||WorkPriorities.Count!=ResidentWork.Types.Length||ResidentWork.Types.Any(t=>!WorkPriorities.ContainsKey(t.Id))||
            WorkPriorities.Values.Any(v=>v is <0 or >4)||ResidentWork.Types.Any(t=>!t.Available&&WorkPriorities[t.Id]!=0)||
            SkillXp is null || SkillXp.Count!=ResidentCatalog.Skills.Length || ResidentCatalog.Skills.Any(s=>!SkillXp.ContainsKey(s.Id)) || SkillXp.Values.Any(v=>v<0||v>1000000000) ||
            GrowthXp is <0 or >1000000000 || !Valid(ExperienceRemainder,1)||ExperienceRemainder>=1 || SpentPoints<0 || Points<0 || DecisionVersion<0 || Talents is null ||
            Talents.Any(t=>!SkillXp.ContainsKey(t.Key)||t.Value is <1 or >3||Level(t.Key)<(t.Value==1?3:t.Value==2?6:10)) ||
            SpentPoints!=Talents.Values.Sum(n=>n*(n+1)/2) || AutoSkill is null || AutoSkill!=""&&!SkillXp.ContainsKey(AutoSkill) ||
            Traits is null || Traits.Count>3 || Traits.Distinct().Count()!=Traits.Count || Traits.Any(t=>!ResidentCatalog.Traits.ContainsKey(t)) ||
            !LifeHealth.Valid(this)||!ResidentHealth.Valid(this)||!Valid(Blood)||!Valid(Hunger)||!Valid(Rest)||!Valid(Recreation)||ResearchWork<0 ||
            Injuries is null||Injuries.Count>40||Injuries.Any(i=>i is null||!ResidentCatalog.Parts.Any(p=>p.Id==i.Part)||i.Kind is not ("cut" or "burn" or "fracture" or "missing")||!Valid(i.Severity)) ||
            Memories is null||Memories.Count>24||Memories.Any(m=>m is null||m.Key is null||m.Text is null||m.Key.Length>100||m.Text.Length>400||!Valid(m.Time,double.MaxValue)) ||
            Relations is null||Relations.Count>32||Relations.Any(r=>r is null||string.IsNullOrWhiteSpace(r.Target)||r.Kind is null||r.Kind.Length>30||r.Opinion is <-100 or >100||!double.IsFinite(r.LastSocial))||Relations.Select(r=>r.Target).Distinct().Count()!=Relations.Count||
            MoodEffects is null||MoodEffects.Count>8||MoodEffects.Any(m=>m is null||m.Key is null||m.Text is null||m.Key.Length>100||m.Text.Length>200||m.Amount is <-100 or >100||!Valid(m.Until,double.MaxValue)) ||
            PausedResearch is not null && (PausedResearch.Kind!="research"||!ValidOrigin(PausedResearch.Origin)||PausedResearch.Target!=""||PausedResearch.ItemId!=0||!Valid(PausedResearch.Progress,1)||PausedResearch.Progress>=1||!TerrainMap.InBounds(PausedResearch.Destination)) ||
            Activity is not null && (Activity.Kind is not ("rest" or "relax" or "research" or "pickup" or "eat" or "treat" or "care_supply" or "food_supply" or "feed" or "social" or "rescue_pickup" or "rescue_carry" or "life_supply" or "life_use" or "water_fill")||!ValidOrigin(Activity.Origin)||Activity.Target is null||Activity.Target.Length>100||Activity.RescueBed is Cell rescueBed&&!TerrainMap.InBounds(rescueBed)||!Valid(Activity.Progress,100000)||!TerrainMap.InBounds(Activity.Destination)))
            throw new InvalidDataException("人物资料无效。");
    }
}

public sealed class ColonyResearch
{
    public HashSet<string> Completed {get;set;}=new();
    public Dictionary<string,double> Progress {get;set;}=new();
    public string Active {get;set;}="";
    public long Version {get;set;}
    public void Validate()
    {
        if(Completed is null||Progress is null||Active is null||Version<0||Completed.Any(id=>!ResidentCatalog.Research.Any(r=>r.Id==id))||
           Completed.Any(id=>ResidentCatalog.Research.First(r=>r.Id==id).Prerequisites.Any(p=>!Completed.Contains(p)))||
           Progress.Any(p=>!ResidentCatalog.Research.Any(r=>r.Id==p.Key)||!double.IsFinite(p.Value)||p.Value<0||p.Value>ResidentCatalog.Research.First(r=>r.Id==p.Key).Cost)||
           Active!=""&&(!ResidentCatalog.Research.Any(r=>r.Id==Active)||Completed.Contains(Active)||ResidentCatalog.Research.First(r=>r.Id==Active).Prerequisites.Any(p=>!Completed.Contains(p))))
            throw new InvalidDataException("研究记录无效。");
    }
}
