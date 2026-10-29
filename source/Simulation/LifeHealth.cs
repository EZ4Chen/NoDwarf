namespace NoDwarf.Simulation;
public static class LifeHealth
{
    public static IEnumerable<(string,double)> Exposures(ResidentProfile p)
    {yield return ("dehydration",p.Dehydration);yield return ("cold",p.ColdExposure);yield return ("heat",p.HeatExposure);}
    public static bool Perishable(string item)=>item is "meal" or "raw_food" or "potato" or "mushroom" or "flour" or "wheat";
    public static double ShelfLife(string item)=>item=="meal"?1800:item is "flour" or "wheat"?18000:6000;
    public static bool Valid(ResidentProfile p)
    {
        static bool Range(double v,double max)=>double.IsFinite(v)&&v>=0&&v<=max;
        return Range(p.Thirst,100)&&Range(p.Dehydration,300)&&Range(p.ColdExposure,300)&&Range(p.HeatExposure,300)&&Range(p.CriticalSeconds,600)&&Range(p.InfectionExposure,120)&&p.DeathCause is not null&&p.DeathCause.Length<=60&&p.Prostheses is not null&&p.Prostheses.Count<=8&&p.Prostheses.All(v=>ResidentCatalog.Parts.Any(t=>t.Id==v.Key)&&v.Key is not ("head" or "torso")&&Range(v.Value,.5))&&
            (p.Activity is null||p.Activity.Supply is not null&&p.Activity.BodyPart is not null&&p.Activity.Supply.Length<40&&p.Activity.BodyPart.Length<40&&(p.Activity.SourceCell is null||TerrainMap.InBounds(p.Activity.SourceCell.Value))&&(p.Activity.Kind is not ("life_supply" or "life_use" or "water_fill")||p.Activity.Supply is "water_bucket" or "bucket" or "splint" or "prosthesis"));
    }
    public static void Step(ResidentProfile p,double seconds,double temperature,double insulation=0)
    {
        if(p.Dead||seconds<=0)return;
        p.Thirst=Math.Max(0,p.Thirst-seconds*.045);
        p.Dehydration=Math.Clamp(p.Dehydration+seconds*(p.Thirst<10?.15:p.Thirst>=40?-.3:0),0,300);
        p.ColdExposure=Math.Clamp(p.ColdExposure+seconds*(temperature<5-insulation?Math.Min(.4,(5-insulation-temperature)*.01):-.3),0,300);
        p.HeatExposure=Math.Clamp(p.HeatExposure+seconds*(temperature>35?Math.Min(.4,(temperature-35)*.01):-.3),0,300);
        string cause=p.Malnutrition>=300?"长期饥饿":p.Dehydration>=300?"严重脱水":p.ColdExposure>=300?"严重失温":p.HeatExposure>=300?"严重中暑":p.Diseases.Any(d=>d.Progress>=300&&d.Immunity<100)?"重症感染":"";
        p.CriticalSeconds=cause==""?0:Math.Min(600,p.CriticalSeconds+seconds);
        if(p.CriticalSeconds>=600)p.DeathCause=cause;
    }
}
public sealed record LifeAlert(string Id,string Text,int Priority,string Resident="",Cell? Location=null);
public sealed partial class DemoWorld
{
    public HashSet<Cell> WaterPoints {get;}=new();
    public List<LifeAlert> LifeAlerts {get;}=new();
    private double _lifeEmergencyAt;
    public ResidentResult SetWaterPoint(Cell c,bool enabled)
    {
        if(!TerrainMap.InBounds(c)||enabled&&(WaterPoints.Count>=256||Fluids.Amount(c)<5))return new(false,"需要含水的方块；最多 256 个取水点");
        if(enabled)WaterPoints.Add(c);else WaterPoints.Remove(c);
        Revision++;return new(true,enabled?"已指定饮用水源；居民只在安全岸边取水":"已取消取水点，现有水桶仍可饮用");
    }
    public void MigrateLifeItems()
    {
        foreach(var s in Items)if(LifeHealth.Perishable(s.Item)&&s.Properties.ExpiresAt is null)s.Properties=s.Properties with{ExpiresAt=_simulationTime+LifeHealth.ShelfLife(s.Item)};
        foreach(var r in Jobs.SelectMany(j=>j.Materials))if(LifeHealth.Perishable(r.Item)&&r.Properties.ExpiresAt is null)r.Properties=r.Properties with{ExpiresAt=_simulationTime+LifeHealth.ShelfLife(r.Item)};
    }
    private void SpoilFood()
    {
        bool Expired(ItemProperties p)=>p.ExpiresAt is double t&&t<=_simulationTime;
        // Cancel whole reservations first, returning in-transit ingredients through the normal cargo path.
        var expiredIds=Items.Where(s=>Expired(s.Properties)).Select(s=>s.Id).ToHashSet();
        foreach(var j in Jobs.Where(j=>j.Materials.Any(r=>Expired(r.Properties)||expiredIds.Contains(r.StackId))).ToArray())CancelJob(j);
        foreach(var s in Items.Where(s=>LifeHealth.Perishable(s.Item)&&Expired(s.Properties)).ToArray())
        {s.Item="rot";s.Properties=new();s.Slot="";Revision++;}
        foreach(var w in Workers)if(w.Person.Activity is {Kind:"food_supply" or "feed" or "eat"} a&&
            (a.Kind=="eat"?!Inventory(w).Any(s=>s.Item=="meal"):!Items.Any(s=>s.Id==a.ItemId&&s.Item=="meal")))CancelResidentActivity(w);
    }
    private void StepLife(double dt)
    {
        SpoilFood();
        var infected=Workers.Where(w=>!w.Person.Dead&&!w.Person.Isolated&&w.Person.Diseases.Any(d=>d.Id=="fever"&&d.Immunity<100)).ToLookup(w=>(w.Tile.X/4,w.Tile.Z/4,w.Level));
        foreach(var w in Workers)
        {
            var p=w.Person;if(p.Dead)continue;
            double insulation=Inventory(w).Count(s=>s.Slot!=""&&s.Item is "shirt" or "trousers" or "boots")*3;
            LifeHealth.Step(p,dt,TemperatureAt(w.Tile),insulation);
            bool exposed=false;
            if(!p.Isolated&&!p.Diseases.Any(d=>d.Id=="fever"))
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                    if(infected[(w.Tile.X/4+dx,w.Tile.Z/4+dz,w.Level)].Any(o=>o!=w&&Math.Abs(o.X-w.X)+Math.Abs(o.Z-w.Z)<=3))exposed=true;
            p.InfectionExposure=Math.Clamp(p.InfectionExposure+(exposed?dt:-dt*2),0,120);
            if(p.InfectionExposure>=120){ResidentHealth.AddDisease(p,"fever");p.InfectionExposure=0;Log(w.Name+"接触热病后发病，可在健康页隔离。");}
            if(p.Dead){if(w.Job is not null)Release(w.Job);CancelResidentActivity(w);Log(w.Name+"死于"+p.DeathCause);}
        }
        RefreshLifeAlerts();
    }
    public void RefreshLifeAlerts()
    {
        LifeAlerts.Clear();AddWaterAlerts();var alive=Workers.Where(w=>!w.Person.Dead).ToArray();if(alive.Length==0)return;
        int meals=Available("meal"),personal=Items.Where(s=>s.Item=="meal"&&s.Owner!="").Sum(s=>s.Count),raw=Items.Where(s=>s.Owner==""&&s.Item is "raw_food" or "wheat" or "flour" or "potato" or "mushroom").Sum(s=>s.Count);
        double days=(meals+personal)*45d/(alive.Length*.035*600);
        if(days<2)LifeAlerts.Add(new("food",$"口粮约 {days:F1} 天：公共 {meals}／随身 {personal}，食材 {raw}（未计收成）",2,Location:Items.FirstOrDefault(s=>s.Item=="meal")?.Cell));
        if(Available("water_bucket")==0)LifeAlerts.Add(new("water","饮水不足：检查水桶、取水点及安全通路",2));
        foreach(var w in alive)
        {
            var p=w.Person;
            void Add(string id,string text,int priority)=>LifeAlerts.Add(new(id+":"+w.Id,w.Name+" · "+text,priority,w.Id,w.Tile));
            if(ResidentHealth.Bleeding(p)>0)Add("bleed",$"出血，预计 {HealthCare.TimeToBleedOut(p):F0} 秒耗尽血量",0);
            if(p.CriticalSeconds>0)Add("critical",$"危急，约 {600-p.CriticalSeconds:F0} 秒后致命；立即补给／救治",0);
            if(p.Hunger<25)Add("hungry",p.FoodIssue!=""?p.FoodIssue:"饥饿，需要可达口粮",1);
            if(p.Thirst<25)Add("thirst","口渴，需要饮水或同伴送水",1);
            if(p.ColdExposure>0||p.HeatExposure>0)Add("temperature",$"冷热伤害 · 当前 {TemperatureAt(w.Tile):F0}°C，需要安全住所",1);
            if(NeedsRescue(w)&&!RescueReserved(w.Id))Add("rescue",MedicalBeds.Any(IsMedicalBed)?"等待照料者搬运，请检查通路":"等待救援，没有医疗床",1);
            if(HealthCare.Needed(p)&&!Workers.Any(m=>m.Person.Activity is {Kind:"treat" or "care_supply"} a&&a.Target==w.Id))
                Add("care",Available("bandage")==0&&!Items.Any(s=>s.Item=="bandage"&&s.Owner!="")?"缺绷带（布料不等于成品）":"等待医生，请启用医疗工作",1);
        }
        LifeAlerts.Sort((a,b)=>a.Priority.CompareTo(b.Priority));
    }
    private void EmergencyMedical()
    {
        if(_simulationTime<_lifeEmergencyAt)return;_lifeEmergencyAt=_simulationTime+2;
        foreach(var patient in Workers.Where(w=>!w.Person.Dead&&HealthCare.Urgency(w.Person)==0&&!RescueReserved(w.Id)).OrderBy(w=>HealthCare.TimeToBleedOut(w.Person)).Take(4))
        {
            if(Workers.Any(m=>m.Person.Activity is {Kind:"treat" or "care_supply"} a&&a.Target==patient.Id))continue;
            var medic=Workers.Where(m=>!m.WaterEvacuating&&!FloodThreat(m)&&!m.Person.Dead&&!m.Person.Incapacitated&&!MilitaryControlled(m)&&m.Person.WorkPriorities.GetValueOrDefault("doctor")>0&&m.Person.Manipulation>=.05&&m.Person.Mobility>=.05&&m.Person.Hunger>=10&&m.Person.Thirst>=10&&m.Person.Activity?.Kind is not ("treat" or "care_supply" or "rescue_carry" or "life_use"))
                .OrderBy(m=>Math.Abs(m.X-patient.X)+Math.Abs(m.Z-patient.Z));
            foreach(var m in medic.Take(4))if(BeginResidentActivity(m,"treat",patient.Id,origin:"emergency").Ok)break;
        }
    }
}
