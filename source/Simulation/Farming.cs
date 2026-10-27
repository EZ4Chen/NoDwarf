namespace NoDwarf.Simulation;

public sealed class FarmPlot
{
    public Cell Cell {get;set;}
    public int ZoneId {get;set;}
    public string Crop {get;set;}="grain";
    public bool Prepared {get;set;}
    public bool Planted {get;set;}
    public double Growth {get;set;}
    public int Harvests {get;set;}
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
    private double _farmElapsed;
    private readonly Dictionary<Cell,(long Version,string Issue)> _farmConditions=new();
    public static string CropName(string crop)=>CropCatalog.Get(crop).Name;
    public static string CropSeed(string crop)=>CropCatalog.Get(crop).Seed;
    public static double GrowTime(string crop)=>CropCatalog.Get(crop).Seconds;
    private string FarmEnvironment(FarmPlot plot)
    {
        if(Jobs.Any(j=>j.Construction&&j.Cell==plot.Cell))return "地格已有施工规划";
        if(_farmConditions.TryGetValue(plot.Cell,out var cached)&&cached.Version==Navigation.Version)return cached.Issue;
        string Check()
        {
            var c=plot.Cell;
            if(!Inside(c)||!Walkable(c)||NaturalRamp(c)||Buildings.ContainsKey(c))return "需要平整、空闲且有支撑的地面";
            string ground=BlockAt(c+new Cell(0,0,-1));
            bool soil=BlockCatalog.IsSoil(ground);
            bool rock=BlockCatalog.IsRegionalRock(ground)||ground is "nodwarf:stone" or "nodwarf:deepslate" or "nodwarf:iron_ore" or "nodwarf:coal_ore" or "nodwarf:deepslate_iron_ore" or "nodwarf:deepslate_coal_ore";
            if(plot.Crop!="mushroom"&&!soil)return "露天作物需要土壤或草地";
            if(plot.Crop=="mushroom"&&!soil&&!rock)return "菌床需要天然土壤或岩石地面";
            bool covered=FarmCovered(c);
            if(plot.Crop!="mushroom"&&covered)return "露天作物需要露天光照，请移除顶板";
            if(plot.Crop=="mushroom"&&(!covered||c.Level>=Terrain[c.X,c.Z].Level))return "蘑菇需要有顶板的地下空间";
            return "";
        }
        string issue=Check();_farmConditions[plot.Cell]=(Navigation.Version,issue);return issue;
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
            if(!p.Planted)ApplyZoneCrop(p);
        }
        Revision++;return true;
    }
    private void ApplyZoneCrop(FarmPlot p)
    {
        if(GrowingZones.TryGetValue(p.ZoneId,out var zone)&&p.Crop!=zone.Crop)
        {p.Crop=zone.Crop;p.Prepared=false;_farmConditions.Remove(p.Cell);_farmExposure.Remove(p.Cell);}
    }
    // Old saves have no zone IDs. Group touching plots of the same crop once, at load time.
    private void RestoreLegacyGrowingZones()
    {
        var remaining=Farms.Keys.ToHashSet();
        while(remaining.Count>0)
        {
            var first=remaining.First();var zone=new GrowingZone{Id=GrowingZones.Count+1,Crop=Farms[first].Crop};GrowingZones.Add(zone.Id,zone);
            var queue=new Queue<Cell>();queue.Enqueue(first);remaining.Remove(first);
            while(queue.TryDequeue(out var c))
            {
                Farms[c].ZoneId=zone.Id;
                foreach(var offset in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
                {var next=c+offset;if(remaining.Contains(next)&&Farms[next].Crop==zone.Crop){remaining.Remove(next);queue.Enqueue(next);}}
            }
        }
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
                if(Farms.Remove(c)){_farmConditions.Remove(c);_farmExposure.Remove(c);changed++;}continue;
            }
            if(Farms.ContainsKey(c)||Farms.Count>=256)continue;
            var plot=new FarmPlot{Cell=c,Crop=crop,ZoneId=zoneId};_farmConditions.Remove(c);
            bool suitable=FarmEnvironment(plot)=="";
            if(!suitable&&zoneOnly)
            {plot.Crop=crop=="grain"?"mushroom":"grain";_farmConditions.Remove(c);suitable=FarmEnvironment(plot)=="";plot.Crop=crop;_farmConditions.Remove(c);}
            if(!suitable){_farmConditions.Remove(c);_farmExposure.Remove(c);continue;}
            Farms.Add(c,plot);Stockpiles.Remove(c);foreach(var zone in StorageZones)zone.Cells.Remove(c);StorageZones.RemoveAll(z=>z.Cells.Count==0);changed++;
        }
        if(changed>0&&!remove)GrowingZones.Add(zoneId,new GrowingZone{Id=zoneId,Crop=crop});
        foreach(var id in GrowingZones.Keys.Where(id=>!Farms.Values.Any(p=>p.ZoneId==id)).ToArray())GrowingZones.Remove(id);
        if(changed>0)Revision++;
        Log(remove?$"已移除 {changed} 格种植区；未收获的农作物会损失，树木保留。":$"已划定 {changed} 格种植区。点击区域设置作物与播种；最多 256 格。");
        return changed>0;
    }
    private bool FarmReady(DemoJob job)=>Farms.TryGetValue(job.Cell,out var p)&&FarmEnvironment(p)==""&&
        (job.Harvest?p.Planted&&p.Growth>=GrowTime(p.Crop):!p.Planted&&GrowingZoneAt(p.Cell)?.AllowSowing!=false&&ClimateRules.TemperatureFactor(p.Crop,FarmClimate(p).Temperature)>0);
    private void UpdateFarming(double dt)
    {
        _farmElapsed+=dt;if(_farmElapsed<1)return;
        double elapsed=_farmElapsed;_farmElapsed=0;
        foreach(var p in Farms.Values)
        {
            if(!p.Planted)ApplyZoneCrop(p);
            if(CropCatalog.IsTree(p.Crop))
            {
                if(GrowingZoneAt(p.Cell)?.AllowSowing==true&&Plantable(p.Cell)&&Available(CropSeed(p.Crop))>0&&!Jobs.Any(j=>j.Forestry&&j.Cell==p.Cell))QueueTree(p.Cell,false,p.Crop);
                continue;
            }
            if(FarmEnvironment(p)!="")
            {
                foreach(var j in Jobs.Where(j=>j.Farming&&j.Cell==p.Cell).ToArray())CancelJob(j);
                continue; // Invalid conditions pause growth without granting harvests.
            }
            var climate=FarmClimate(p);
            if(!p.Planted&&ClimateRules.TemperatureFactor(p.Crop,climate.Temperature)==0)
            {
                foreach(var j in Jobs.Where(j=>j.Sow&&j.Cell==p.Cell).ToArray())CancelJob(j);
                continue; // Do not consume seed in temperatures that prevent germination.
            }
            if(p.Planted)
            {
                int previous=(int)(p.Growth/GrowTime(p.Crop)*3);
                p.Growth=Math.Min(GrowTime(p.Crop),p.Growth+elapsed*climate.Rate);
                if((int)(p.Growth/GrowTime(p.Crop)*3)!=previous)Revision++;
            }
            if(!p.Planted&&GrowingZoneAt(p.Cell)?.AllowSowing==false)continue;
            if(Jobs.Any(j=>j.Farming&&j.Cell==p.Cell)||p.Planted&&p.Growth<GrowTime(p.Crop))continue;
            var job=new DemoJob{Id=_nextJob+1,Cell=p.Cell,Sow=!p.Planted,Harvest=p.Planted};
            if(job.Sow)
            {
                var recipe=new Dictionary<string,int>{{CropSeed(p.Crop),1}};
                if(p.Crop=="mushroom"&&!p.Prepared)recipe["soil"]=1;
                if(!Reserve(job,recipe))continue;
            }
            _nextJob++;Jobs.Add(job);Revision++;
        }
    }
    private void CompleteFarm(DemoJob job)
    {
        var p=Farms[job.Cell];
        if(job.Sow){ConsumeMaterials(job);p.Prepared=true;p.Planted=true;p.Growth=0;}
        else
        {
            // One seed always returns; every third harvest yields one extra for expansion.
            p.Harvests++;var crop=CropCatalog.Get(p.Crop);SpawnItem(crop.Yield,crop.Count,p.Cell);SpawnItem(CropSeed(p.Crop),p.Harvests%3==0?2:1,p.Cell);
            p.Planted=false;p.Growth=0;ApplyZoneCrop(p);
        }
        Revision++;
    }
    public string FarmStatus(FarmPlot p)
    {
        if(Trees.TryGetValue(p.Cell,out var tree))return $"{CropName(tree.Species)} {tree.Height} m · 成长 {tree.Age/GrowTime(tree.Species):P0}（伐木由命令指定）";
        if(!p.Planted&&GrowingZoneAt(p.Cell)?.AllowSowing==false)return "已暂停播种；现有作物仍生长并收获";
        if(CropCatalog.IsTree(p.Crop))return !Plantable(p.Cell)?"树木需露天土壤、14 米净空和 4 米树间距（含待种树苗）":Available(CropSeed(p.Crop))==0&&!Jobs.Any(j=>j.PlantTree&&j.Cell==p.Cell)?"等待：缺少树苗":"等待居民种树";
        string issue=FarmEnvironment(p);if(issue!="")return "暂停："+issue;
        var climate=FarmClimate(p);
        if(p.Planted)
        {
            if(p.Growth>=GrowTime(p.Crop))return "成熟，等待收获";
            return climate.Rate==0?$"生长 {p.Growth/GrowTime(p.Crop):P0} · 停长：{climate.Issue}":$"生长 {p.Growth/GrowTime(p.Crop):P0} · 速度 {climate.Rate:P0}";
        }
        if(ClimateRules.TemperatureFactor(p.Crop,climate.Temperature)==0)return "等待播种："+climate.Issue;
        var sow=Jobs.FirstOrDefault(j=>j.Sow&&j.Cell==p.Cell);
        if(sow is not null)
        {
            if(sow.Worker>=0)return sow.Materials.All(r=>r.Stage==2)?"正在播种":"正在取料／搬入种子";
            if(Workers.Count==0)return "等待：启用居民耕作";
            if(!Workers.Any(w=>ActionPositions(sow).Any(c=>Navigation.CanReach(w.Tile,c))&&CanSupply(w,sow)))return "等待：田地或种子不可达";
            return "等待居民播种";
        }
        if(Available(CropSeed(p.Crop))<1)return "等待：缺少"+ItemCatalog.Get(CropSeed(p.Crop)).Name;
        if(p.Crop=="mushroom"&&!p.Prepared&&Available("soil")<1)return "等待：首次铺设菌床需要 1 土壤";
        return "等待播种";
    }
}
