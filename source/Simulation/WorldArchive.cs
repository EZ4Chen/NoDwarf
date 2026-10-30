using System.Text.Json;
using System.Text.Json.Serialization;
namespace NoDwarf.Simulation;

public sealed class WorkerState
{
    public string? SpeciesFingerprint {get;set;}
    public CombatOrder Combat {get;set;}=new();
    public string Id {get;set;}="";public string Name {get;set;}="";
    public CharacterAppearance Appearance {get;set;}=new();
    public ResidentProfile? Person {get;set;}
    public double X {get;set;} public double Z {get;set;} public int Level {get;set;}
    public double ClimbProgress {get;set;}
    public Cell? TravelFrom {get;set;} public Cell[] Path {get;set;}=Array.Empty<Cell>();
    public bool WaterEvacuating {get;set;}
    public int? JobId {get;set;} public string Status {get;set;}="等待工作";
}
public sealed class WorldSnapshot
{
    public const int CurrentVersion=34;
    [JsonRequired] public int Version {get;set;}=CurrentVersion;
    public ContentManifest? Content {get;set;}
    public ModuleArchive[]? Modules {get;set;}
    public CreatureArchive? Creatures {get;set;}
    public StoryState? Story {get;set;}
    public ResidentMindState? ResidentMind {get;set;}
    public DialogueState? Dialogues {get;set;}=new();
    public Cell[] WaterPoints {get;set;}=[];
    public Cell[] MedicalBeds {get;set;}=[];
    public WorkerState[] Enemies {get;set;}=[];
    public Cell? RallyPoint {get;set;}
    public bool CivilianAlert {get;set;}
    public int RaidSerial {get;set;}
    public string BattleOutcome {get;set;}="";
    public ProductionOrder[] ProductionOrders {get;set;}=Array.Empty<ProductionOrder>();
    public double ProductionElapsed {get;set;}
    public RoomClimateState[] RoomClimates {get;set;}=Array.Empty<RoomClimateState>();
    public double RoomTemperatureUpdatedAt {get;set;}
    public FluidArchive? Fluids {get;set;}
    [JsonRequired] public int ClimateVersion {get;set;}=1;
    public string? WeatherOverride {get;set;}
    [JsonRequired] public int EcologyVersion {get;set;}=TerrainMap.CurrentEcologyVersion;
    [JsonRequired] public int GeneratorVersion {get;set;}=TerrainMap.CurrentGeneratorVersion;
    public WorldGenerationSettings? Generation {get;set;}
    public int Seed {get;set;} public bool Flat {get;set;}
    public string? Region {get;set;}
    public TreeState[] Trees {get;set;}=Array.Empty<TreeState>();
    public StorageZone[] StorageZones {get;set;}=Array.Empty<StorageZone>();
    public FireState[] Fires {get;set;}=Array.Empty<FireState>();
    public FarmPlot[] Farms {get;set;}=Array.Empty<FarmPlot>();
    public GrowingZone[] GrowingZones {get;set;}=Array.Empty<GrowingZone>();
    public PlantState[] Plants {get;set;}=Array.Empty<PlantState>();
    public BlockDamage[] Damage {get;set;}=Array.Empty<BlockDamage>();
    public TerrainBlock[] PlacedBlocks {get;set;}=Array.Empty<TerrainBlock>();
    public TerrainBlock[] TerrainBlocks {get;set;}=Array.Empty<TerrainBlock>();
    public Cell[] Excavated {get;set;}=Array.Empty<Cell>();
    public Building[] Buildings {get;set;}=Array.Empty<Building>();
    public ItemStack[] Items {get;set;}=Array.Empty<ItemStack>();
    public Cell[] Stockpiles {get;set;}=Array.Empty<Cell>();
    public DemoJob[] Jobs {get;set;}=Array.Empty<DemoJob>();
    public WorkerState[] Workers {get;set;}=Array.Empty<WorkerState>();
    public WorkerState[]? RemovedWorkers {get;set;}
    public BlueprintPart[] Blueprint {get;set;}=Array.Empty<BlueprintPart>();
    public int? DemoStage {get;set;} public int? UndergroundStage {get;set;}
    public double SimulationTime {get;set;}
    public double ResidentElapsed {get;set;}
    public ColonyResearch? Research {get;set;}
}
public sealed partial class DemoWorld
{
    public static readonly JsonSerializerOptions SaveOptions=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    private static WorkerState SaveWorker(DemoWorker w)=>new(){SpeciesFingerprint=w.Person.Species.Fingerprint,WaterEvacuating=w.WaterEvacuating,Combat=w.Combat,Id=w.Id,Name=w.Name,Appearance=w.Appearance,Person=w.Person,X=w.X,Z=w.Z,Level=w.Level,TravelFrom=w.TravelFrom,ClimbProgress=w.ClimbProgress,Path=w.Path.ToArray(),JobId=w.Job?.Id,Status=w.Status};
    public string SaveJson()=>JsonSerializer.Serialize(CreateArchive(),SaveOptions);
    // Call between simulation ticks on the owning thread. Only the detached graph leaves it.
    public WorldSnapshot CaptureSnapshot()=>SnapshotCopy.Copy(CreateArchive());
    private WorldSnapshot CreateArchive()=>new WorldSnapshot{
        PlacedBlocks=Terrain.PlacedBlocks.ToArray(),TerrainBlocks=Terrain.DefinitionBlocks.ToArray(),Content=Content.Manifest(),Modules=Modules.Save(this),
        Creatures=_creatures?.Save()??new CreatureArchive(),Story=Story,ResidentMind=ResidentMind,Dialogues=Dialogues,
        WaterPoints=WaterPoints.ToArray(),MedicalBeds=MedicalBeds.Where(IsMedicalBed).ToArray(),
        Enemies=Enemies.Select(SaveWorker).ToArray(),RallyPoint=RallyPoint,CivilianAlert=CivilianAlert,RaidSerial=_raidSerial,BattleOutcome=BattleOutcome,
        ProductionOrders=ProductionOrders.ToArray(),ProductionElapsed=_productionElapsed,RoomClimates=SaveRoomClimates(),RoomTemperatureUpdatedAt=_lastRoomTemperatureUpdate,
        Fluids=Fluids.Save(_openFloodgates),ClimateVersion=ClimateVersion,WeatherOverride=WeatherOverride,EcologyVersion=Terrain.EcologyVersion,Seed=Terrain.Seed,Flat=Terrain.Flat,Region=Terrain.Region?.Id,GeneratorVersion=Terrain.GeneratorVersion,Trees=Trees.Values.ToArray(),StorageZones=StorageZones.ToArray(),Fires=FireFuel.Where(f=>Buildings.TryGetValue(f.Key,out var b)&&b.Kind==BuildKind.Campfire).Select(f=>new FireState(f.Key,f.Value)).ToArray(),Farms=Farms.Values.ToArray(),Plants=Plants.Values.ToArray(),Damage=_blockDamage.Values.Where(d=>d.Block==BlockAt(d.Cell)).ToArray(),Excavated=Terrain.Excavated.ToArray(),Buildings=Buildings.Values.ToArray(),Items=Items.ToArray(),Stockpiles=Stockpiles.ToArray(),Jobs=Jobs.ToArray(),
        Generation=Terrain.Generation,GrowingZones=GrowingZones.Values.ToArray(),Workers=Workers.Select(SaveWorker).ToArray(),RemovedWorkers=_removedResidents?.Select(SaveWorker).ToArray(),Blueprint=Blueprint.ToArray(),DemoStage=Demo?.Stage,UndergroundStage=Underground?.Stage,SimulationTime=_simulationTime,ResidentElapsed=_residentElapsed,Research=Research};
    public static DemoWorld LoadJson(string json,CreatureCatalog? creatureCatalog=null,CreatureBehaviorRegistry? creatureBehaviors=null,WorldModuleRegistry? modules=null,ContentCatalog? content=null)
    {
        return LoadSnapshot(JsonSerializer.Deserialize<WorldSnapshot>(json,SaveOptions)??throw new InvalidDataException("存档为空。"),creatureCatalog,creatureBehaviors,modules,content);
    }
    public static DemoWorld LoadSnapshot(WorldSnapshot s,CreatureCatalog? creatureCatalog=null,CreatureBehaviorRegistry? creatureBehaviors=null,WorldModuleRegistry? modules=null,ContentCatalog? content=null)
    {
        var catalog=content??ContentRuntime.Current;
        ValidateContentArchive(s,catalog);
        s.Generation?.Validate(false);
        if(s.EcologyVersion!=TerrainMap.CurrentEcologyVersion||s.GeneratorVersion!=TerrainMap.CurrentGeneratorVersion||
           (s.Region is not null&&!RegionProfile.All.Any(p=>p.Id==s.Region))||
           (s.Flat&&(s.Region is not null||s.Generation is not null))||
           (s.Generation is not null&&(s.Region!=s.Generation.Site.Region||s.Seed!=s.Generation.LocalSeed)))
            throw new InvalidDataException("不支持的存档或生成器版本，请新建游戏。");
        if(s.Items is null||s.Jobs is null||s.Workers is null||s.Stockpiles is null||s.Excavated is null||s.Buildings is null||s.Blueprint is null||
           s.Items.Length>200000||s.Jobs.Length>10000||s.Workers.Length>1000||(s.RemovedWorkers?.Length??0)>1000||!double.IsFinite(s.SimulationTime)||s.SimulationTime<0)
            throw new InvalidDataException("存档结构无效。");
        try{GameCalendar.At(s.SimulationTime);}catch(ArgumentOutOfRangeException){throw new InvalidDataException("日历时间超出支持范围。");}
        if(!double.IsFinite(s.ResidentElapsed)||s.ResidentElapsed<0||s.ResidentElapsed>=2)throw new InvalidDataException("人物时钟无效。");
        if((s.Research is null||s.Workers.Concat(s.RemovedWorkers??Array.Empty<WorkerState>()).Any(a=>a.Person is null)))throw new InvalidDataException("人物存档缺少资料。");
        var research=s.Research??new ColonyResearch();research.Validate();
        s.Generation?.Validate();
        if(s.ClimateVersion!=1||(s.WeatherOverride is not null&&!WeatherState.All.Any(x=>x.Id==s.WeatherOverride)))throw new InvalidDataException("天气存档无效。");
        var w=new DemoWorld(s.Seed,s.Flat,s.Region,s.Generation,generateFluids:false,content:catalog,generateContentTerrain:false);w.WeatherOverride=s.WeatherOverride;w.Research=research;w._residentElapsed=s.ResidentElapsed;w.Items.Clear();w.Stockpiles.Clear();w.Workers.Clear();w.Jobs.Clear();
        w._simulationTime=s.SimulationTime;if(modules is not null)w.Modules=modules;
        {
            if(s.Trees is null||s.Trees.Length>Width*Depth)throw new InvalidDataException("树木记录无效。");
            foreach(var c in w.Trees.Keys)w.Terrain.SetTree(c,false);
            w.Trees.Clear();
            foreach(var t in s.Trees)
            {
                if(!Inside(t.Cell)||!w.Terrain.Contains(t.Cell)||!double.IsFinite(t.GrowthUpdatedAt)||t.GrowthUpdatedAt<0||t.GrowthUpdatedAt>s.SimulationTime||!double.IsFinite(t.NextGrowthAt)||t.NextGrowthAt<0||!double.IsFinite(t.Age)||t.Age<0||!CropCatalog.IsTree(t.Species)||t.Age>GrowTime(t.Species)||t.MaximumHeight is <5 or >14||t.Cell.Level+t.MaximumHeight>TerrainMap.MaxLevel+1||!w.Trees.TryAdd(t.Cell,t))throw new InvalidDataException("树木记录无效。");
                w.Terrain.SetTree(t.Cell,true);if(t.Age<GrowTime(t.Species))w._treeUpdates.Schedule(t.Cell,t.NextGrowthAt);
            }
            w.TreeRevision++;
        }
        if(s.TerrainBlocks is null||s.TerrainBlocks.Length>200000)throw new InvalidDataException("地形定义记录过多或为空");
        w.Terrain.RestoreDefinitionBlocks(s.TerrainBlocks,catalog);
        foreach(var c in s.Excavated)if(!w.Terrain.Excavate(c))throw new InvalidDataException("挖掘记录越界、重复或无效。");
        if(s.PlacedBlocks is null||s.PlacedBlocks.Length>200000)throw new InvalidDataException("放置方块记录无效");
        w.Terrain.RestorePlacedBlocks(s.PlacedBlocks,catalog);
        foreach(var b in s.Buildings){if(b is not null&&(w.Terrain.IsPlaced(b.Cell)||TallFixture(b.Kind)&&w.Terrain.IsPlaced(b.Cell+new Cell(0,0,1))))throw new InvalidDataException("建筑与地形重叠");if(b is null||b.DefinitionId is null)throw new InvalidDataException("建筑定义为空");w.Buildings.Add(b.Cell,b);}
        if(s.Fluids is null)throw new InvalidDataException("缺少流体存档。");
        w.RestoreFluids(s.Fluids);
        if(s.Fires is null||s.Fires.Length>s.Buildings.Length)throw new InvalidDataException("篝火记录无效。");
        foreach(var f in s.Fires)
            if(!double.IsFinite(f.Fuel)||f.Fuel<=0||f.Fuel>330||!w.Buildings.TryGetValue(f.Cell,out var fire)||fire.Kind!=BuildKind.Campfire||!w.FireFuel.TryAdd(f.Cell,f.Fuel))throw new InvalidDataException("篝火燃料无效。");
        if(s.Damage is null||s.Damage.Length>200000)throw new InvalidDataException("方块损伤记录无效。");
        foreach(var d in s.Damage)
        {
            if(!Inside(d.Cell)||d.Block!=w.BlockAt(d.Cell)||!double.IsFinite(d.Damage)||d.Damage<=0||d.Damage>=catalog.Block(d.Block).Durability||!double.IsFinite(catalog.Block(d.Block).Durability)||!w._blockDamage.TryAdd(d.Cell,d))
                throw new InvalidDataException("方块损伤记录无效。");
        }
        foreach(var stack in s.Items)
        {
            catalog.Item(stack.Item);
            if(stack.Properties is null)throw new InvalidDataException("物品属性为空");
            stack.Properties.Validate();
            if(stack.DestinationZone< -1||stack.Id<=0||stack.Count<=0||stack.Count>1000000||!Inside(stack.Cell)||w.Items.Any(i=>i.Id==stack.Id))throw new InvalidDataException("物品记录无效。");
            w.Items.Add(stack);
        }
        foreach(var c in s.Stockpiles){if(!Inside(c))throw new InvalidDataException("堆场越界。");w.Stockpiles.Add(c);}
        if(s.StorageZones is null||s.StorageZones.Length>s.Stockpiles.Length)throw new InvalidDataException("堆场记录无效。");
        var zoneCells=new HashSet<Cell>();var zoneIds=new HashSet<int>();
        foreach(var z in s.StorageZones)
        {
            if(z.Id<=0||!zoneIds.Add(z.Id)||z.Priority is <1 or >5||z.Allowed is null||z.Cells is null||z.Allowed.Any(i=>!catalog.Items.Any(d=>d.Id==i))||z.Cells.Any(c=>!w.Stockpiles.Contains(c)||!zoneCells.Add(c)))throw new InvalidDataException("堆场设置无效。");
            w.StorageZones.Add(z);
        }
        if(s.Farms is null||s.Farms.Length>256||s.Plants is null||s.Plants.Length>16384)throw new InvalidDataException("植物存档无效。");
        foreach(var p in s.Farms)
            if(!Inside(p.Cell)||!CropCatalog.Valid(p.Crop)||!double.IsFinite(p.NextUpdateAt)||p.NextUpdateAt<0||p.Harvests<0||p.Harvests==int.MaxValue||!w.Farms.TryAdd(p.Cell,p))throw new InvalidDataException("农田记录无效。");
        foreach(var p in w.Farms.Values)w._farmUpdates.Schedule(p.Cell,p.NextUpdateAt);
        w.Plants.Clear();w._plantUpdates.Clear();
        foreach(var p in s.Plants)
        {
            if(!Inside(p.Cell)||!CropCatalog.Valid(p.Crop)||CropCatalog.IsTree(p.Crop)||!double.IsFinite(p.Growth)||p.Growth<0||p.Growth>GrowTime(p.Crop)||!double.IsFinite(p.GrowthUpdatedAt)||p.GrowthUpdatedAt<0||p.GrowthUpdatedAt>s.SimulationTime||!double.IsFinite(p.NextUpdateAt)||p.NextUpdateAt<0||w.Trees.ContainsKey(p.Cell)||!w.Plants.TryAdd(p.Cell,p))throw new InvalidDataException("植物记录无效。");
            w._plantUpdates.Schedule(p.Cell,p.NextUpdateAt);
        }
        {
            if(s.GrowingZones is null||s.GrowingZones.Length>s.Farms.Length)throw new InvalidDataException("种植区记录无效。");
            foreach(var zone in s.GrowingZones)
                if(zone.Id<=0||zone.Id==int.MaxValue||!CropCatalog.Valid(zone.Crop)||!w.Farms.Values.Any(p=>p.ZoneId==zone.Id)||!w.GrowingZones.TryAdd(zone.Id,zone))throw new InvalidDataException("种植区设置无效。");
            if(w.Farms.Values.Any(p=>!w.GrowingZones.ContainsKey(p.ZoneId)||p.Crop!=w.GrowingZones[p.ZoneId].Crop))throw new InvalidDataException("农田与种植区不一致。");
        }
        foreach(var group in s.Jobs.Where(j=>!string.IsNullOrEmpty(j.DigRegion)).GroupBy(j=>j.DigRegion))
            if(group.Any(j=>j.DigRegionMin!=group.First().DigRegionMin||j.DigRegionMax!=group.First().DigRegionMax))
                throw new InvalidDataException("开采区域范围不一致。");
        foreach(var j in s.Jobs)
        {
            if(j.DigRegion is null || (j.DigRegion=="" ? j.DigRegionMin is not null||j.DigRegionMax is not null :
                !Guid.TryParseExact(j.DigRegion,"N",out _)||!j.Dig||j.Demolish||j.DigRegionMin is not Cell lo||j.DigRegionMax is not Cell hi||
                !Inside(lo)||!Inside(hi)||lo.X>hi.X||lo.Z>hi.Z||lo.Level>hi.Level||
                j.Cell.X<lo.X||j.Cell.X>hi.X||j.Cell.Z<lo.Z||j.Cell.Z>hi.Z||j.Cell.Level<lo.Level||j.Cell.Level+j.DigHeight-1>hi.Level))
                throw new InvalidDataException("开采区域记录无效。");
            if(j.WaterPaused&&(!j.Dig||j.Demolish))throw new InvalidDataException("水患暂停任务类型无效。");
            if(j.Id<=0||!Inside(j.Cell)||!double.IsFinite(j.Progress)||j.Progress<0||j.Materials is null||w.Jobs.Any(o=>o.Id==j.Id)||j.Worker< -1||j.Worker>=s.Workers.Length||j.DigHeight is <1 or >3)
                throw new InvalidDataException("任务记录无效。");
            if(j.PlaceItem is null||j.PlaceBlock is null)throw new InvalidDataException("放置字段为空");
            if((j.Build is not null?1:0)+(j.Placing?1:0)+(j.Dig?1:0)+(j.Haul?1:0)+(j.Smelt?1:0)+(j.Cook?1:0)+(j.Sow?1:0)+(j.Harvest?1:0)+(j.Refuel?1:0)+(j.Chop?1:0)+(j.PlantTree?1:0)+(j.Processing?1:0)>1||j.Demolish&&(!j.Dig||j.DigHeight!=1))throw new InvalidDataException("任务类型冲突。");
            if(j.Recipe is null||j.Processing&&catalog.FindRecipe(j.Recipe) is null||j.PlantTree&&!CropCatalog.IsTree(j.TreeSpecies))throw new InvalidDataException("生产配方或树种无效。");
            RestoreJobRecipe(j,catalog);
            if(j.Processing)
            {
                var recipe=j.RecipeSnapshot!;
                if(j.Materials.Any(r=>!recipe.Inputs.ContainsKey(r.Item))||recipe.Inputs.Any(p=>j.Materials.Where(r=>r.Item==p.Key).Sum(r=>r.Count)!=p.Value))throw new InvalidDataException("加工材料不完整。");
            }
            if(j.Farming&&(j.Sow&&(!w.Farms.ContainsKey(j.Cell)||w.Plants.ContainsKey(j.Cell))||j.Harvest&&(!w.Plants.TryGetValue(j.Cell,out var plant)||plant.Growth<GrowTime(plant.Crop))||w.Jobs.Any(o=>o.Farming&&o.Cell==j.Cell)))throw new InvalidDataException("农务任务与植物不一致。");
            if(j.PlaceItem is null||j.PlaceBlock is null)throw new InvalidDataException("放置任务记录无效");
            if(j.BuildDefinitionId is null)throw new InvalidDataException("建筑任务定义为空");
            w.BindJobBuilding(j);
            if(j.BuildingWork)
            {
                if(j.Placing&&!w.PlacementSiteOpen(j))throw new InvalidDataException("放置位置被占用");
                var cost=w.BuildCost(j);
                if(j.Materials.Any(r=>r is null||r.Item is null||!cost.ContainsKey(r.Item))||cost.Any(p=>j.Materials.Where(r=>r.Item==p.Key).Sum(r=>(long)r.Count)!=p.Value))throw new InvalidDataException("建筑材料与定义不一致");
            }
            foreach(var r in j.Materials)
            {
                catalog.Item(r.Item);
                if(r.Properties is null)throw new InvalidDataException("搬运属性为空");
                r.Properties.Validate();
                if(r.DestinationZone< -1||r.Count<=0||r.Stage is <0 or >2||(r.Stage==1&&j.Worker<0)||
                    (r.Stage!=1&&!w.Items.Any(i=>i.Id==r.StackId&&i.Item==r.Item&&i.Owner=="")))throw new InvalidDataException("材料预约无效。");
            }
            if(j.Sow)
            {
                var sowPlot=w.Farms[j.Cell];
                if(j.Materials.Where(r=>r.Item==CropSeed(sowPlot.Crop)).Sum(r=>r.Count)!=1||
                   j.Materials.Where(r=>r.Item=="soil").Sum(r=>r.Count)!=(sowPlot.Crop=="mushroom"&&!sowPlot.Prepared?1:0)||
                   j.Materials.Any(r=>r.Item!=CropSeed(sowPlot.Crop)&&r.Item!="soil"))throw new InvalidDataException("播种材料不完整。");
            }
            if(j.Harvest&&j.Materials.Count!=0)throw new InvalidDataException("收获任务材料无效。");
            w.Jobs.Add(j);
        }
        foreach(var i in w.Items)if(w.Reserved(i.Id)>i.Count)throw new InvalidDataException("重复预约超出物品数量。");
        DemoWorker Restore(WorkerState a,int index,bool removed)
        {
            if(!double.IsFinite(a.X)||!double.IsFinite(a.Z)||a.Path is null||a.Appearance is null||
                !double.IsFinite(a.ClimbProgress)||a.ClimbProgress<0||a.ClimbProgress>=1||!Inside(new Cell((int)Math.Round(a.X),(int)Math.Round(a.Z),a.Level))||a.Path.Any(c=>!Inside(c))||(a.TravelFrom is Cell t&&!Inside(t)))throw new InvalidDataException("居民位置无效。");
            var person=a.Person??throw new InvalidDataException("人物存档缺少资料。");person.Validate();
            if(string.IsNullOrWhiteSpace(a.Id)||a.Id.Length>100||string.IsNullOrWhiteSpace(a.Name)||a.Name.Length>100||
                person.Activity is not null&&a.JobId is not null)throw new InvalidDataException("居民身份或活动冲突。");
            if(a.Combat is null||!double.IsFinite(a.Combat.Cooldown)||a.Combat.Cooldown<0||a.Combat.Cooldown>10||!double.IsFinite(a.Combat.RetryAt)||!double.IsFinite(a.Combat.FlashUntil)||a.Combat.Strikes<0||a.Combat.Target is null||a.Combat.Target.Length>100||a.Combat.Destination is Cell dest&&!Inside(dest)||a.Combat.Drafted&&(a.JobId is not null||person.Activity is not null))throw new InvalidDataException("战斗状态无效。");
            ValidateMeleeState(a.Combat);
            if(a.WaterEvacuating&&(a.JobId is not null||person.Activity is not null&&person.Activity.Kind!="rescue_carry"))throw new InvalidDataException("撤离状态与普通任务冲突。");
            if(string.IsNullOrWhiteSpace(person.SpeciesId))throw new InvalidDataException("缺少居民物种 ID");
            person.BindSpeciesCatalog(creatureCatalog?.Species.ContainsKey(person.SpeciesId)==true?creatureCatalog:catalog.Creatures);
            if(a.SpeciesFingerprint!=person.Species.Fingerprint)throw new InvalidDataException("居民物种配置已改变，请新建游戏："+person.SpeciesId);
            var worker=new DemoWorker{WaterEvacuating=a.WaterEvacuating,Combat=a.Combat,Id=a.Id,Name=a.Name,Appearance=a.Appearance,Person=person,X=a.X,Z=a.Z,Level=a.Level,TravelFrom=a.TravelFrom,ClimbProgress=a.ClimbProgress,Status=a.Status};
            foreach(var c in a.Path)worker.Path.Enqueue(c);
            if(a.JobId is int id)
            {
                if(removed)throw new InvalidDataException("移除的居民不能持有任务。");
                worker.Job=w.Jobs.SingleOrDefault(j=>j.Id==id&&j.Worker==index)??throw new InvalidDataException("任务归属不一致。");
            }
            return worker;
        }
        for(int i=0;i<s.Workers.Length;i++)w.Workers.Add(Restore(s.Workers[i],i,false));
        if(s.RemovedWorkers is not null)w._removedResidents=s.RemovedWorkers.Select((a,i)=>Restore(a,i,true)).ToList();
        if(s.BattleOutcome is not ("" or "victory" or "defeat")||s.Enemies is null||s.Enemies.Length>MaxStoryRaid||s.RaidSerial<0||s.RallyPoint is Cell rally&&!Inside(rally)||s.CivilianAlert&&s.RallyPoint is null)throw new InvalidDataException("袭击或集合点无效。");
        foreach(var enemy in s.Enemies){if(enemy.JobId is not null||enemy.Person?.Activity is not null)throw new InvalidDataException("敌人不能领取居民任务。");w.Enemies.Add(Restore(enemy,0,true));}
        w.RallyPoint=s.RallyPoint;w.CivilianAlert=s.CivilianAlert;w._raidSerial=s.RaidSerial;w.BattleOutcome=s.BattleOutcome;
        foreach(var actor in w.Workers.Concat(w.Enemies))
            if(!(w.Workers.Contains(actor)?w.Enemies:w.Workers).Any(o=>o.Id==actor.Combat.Target&&!o.Person.Incapacitated))actor.Combat.Target="";
        var residents=w.Workers.Concat(w._removedResidents??new()).Concat(w.Enemies).ToArray();
        if(residents.Select(a=>a.Id).Distinct().Count()!=residents.Length)throw new InvalidDataException("居民 ID 重复。");
        foreach(var item in w.Items)
        {
            if(item.Owner is null||item.Slot is null||item.Owner!=""&&!residents.Any(a=>a.Id==item.Owner)||
                item.Slot!=""&&(item.Owner==""||item.Count!=1||ResidentGear.Get(item.Item)?.Slot!=item.Slot))throw new InvalidDataException("物品归属或装备槽无效。");
        }
        foreach(var resident in residents)
        {
            var worn=w.Inventory(resident).Where(i=>i.Slot!="").ToArray();
            if(worn.Select(i=>i.Slot).Distinct().Count()!=worn.Length||worn.Any(i=>ResidentGear.Get(i.Item)?.TwoHanded==true)&&worn.Any(i=>i.Slot=="off"))throw new InvalidDataException("装备冲突。");
            if(resident.Person.Talents.Any(t=>t.Value>=2&&!w.Research.Completed.Contains(ResidentCatalog.Talents.First(n=>n.Skill==t.Key&&n.Rank==t.Value).Research)))throw new InvalidDataException("技能缺少研究前置。");
            w.RefreshWeight(resident);
        }
        if(s.MedicalBeds is null||s.MedicalBeds.Distinct().Count()!=s.MedicalBeds.Length||s.MedicalBeds.Any(c=>!w.Buildings.TryGetValue(c,out var b)||b.Kind!=BuildKind.Bed))throw new InvalidDataException("医疗床记录无效");
        if(s.WaterPoints is null||s.WaterPoints.Length>256||s.WaterPoints.Any(c=>!TerrainMap.InBounds(c)))throw new InvalidDataException("取水点无效");w.WaterPoints.UnionWith(s.WaterPoints);
        w.Modules.Restore(w,s.Modules??throw new InvalidDataException("缺少模块存档"));
        w.MedicalBeds.UnionWith(s.MedicalBeds);w.ValidateRescueArchive();
        foreach(var medic in w.Workers.Where(a=>a.Person.Activity?.Kind=="care_supply").ToArray())
            if(!w.Items.Any(i=>i.Id==medic.Person.Activity!.ItemId&&i.Item=="bandage"&&i.Owner=="")||!w.Workers.Any(a=>a.Id==medic.Person.Activity!.Target&&!a.Person.Dead))w.CancelResidentActivity(medic);
        foreach(var resident in w.Workers)
        {
            w.ValidateHomeBed(resident);
            if(resident.Person.Activity is {Kind:"food_supply" or "feed"} food&&!w.ValidFoodActivity(resident,food))w.CancelResidentActivity(resident);
        }
        w.RestoreTaskBindings();
        if(w.Workers.Where(r=>r.Person.HomeBed is not null).GroupBy(r=>r.Person.HomeBed).Any(g=>g.Count()>1)||
            w.Workers.Where(r=>r.Person.Activity is {Kind:"food_supply" or "feed"}).GroupBy(r=>r.Person.Activity!.Target).Any(g=>g.Count()>1))
            throw new InvalidDataException("床位或送饭预约冲突。");
        if(w.Items.Any(i=>w.Reserved(i.Id)>i.Count)||w.Workers.Where(a=>a.Person.Activity is {Kind:"treat" or "care_supply"}).GroupBy(a=>a.Person.Activity!.Target).Any(g=>g.Count()>1))throw new InvalidDataException("护理预约冲突。");
        if(w.Jobs.Any(j=>j.Worker>=0&&w.Workers[j.Worker].Job!=j))throw new InvalidDataException("任务重复指派。");
        w.Blueprint=s.Blueprint.ToList();foreach(var p in w.Blueprint)if(p is null||p.DefinitionId is null||w.BlueprintDefinition(p).Handler!=p.Kind)throw new InvalidDataException("蓝图定义无效");
        if(s.DemoStage is int ds){if(ds is <0 or >5)throw new InvalidDataException("示例阶段无效。");w.Demo=new TwoStoreyDemo(w.Camp);w.Demo.RestoreStage(ds);}
        if(s.UndergroundStage is int us){if(us is <0 or >8)throw new InvalidDataException("地下示例阶段无效。");w.Underground=new UndergroundDemo(w.Camp);w.Underground.RestoreStage(us);}
        w._nextItem=w.Items.Select(i=>i.Id).DefaultIfEmpty().Max();w._nextJob=w.Jobs.Select(j=>j.Id).DefaultIfEmpty().Max();
        w._nextConstructionGroup=w.Jobs.Select(j=>j.ConstructionGroup).DefaultIfEmpty().Max();w._simulationTime=s.SimulationTime;
        if(s.Story is null)throw new InvalidDataException("缺少叙事存档。");
        w.RestoreStory(s.Story);
        if(s.ResidentMind is null)throw new InvalidDataException("缺少居民决策存档。");
        w.RestoreResidentMind(s.ResidentMind);
        w.RestoreDialogues(s.Dialogues);
        w.RestoreRoomClimates(s.RoomClimates,s.RoomTemperatureUpdatedAt);
        w.RestoreProductionOrders(s.ProductionOrders,s.ProductionElapsed);
        if(s.Creatures is null)throw new InvalidDataException("缺少生物存档。");
        if(creatureCatalog is not null)w.ConfigureCreatures(creatureCatalog,creatureBehaviors);
        if(s.Creatures is { } creatures)
        {
            if(creatures.Creatures is null)throw new InvalidDataException("生物存档列表为空。");
            if(creatures.Creatures.Length>0||creatureCatalog is not null)w.Creatures.Restore(creatures,s.SimulationTime);
            else if(creatures.Version!=1||creatures.NextId<0||creatures.Cursor!=0)throw new InvalidDataException("生物存档结构无效。");
            else if(creatures.NextId>0)w.Creatures.Restore(creatures,s.SimulationTime);
        }
        w.RefreshWaterSafety();w.RefreshLifeAlerts();w.ValidateWorkshop();w.Revision++;w.Log("已读取营地，材料与在途任务已恢复。");return w;
    }
}
