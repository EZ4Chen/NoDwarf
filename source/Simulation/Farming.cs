namespace NoDwarf.Simulation;

public class PlantSite
{
    public Cell Cell {get;set;}
    public string Crop {get;set;}="grain";
    public bool Prepared {get;set;}
}
// Management only. Living plants are owned by the world, never by a growing zone.
public sealed class FarmPlot : PlantSite
{
    public int ZoneId {get;set;}
    public int Harvests {get;set;}
    public double NextUpdateAt {get;set;}
}
public sealed class PlantState : PlantSite
{
    public double Growth {get;set;}
    public double GrowthUpdatedAt {get;set;}
    public double NextUpdateAt {get;set;}
}
public sealed class GrowingZone
{
    public int Id {get;set;}
    public string Crop {get;set;}="grain";
    public bool AllowSowing {get;set;}=true;
}
public sealed partial class DemoWorld
{
    public Dictionary<Cell,FarmPlot> Farms {get;}=new();
    public Dictionary<int,GrowingZone> GrowingZones {get;}=new();
    public GrowingZone? GrowingZoneAt(Cell cell)=>Farms.TryGetValue(cell,out var p)?GrowingZones.GetValueOrDefault(p.ZoneId):null;
    public Dictionary<Cell,PlantState> Plants {get;}=new();
    private readonly ScheduledUpdates<Cell> _plantUpdates=new(CellUpdateOrder.Instance);
    public int LastPlantVisits {get;private set;}
    private readonly ScheduledUpdates<Cell> _farmUpdates=new(CellUpdateOrder.Instance);
    public long FarmUpdateVisits {get;private set;}
    public int LastFarmVisits {get;private set;}
    private void ScheduleFarm(FarmPlot plot,double due){plot.NextUpdateAt=due;_farmUpdates.Schedule(plot.Cell,due);}
    private static double FarmPhase(Cell c)=>((uint)(c.X*73856093^c.Z*19349663^c.Level*83492791)%1000+1)/1000d;
    private readonly Dictionary<Cell,(long Version,string Crop,string Issue)> _farmConditions=new();
    public static string CropName(string crop)=>CropCatalog.Get(crop).Name;
    public static string CropSeed(string crop)=>CropCatalog.Get(crop).Seed;
    public static double GrowTime(string crop)=>CropCatalog.Get(crop).Seconds;
    private string FarmEnvironment(PlantSite plot)
    {
        if(Jobs.At(plot.Cell).Any(j=>j.Construction))return "地格已有施工规划";
        if(_farmConditions.TryGetValue(plot.Cell,out var cached)&&cached.Version==FarmGeometryVersion(plot.Cell)&&cached.Crop==plot.Crop)return cached.Issue;
        string Check()
        {
            var c=plot.Cell;
            if(!Inside(c)||!Walkable(c)||NaturalRamp(c)||Buildings.ContainsKey(c))return "需要平整、空闲且有支撑的地面";
            string ground=BlockAt(c+new Cell(0,0,-1));
            bool soil=BlockCatalog.IsSoil(ground);
            bool rock=BlockCatalog.IsRegionalRock(ground)||ground is "nodwarf:stone" or "nodwarf:deepslate" or "nodwarf:iron_ore" or "nodwarf:coal_ore" or "nodwarf:deepslate_iron_ore" or "nodwarf:deepslate_coal_ore";
            if(plot.Crop!="mushroom"&&PlantGrowthRules.Default.Fertility(ground)<PlantGrowthRules.Default.Crop(CropCatalog.IsTree(plot.Crop)?"grain":plot.Crop).MinFertility)return "土壤肥力不足";
            if(plot.Crop!="mushroom"&&!soil)return "露天作物需要土壤或草地";
            if(plot.Crop=="mushroom"&&!soil&&!rock)return "菌床需要天然土壤或岩石地面";
            bool covered=FarmCovered(c);
            if(plot.Crop!="mushroom"&&covered)return "露天作物需要露天光照，请移除顶板";
            if(plot.Crop=="mushroom"&&(!covered||c.Level>=Terrain[c.X,c.Z].Level))return "蘑菇需要有顶板的地下空间";
            return "";
        }
        string issue=Check();_farmConditions[plot.Cell]=(FarmGeometryVersion(plot.Cell),plot.Crop,issue);return issue;
    }
    public bool DesignateGrowingZone(Cell a,Cell b)=>Inside(a)&&Inside(b)&&SetFarm(a,b,FarmCovered(a)&&a.Level<Terrain[a.X,a.Z].Level?"mushroom":"grain",zoneOnly:true);
    public bool ConfigureGrowingZone(int id,string crop,bool allowSowing)
    {
        if(!CropCatalog.Valid(crop)||!GrowingZones.TryGetValue(id,out var zone))return false;
        bool changed=zone.Crop!=crop;
        zone.Crop=crop;zone.AllowSowing=allowSowing;
        foreach(var p in Farms.Values.Where(p=>p.ZoneId==id))
        {
            if(changed||!allowSowing)
                foreach(var j in Jobs.Where(j=>j.Cell==p.Cell&&(j.Sow||j.PlantTree)).ToArray())CancelJob(j);
            ApplyZoneCrop(p);
        }
        Revision++;return true;
    }
    private void ApplyZoneCrop(FarmPlot p)
    {
        if(GrowingZones.TryGetValue(p.ZoneId,out var zone)&&p.Crop!=zone.Crop)
        {p.Crop=zone.Crop;p.Prepared=false;_farmConditions.Remove(p.Cell);_farmExposure.Remove(p.Cell);}
    }
    public bool SetFarm(Cell a,Cell b,string crop,bool remove=false,bool zoneOnly=false)
    {
        if(a.Level!=b.Level||!Inside(a)||!Inside(b)||Math.Abs(a.X-b.X)>15||Math.Abs(a.Z-b.Z)>15||(!CropCatalog.Valid(crop)||CropCatalog.IsTree(crop)))return false;
        int changed=0,zoneId=1;while(GrowingZones.ContainsKey(zoneId))zoneId++;
        for(int x=Math.Min(a.X,b.X);x<=Math.Max(a.X,b.X);x++)for(int z=Math.Min(a.Z,b.Z);z<=Math.Max(a.Z,b.Z);z++)
        {
            var c=new Cell(x,z,a.Level);
            if(remove)
            {
                foreach(var j in Jobs.Where(j=>(j.Farming||j.PlantTree)&&j.Cell==c&&Farms.ContainsKey(c)).ToArray())CancelJob(j);
                if(Farms.Remove(c)){_farmUpdates.Cancel(c);_farmConditions.Remove(c);_farmExposure.Remove(c);changed++;}continue;
            }
            if(Farms.ContainsKey(c)||Farms.Count>=256)continue;
            var plot=new FarmPlot{Cell=c,Crop=crop,ZoneId=zoneId};_farmConditions.Remove(c);
            bool suitable=FarmEnvironment(plot)=="";
            if(!suitable&&zoneOnly)
            {plot.Crop=crop=="grain"?"mushroom":"grain";_farmConditions.Remove(c);suitable=FarmEnvironment(plot)=="";plot.Crop=crop;_farmConditions.Remove(c);}
            if(!suitable){_farmConditions.Remove(c);_farmExposure.Remove(c);continue;}
            Farms.Add(c,plot);ScheduleFarm(plot,SimulationTime+FarmPhase(c));Stockpiles.Remove(c);foreach(var zone in StorageZones)zone.Cells.Remove(c);StorageZones.RemoveAll(z=>z.Cells.Count==0);changed++;
        }
        if(changed>0&&!remove)GrowingZones.Add(zoneId,new GrowingZone{Id=zoneId,Crop=crop});
        foreach(var id in GrowingZones.Keys.Where(id=>!Farms.Values.Any(p=>p.ZoneId==id)).ToArray())GrowingZones.Remove(id);
        if(changed>0)Revision++;
        Log(remove?$"已移除 {changed} 格种植区；现有植物保留并继续生长，停止自动播种和收获。":$"已划定 {changed} 格种植区。点击区域设置作物与播种；最多 256 格。");
        return changed>0;
    }
    private void SeedWildPlants()
    {
        // Bounded generation candidates, separate random stream from terrain and residents.
        var random=new Random(unchecked(Terrain.Seed*397+5311));
        var crops=CropCatalog.All.Where(c=>!c.Tree).ToArray();
        for(int x=4;x<Width-4;x+=8)for(int z=4;z<Depth-4;z+=8)
        {
            int px=x+random.Next(4),pz=z+random.Next(4);
            if(Math.Abs(px-Camp.X)<14&&Math.Abs(pz-Camp.Z)<14)continue;
            var cell=new Cell(px,pz,Terrain[px,pz].Level);
            double roll=random.NextDouble();
            foreach(var crop in crops)
            {
                roll-=PlantGrowthRules.Default.Crop(crop.Id).WildChance;
                if(roll>=0)continue;
                SpawnPlant(cell,crop.Id,crop.Seconds*(.1+random.NextDouble()*.8));break;
            }
        }
    }
    public bool SpawnPlant(Cell cell,string crop,double growth=0,bool prepared=false)
    {
        if(!Inside(cell)||!CropCatalog.Valid(crop)||CropCatalog.IsTree(crop)||!double.IsFinite(growth)||growth<0||growth>GrowTime(crop)||Plants.Count>=16384||Plants.ContainsKey(cell)||Trees.ContainsKey(cell))return false;
        var plant=new PlantState{Cell=cell,Crop=crop,Prepared=prepared,Growth=growth,GrowthUpdatedAt=SimulationTime,NextUpdateAt=SimulationTime+FarmPhase(cell)};
        if(FarmEnvironment(plant)!="")return false;
        Plants.Add(cell,plant);_plantUpdates.Schedule(cell,plant.NextUpdateAt);Revision++;return true;
    }
    public bool RemovePlant(Cell cell)
    {
        if(!Plants.Remove(cell))return false;
        _plantUpdates.Cancel(cell);
        foreach(var job in Jobs.At(cell).Where(j=>j.Harvest).ToArray())CancelJob(job);
        Revision++;return true;
    }
    public bool QueuePlantHarvest(Cell cell)
    {
        if(!Plants.TryGetValue(cell,out var p)||p.Growth<GrowTime(p.Crop)||FarmEnvironment(p)!=""||Jobs.At(cell).Any(j=>j.Farming))return false;
        Jobs.Add(new DemoJob{Id=++_nextJob,Cell=cell,Harvest=true});Revision++;return true;
    }
    private bool FarmReady(DemoJob job)=>job.Harvest
        ?Plants.TryGetValue(job.Cell,out var plant)&&plant.Growth>=GrowTime(plant.Crop)&&FarmEnvironment(plant)==""
        :Farms.TryGetValue(job.Cell,out var p)&&!Plants.ContainsKey(p.Cell)&&FarmEnvironment(p)==""&&GrowingZoneAt(p.Cell)?.AllowSowing==true&&ClimateRules.TemperatureFactor(p.Crop,FarmClimate(p).Temperature)>0;
    private void UpdateFarming(double dt)
    {
        LastPlantVisits=0;
        for(int budget=32;budget>0&&_plantUpdates.TryTake(SimulationTime,out var cell);budget--)
        {
            if(!Plants.TryGetValue(cell,out var p))continue;
            LastPlantVisits++;
            double elapsed=Math.Max(0,SimulationTime-p.GrowthUpdatedAt);p.GrowthUpdatedAt=SimulationTime;
            p.NextUpdateAt=SimulationTime+1;_plantUpdates.Schedule(cell,p.NextUpdateAt);
            int previous=(int)(p.Growth/GrowTime(p.Crop)*3);
            p.Growth=Math.Min(GrowTime(p.Crop),p.Growth+elapsed*FarmClimate(p).Rate);
            if((int)(p.Growth/GrowTime(p.Crop)*3)!=previous)Revision++;
        }
        LastFarmVisits=0;
        for(int budget=32;budget>0&&_farmUpdates.TryTake(SimulationTime,out var cell);budget--)
        {
            if(!Farms.TryGetValue(cell,out var p))continue;
            LastFarmVisits++;FarmUpdateVisits++;ScheduleFarm(p,SimulationTime+1);ApplyZoneCrop(p);
            if(Plants.ContainsKey(cell)){QueuePlantHarvest(cell);continue;}
            if(CropCatalog.IsTree(p.Crop))
            {
                if(GrowingZoneAt(cell)?.AllowSowing==true&&Plantable(cell)&&Available(CropSeed(p.Crop))>0&&!Jobs.At(cell).Any(j=>j.Forestry))QueueTree(cell,false,p.Crop);
                continue;
            }
            if(FarmEnvironment(p)!=""||ClimateRules.TemperatureFactor(p.Crop,FarmClimate(p).Temperature)==0)
            {foreach(var j in Jobs.At(cell).Where(j=>j.Farming).ToArray())CancelJob(j);continue;}
            if(GrowingZoneAt(cell)?.AllowSowing!=true||Jobs.At(cell).Any(j=>j.Farming))continue;
            var job=new DemoJob{Id=_nextJob+1,Cell=cell,Sow=true};
            var recipe=new Dictionary<string,int>{{CropSeed(p.Crop),1}};
            if(p.Crop=="mushroom"&&!p.Prepared)recipe["soil"]=1;
            if(!Reserve(job,recipe))continue;
            _nextJob++;Jobs.Add(job);Revision++;
        }
    }
    private void CompleteFarm(DemoJob job)
    {
        if(job.Sow)
        {
            var p=Farms[job.Cell];
            if(!SpawnPlant(p.Cell,p.Crop,prepared:true))return;
            ConsumeMaterials(job);p.Prepared=true;
        }
        else
        {
            var p=Plants[job.Cell];var crop=CropCatalog.Get(p.Crop);
            int harvests=Farms.TryGetValue(job.Cell,out var plot)?++plot.Harvests:1;
            SpawnItem(crop.Yield,crop.Count,p.Cell);SpawnItem(CropSeed(p.Crop),harvests%3==0?2:1,p.Cell);
            Plants.Remove(p.Cell);_plantUpdates.Cancel(p.Cell);
        }
        Revision++;
    }
    public string PlantStatus(PlantState p)
    {
        if(p.Growth>=GrowTime(p.Crop))return "成熟，可收获";
        var climate=FarmClimate(p);
        return climate.Rate==0?$"生长 {p.Growth/GrowTime(p.Crop):P0} · 停长：{climate.Issue}":$"生长 {p.Growth/GrowTime(p.Crop):P0} · 速度 {climate.Rate:P0}";
    }
    public string FarmStatus(FarmPlot p)
    {
        if(Trees.TryGetValue(p.Cell,out var tree))return $"{CropName(tree.Species)} {tree.Height} m · 成长 {TreeAge(tree)/GrowTime(tree.Species):P0}（伐木由命令指定）";
        if(Plants.TryGetValue(p.Cell,out var plant))return PlantStatus(plant);
        if(GrowingZoneAt(p.Cell)?.AllowSowing==false)return "已暂停播种；现有作物仍生长并收获";
        if(CropCatalog.IsTree(p.Crop))return !Plantable(p.Cell)?"树木需露天土壤、14 米净空和 4 米树间距（含待种树苗）":Available(CropSeed(p.Crop))==0&&!Jobs.Any(j=>j.PlantTree&&j.Cell==p.Cell)?"等待：缺少树苗":"等待居民种树";
        string issue=FarmEnvironment(p);if(issue!="")return "暂停："+issue;
        var climate=FarmClimate(p);
        if(ClimateRules.TemperatureFactor(p.Crop,climate.Temperature)==0)return "等待播种："+climate.Issue;
        var sow=Jobs.FirstOrDefault(j=>j.Sow&&j.Cell==p.Cell);
        if(sow is not null)
        {
            if(sow.Worker>=0)return sow.Materials.All(r=>r.Stage==2)?"正在播种":"正在取料／搬入种子";
            if(Workers.Count==0)return "等待：启用居民耕作";
            if(!Workers.Any(w=>ActionPositions(sow).Any(c=>Navigation.CanReach(w.Tile,c))&&CanSupply(w,sow)))return "等待：田地或种子不可达";
            return "等待居民播种";
        }
        if(Available(CropSeed(p.Crop))<1)return "等待：缺少"+Content.Item(CropSeed(p.Crop)).Name;
        if(p.Crop=="mushroom"&&!p.Prepared&&Available("soil")<1)return "等待：首次铺设菌床需要 1 土壤";
        return "等待播种";
    }
}
