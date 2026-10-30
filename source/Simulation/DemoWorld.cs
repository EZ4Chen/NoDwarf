namespace NoDwarf.Simulation;

public enum BuildKind { Wall, Door, Furnace, Anvil, Log, Planks, Cobblestone, StoneBricks, LogStairs, PlankStairs, CobbleStairs, BrickStairs, Stove, Bed, Campfire, Mill, Loom, Floodgate, Workbench, Ladder, Table, Chair, GameTable }
public readonly record struct Cell(int X, int Z, int Level = 0)
{
    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Z + b.Z, a.Level + b.Level);
}
public sealed record Building(Cell Cell, BuildKind Kind, int Facing = 0)
{
    public string DefinitionId {get;init;}="";
    [System.Text.Json.Serialization.JsonIgnore] public BuildingDefinition? Definition {get;init;}
    [System.Text.Json.Serialization.JsonIgnore] public BlockDefinition? BlockDefinition {get;init;}
    public string BlockId => Definition?.Block??(DefinitionId.Length==0?BlockCatalog.Id(Kind):DefinitionId);
}
public sealed record BlueprintPart(Cell Offset, BuildKind Kind, int Facing = 0) { public string DefinitionId {get;init;}=""; }

public sealed class DemoJob
{
    public ProcessingRecipe? RecipeSnapshot {get;set;}
    public string? RecipeRevision {get;set;}
    public string BuildDefinitionId {get;set;}="";
    public string PlaceItem {get;set;}="";
    public string PlaceBlock {get;set;}="";
    public bool Placing=>PlaceItem.Length!=0;
    public bool BuildingWork=>Build is not null||Placing;
    [System.Text.Json.Serialization.JsonIgnore] public BuildKind WorkKind=>Build??BuildKind.Wall;
    [System.Text.Json.Serialization.JsonIgnore] public BlockDefinition? ResolvedBuild {get;set;}
    public int Id { get; init; }
    public int Facing { get; init; }
    public int ProductionOrderId {get;set;}
    public int ConstructionGroup { get; set; }
    public string DigRegion { get; set; } = "";
    public Cell? DigRegionMin { get; set; }
    public Cell? DigRegionMax { get; set; }
    public Cell Cell { get; set; }
    public BuildKind? Build { get; init; }
    public bool Dig { get; init; }
    public bool Demolish {get;init;}
    public int DigHeight { get; init; } = 1;
    public bool WaterPaused { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string AcceptedWaterRisk { get; set; } = "";
    public bool Haul {get;init;}
    public bool Smelt {get;init;}
    public bool Cook {get;init;}
    public string Recipe {get;init;}="";
    public string TreeSpecies {get;init;}="sapling";
    public bool Processing=>Recipe!="";
    public bool Refuel {get;init;}
    public bool Chop {get;init;}
    public bool PlantTree {get;init;}
    public bool Forestry => Chop || PlantTree;
    public bool Sow {get;init;}
    public bool Harvest {get;init;}
    public bool Farming => Sow || Harvest;
    public bool Forge => !Placing && Build is null && !Dig && !Haul && !Smelt && !Cook && !Farming && !Refuel && !Forestry && !Processing;
    public bool Production => Forge || Smelt || Cook || Processing;
    public bool Construction => BuildingWork || Dig;
    public List<MaterialReservation> Materials {get;set;}=new();
    public int Worker { get; set; } = -1;
    public double Progress { get; set; }
    public const double BuildWorkRequired = 1.0;
    [System.Text.Json.Serialization.JsonIgnore]
    public double BuildFraction => !BuildingWork ? 0 : Math.Clamp(Progress / BuildWorkRequired, 0, 1);
    [System.Text.Json.Serialization.JsonIgnore]
    public double BuildDurability => BuildingWork ? (ResolvedBuild??BlockCatalog.Get(WorkKind)).Durability * BuildFraction : 0;
}

public sealed class DemoWorker
{
    public CombatOrder Combat {get;set;}=new();
    public required string Name { get; init; }
    public string Id { get; init; } = "resident";
    public CharacterAppearance Appearance { get; set; } = new();
    public ResidentProfile Person { get; set; } = new();
    public double X { get; set; }
    public double Z { get; set; }
    public int Level { get; set; }
    public Queue<Cell> Path { get; } = new();
    public DemoJob? Job { get; set; }
    public string Status { get; set; } = "等待工作";
    public Cell? TravelFrom { get; set; }
    public double ClimbProgress {get;set;}
    public long CheckedNavigationVersion { get; set; } = -1;
    public bool WaterEvacuating { get; set; }
    public string WaterIssue { get; set; } = "";
    public double WaterRetryAt { get; set; }
    public double NextJobSearch { get; set; }
    public double NextMedicalCheck {get;set;}
    public double NextCareCheck {get;set;}
    public double NextRescueCheck {get;set;}
    public string MedicalIssue {get;set;}="";
    public double NextPolicyCheck { get; set; }
    public Cell Tile => TravelFrom ?? new((int)Math.Round(X), (int)Math.Round(Z), Level);
}

// Small single-threaded demo, deliberately separate from Godot rendering.
public sealed partial class DemoWorld
{
    public const int Width = TerrainMap.Size, Depth = TerrainMap.Size;
    public TerrainMap Terrain { get; }
    public Cell Camp => Terrain.Flat ? new Cell(5, 4) : Terrain.At(TerrainMap.Size/2-2, TerrainMap.Size/2-3);
    public Cell Surface(int x, int z) => Terrain.At(x, z);
    public IEnumerable<Cell> Neighbors(Cell c)
    {
        if(LadderAt(c))yield return c+new Cell(0,0,1);
        if(LadderAt(c+new Cell(0,0,-1)))yield return c+new Cell(0,0,-1);
        foreach (var d in Directions)
        {
            int x = c.X + d.X, z = c.Z + d.Z;
            if (x >= 0 && x < Width && z >= 0 && z < Depth) for (int dy = -2; dy <= 1; dy++) yield return new Cell(x, z, c.Level + dy);
        }
    }
    private static readonly Cell[] Directions = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    public BuildingMap Buildings { get; }
    public WorldNavigation Navigation { get; }
    public PerformanceProbe? Probe { get; set; }
    public JobCollection Jobs { get; }
    public List<DemoWorker> Workers { get; } = new();
    public List<string> Events { get; } = new();
    public List<BlueprintPart> Blueprint { get; private set; } = new();
    public int Stone => Available("cobblestone");
    public int Wood => Available("wood");
    public int Iron => Available("iron");
    public int Coal => Available("coal");
    public int Swords => Total("sword");
    public int Revision { get; private set; }
    public bool HasRoom { get; private set; }
    public bool HasFurnace { get; private set; }
    public bool HasAnvil { get; private set; }
    public bool WorkshopReady => HasRoom && _forgeAnvil is not null;
    public string WorkshopIssue { get; private set; } = "建起围墙与门，把炉子和铁砧放在同一间屋里。";
    private Cell? _forgeAnvil;
    private Cell? _forgeFurnace;
    private int _nextJob, _nextConstructionGroup;
    private double _assignElapsed;
    private double _simulationTime;
    private int _assignmentCursor;
    public int NavigationRequestsPerTick { get; set; } = 8;
    public TwoStoreyDemo? Demo { get; private set; }
    public bool StartTwoStoreyDemo()
    {
        for(int x=0;x<6;x++) for(int z=0;z<8;z++)
        {
            var c=Camp+new Cell(x,z);
            if (!Terrain.Contains(c) || NaturalRamp(c) || Terrain[c.X,c.Z].Tree || Buildings.Keys.Any(b=>b.X==c.X&&b.Z==c.Z) || Jobs.Any(j=>j.Cell.X==c.X&&j.Cell.Z==c.Z))
            { Log("示例需要营地 6×8 范围为空；请先拆除原工坊，或生成新地图。"); return false; }
        }
        Demo=new TwoStoreyDemo(Camp); Demo.Advance(this); return true;
    }

    public DemoWorld(int seed = 74219, bool flat = false, string? region = null,WorldGenerationSettings? generation=null,bool generateFluids=true,ContentCatalog? content=null,bool generateContentTerrain=true)
    {
        Content=content??ContentRuntime.Current;
        Jobs=new JobCollection(j=>{BindJobBuilding(j);BindJobRecipe(j);});
        Terrain = new TerrainMap(seed, flat, region,generation);
        Terrain.Definitions=Content;
        if(generateContentTerrain)Terrain.GenerateDefinitionOres(Content);
        Navigation = new WorldNavigation(this);
        Buildings = new BuildingMap(GeometryChanged,BindBuilding);
        Terrain.Changed += GeometryChanged;
        InitializeFluids(generateFluids);
        Workers.Add(new DemoWorker { Id = "azhen", Name = "阿榛", X = 2, Z = 10 });
        Workers.Add(new DemoWorker { Id = "mila", Name = "米拉", X = 3, Z = 11 });
        Workers.Add(new DemoWorker { Id = "shiya", Name = "石芽", X = 1, Z = 10 });
        for (int i = 0; i < Workers.Count; i++)
        {
            Workers[i].Appearance = CharacterAppearance.Randomize(new Random(unchecked(seed * 397 + i * 7919)));
            Workers[i].Person = ResidentProfile.Create(i);
            Workers[i].Person.BindSpeciesCatalog(Content.Creatures);
        }
        if (!flat)
            for (int i = 0; i < Workers.Count; i++)
            { Workers[i].X = Camp.X + i; Workers[i].Z = Camp.Z + 7; Workers[i].Level = Terrain[Camp.X + i, Camp.Z + 7].Level; }
        SeedTrees(); if(!flat&&generateContentTerrain)SeedWildPlants(); SeedItems(); SeedResidentItems(); SpawnItem("sapling",12,Camp+new Cell(5,8));
        Log($"三位居民抵达。地图 {Width}×{Depth}，种子 {seed}。");
    }

    public static string NameOf(BuildKind kind) => BlockCatalog.Get(kind).Name;
    public static string CostOf(BuildKind kind)
    {
        var c = BlockCatalog.Get(kind).Cost;
        return c.Stone > 0 ? $"石料 {c.Stone}" : c.Wood > 0 ? $"木料 {c.Wood}" : $"铁锭 {c.Iron}";
    }
    public static Cell StairDirection(int facing) => ((facing % 4 + 4) % 4) switch
    { 0 => new Cell(1, 0), 1 => new Cell(0, 1), 2 => new Cell(-1, 0), _ => new Cell(0, -1) };
    // Natural terrain is a solid column; placed voxels are keyed by all three coordinates.
    // Cell.Level is the bottom of a voxel / feet height of a navigation node, in metres.
    public bool NaturalSolid(Cell c) => Terrain.Solid(c);
    public bool Solid(Cell c) => NaturalSolid(c) || (Buildings.TryGetValue(c, out var b) && BlockCatalog.IsWall(b.Kind));
    public bool NaturalRamp(Cell c) => Terrain.Ramp(c);
    public string BlockAt(Cell c) => Buildings.TryGetValue(c,out var b)?b.BlockId:Trees.ContainsKey(c)?"nodwarf:tree":Terrain.BlockAt(c);
    private static bool TallFixture(BuildKind kind) => kind is BuildKind.Door or BuildKind.Furnace;
    private bool Occupied(Cell c) => !Inside(c) || NaturalSolid(c) || NaturalRamp(c) || Buildings.ContainsKey(c) ||
        (Terrain[c.X, c.Z].Tree && c.Level >= Terrain[c.X, c.Z].Level && c.Level < Terrain[c.X, c.Z].Level + TreeHeight(Surface(c.X,c.Z))) ||
        (Buildings.TryGetValue(c + new Cell(0, 0, -1), out var under) && TallFixture(under.Kind));
    private bool PlannedSolid(Cell c) => Jobs.Any(j => j.Cell == c && j.BuildingWork && BlockCatalog.IsWall(j.WorkKind));
    private bool Supported(Cell c, bool plans = false,IReadOnlySet<Cell>? previewSolids=null) => Solid(c + new Cell(0, 0, -1)) ||
        (plans && (PlannedSolid(c + new Cell(0, 0, -1))||previewSolids?.Contains(c+new Cell(0,0,-1))==true)) ||
        Directions.Any(d => Solid(c + d) || (plans && (PlannedSolid(c + d)||previewSolids?.Contains(c+d)==true)));
    public bool CanPlace(Cell c) => CanPlaceSite(c,null);
    private bool CanPlaceSite(Cell c,IReadOnlySet<Cell>? previewSolids) => Inside(c) && c.Level > TerrainMap.MinLevel && c.Level <= TerrainMap.MaxLevel && !Occupied(c) && Fluids.Amount(c)==0 &&
        !Jobs.Any(j => j.Construction && (j.Cell == c || (j.Build is BuildKind plannedKind && TallFixture(plannedKind) && j.Cell + new Cell(0, 0, 1) == c))) && Supported(c, true,previewSolids);
    public bool CanPlace(Cell c, BuildKind kind, int facing = 0,IReadOnlySet<Cell>? previewSolids=null)
    {
        // Preview support is only for solid line planning, never physical movement or completed work.
        if (!CanPlaceSite(c,BlockCatalog.IsWall(kind)?previewSolids:null)) return false;
        if(kind==BuildKind.Ladder)return Solid(c+StairDirection(facing))||PlannedSolid(c+StairDirection(facing));
        if(BlockCatalog.IsStairs(kind)&&Enumerable.Range(1,2).Any(h=>Occupied(c+new Cell(0,0,h))))return false;
        if (!BlockCatalog.IsWall(kind) && !Solid(c + new Cell(0, 0, -1)) && !PlannedSolid(c + new Cell(0, 0, -1))) return false;
        if (TallFixture(kind) && (Occupied(c + new Cell(0, 0, 1)) || Jobs.Any(j => j.Construction && j.Cell == c + new Cell(0, 0, 1)))) return false;
        return true;
    }
    public static bool Inside(Cell c) => TerrainMap.InBounds(c);
    public bool Walkable(Cell c) => WalkableCore(c,false);
    private bool WalkableCore(Cell c,bool ignoreWater)
    {
        if (!Inside(c) || c.Level <= TerrainMap.MinLevel || c.Level > TerrainMap.MaxLevel || NaturalSolid(c) || (!ignoreWater && Fluids.Amount(c)>=500)) return false;
        if (Terrain[c.X, c.Z].Tree && c.Level >= Terrain[c.X,c.Z].Level && c.Level < Terrain[c.X, c.Z].Level + TreeHeight(Surface(c.X,c.Z))) return false;
        bool stair = Buildings.TryGetValue(c, out var b) && BlockCatalog.IsStairs(b.Kind);
        if (b is not null && b.Kind != BuildKind.Door && !(b.Kind==BuildKind.Floodgate&&GateOpen(c)) && !stair && b.Kind!=BuildKind.Ladder) return false;
        if (!Solid(c + new Cell(0, 0, -1))&&!LadderAt(c)&&!LadderAt(c+new Cell(0,0,-1))) return false;
        // 1.4 m residents require two clear voxels; stairs need clearance at their high end.
        if (BodyBlocked(c + new Cell(0, 0, 1)) && b?.Kind != BuildKind.Door) return false;
        if ((stair || NaturalRamp(c)) && Occupied(c + new Cell(0, 0, 2))) return false;
        return true;
    }
    public bool LadderAt(Cell c)=>Buildings.TryGetValue(c,out var b)&&b.Kind==BuildKind.Ladder;
    private bool BodyBlocked(Cell c)=>Occupied(c)&&!LadderAt(c);
    public bool LadderLink(Cell a,Cell b)=>a.X==b.X&&a.Z==b.Z&&Math.Abs(a.Level-b.Level)==1&&LadderAt(a.Level<b.Level?a:b);
    private Cell? Slope(Cell c)
    {
        if (Buildings.TryGetValue(c, out var b) && BlockCatalog.IsStairs(b.Kind)) return StairDirection(b.Facing);
        if (NaturalRamp(c)) { var g = Terrain[c.X, c.Z]; return new Cell(g.RampX, g.RampZ); }
        return null;
    }
    public float NodeHeight(Cell c, double localX = 0, double localZ = 0)
    {
        var slope = Slope(c);
        return (float)(c.Level + (slope is Cell d ? Math.Clamp(.5 + localX * d.X + localZ * d.Z, 0, 1) : 0));
    }
    public bool Connected(Cell a, Cell b) => ConnectedCore(a,b,false);
    private bool ConnectedCore(Cell a, Cell b,bool escapeOrigin)
    {
        if(LadderLink(a,b))return WalkableCore(a,escapeOrigin)&&Walkable(b);
        int dx = b.X - a.X, dz = b.Z - a.Z;
        if (Math.Abs(dx) + Math.Abs(dz) != 1 || b.Level-a.Level > 1 || a.Level-b.Level > 2 || !WalkableCore(a,escapeOrigin) || !Walkable(b)) return false;
        // Match the height of the shared edge. Stairs cannot be entered through their sides.
        if (Slope(a) is Cell sa && dx * sa.X + dz * sa.Z == 0) return false;
        if (Slope(b) is Cell sb && dx * sb.X + dz * sb.Z == 0) return false;
        if(Math.Abs(NodeHeight(a, dx * .5, dz * .5) - NodeHeight(b, -dx * .5, -dz * .5)) < .001f)return true;
        if(!ClimbEdge(a,b))return false;
        var low=a.Level<b.Level?a:b;
        // The body rises above the low cell before crossing the ledge: 2.4 m clearance.
        for(int y=low.Level+2;y<=Math.Max(a.Level,b.Level)+1;y++)if(BodyBlocked(low with{Level=y}))return false;
        return true;
    }
    public bool ClimbEdge(Cell a,Cell b)=>b.Level-a.Level is >=-2 and <=1&&a.Level!=b.Level&&Slope(a) is null&&Slope(b) is null;
    public int TraversalCost(Cell a,Cell b)=>LadderLink(a,b)?4:ClimbEdge(a,b)?(a.Level-b.Level==2?4:3):1;
    public float StandingHeight(double x, double z) => NodeHeight(Surface((int)Math.Round(x), (int)Math.Round(z)), x - Math.Round(x), z - Math.Round(z));
    public float WorkerHeight(DemoWorker worker)
    {
        var from = worker.Tile;
        if (worker.Path.TryPeek(out var to))
        {
            if(LadderLink(from,to))return (float)(from.Level+(to.Level-from.Level)*worker.ClimbProgress);
            double t = Math.Abs(worker.X - from.X) + Math.Abs(worker.Z - from.Z);
            if(ClimbEdge(from,to))
            {
                t=Math.Clamp(t,0,1);
                double phase=to.Level>from.Level?Math.Clamp(t/.45,0,1):Math.Clamp((t-.55)/.45,0,1);
                phase=phase*phase*(3-2*phase);
                return (float)(from.Level+(to.Level-from.Level)*phase);
            }
            // Evaluate each half of the edge in its own voxel, preserving flat landings.
            return t < .5 ? NodeHeight(from, worker.X - from.X, worker.Z - from.Z) : NodeHeight(to, worker.X - to.X, worker.Z - to.Z);
        }
        return NodeHeight(from);
    }

    public bool QueueBuild(Cell cell, BuildKind kind, bool announce = true, int facing = 0)
        =>QueueBuild(cell,BlockCatalog.Id(kind),announce,facing);
    private void Refund(Building building,Cell cell)
    {foreach(var p in BuildingDefinition(building).Cost)SpawnItem(p.Key,p.Value,cell);SettleItems();}

    public void Erase(Cell c)
    {
        _supportSafety.Clear();
        var pending = Jobs.FirstOrDefault(j => j.Construction && j.Cell == c);
        if (pending is not null)
        {
            Release(pending);
            Jobs.Remove(pending);

            Revision++;
            Log("已取消规划，材料解除预留并保留在实际位置。");
            return;
        }
        if(Buildings.TryGetValue(c,out var support) && BlockCatalog.IsWall(support.Kind) && Workers.Any(w=>w.Tile==c+new Cell(0,0,1) || (w.Path.TryPeek(out var next)&&next==c+new Cell(0,0,1))))
        { Log("居民正站在这块楼板上或经过它；请先移开居民，再拆除。");return; }
        if(Buildings.ContainsKey(c)&&RemovalEndangersResident(c)){Log("先移开受支撑建筑上的居民再拆除。");return;}
        if (!Buildings.Remove(c, out var building)) return;
        _blockDamage.Remove(c);
        Refund(building,c); // Demo deliberately uses full refunds.
        Revision++;
        ValidateWorkshop();
        Log($"已拆除{NameOf(building.Kind)}，材料退回。");
    }

    public bool QueueSword()
    {
        if (!WorkshopReady) { Log("工坊尚未就绪：" + WorkshopIssue); return false; }
        if (Content.Recipe("sword").Inputs.Any(p=>Available(p.Key)<p.Value)) { Log("锻造需要 2 铁锭和 1 煤炭。"); return false; }
        if (Jobs.Count(j => j.Forge) >= 3) { Log("锻造队列已满，先等当前订单完成。"); return false; }
        var job=new DemoJob { Id = ++_nextJob, Cell = _forgeAnvil!.Value };
        if(!Reserve(job,Content.Recipe("sword").Inputs))return false;
        job.ProductionOrderId=AddProductionOrder("sword",station:job.Cell).Id;Jobs.Add(job);Revision++;
        Log("铁剑订单已下达，原料已预留。");
        return true;
    }

    public static List<BlueprintPart> ExampleBlueprint()
    {
        var parts = new List<BlueprintPart>();
        for (int x = 0; x < 6; x++)
        for (int z = 0; z < 5; z++)
            if (x == 0 || x == 5 || z == 0 || z == 4)
                parts.Add(new BlueprintPart(new Cell(x, z), x == 2 && z == 4 ? BuildKind.Door : BuildKind.Wall));
        parts.Add(new BlueprintPart(new Cell(1, 1), BuildKind.Furnace));
        parts.Add(new BlueprintPart(new Cell(4, 1), BuildKind.Anvil));
        return parts;
    }

    public bool QueueBlueprint(Cell origin, IReadOnlyList<BlueprintPart> parts)
    {
        if (parts.Count == 0) { Log("先保存一份蓝图。"); return false; }
        if(parts.Any(p=>p is null||p.DefinitionId is null||BlueprintDefinition(p).Handler!=p.Kind))throw new InvalidDataException("蓝图建筑定义无效");
        if (parts.Select(p=>p.Offset).Distinct().Count()!=parts.Count||parts.Any(p => !CanPlace(origin + p.Offset, p.Kind, p.Facing)))
        { Log("蓝图需要同高空地，不能跨坡、穿树或覆盖已有建筑。"); return false; }
        var costs=parts.SelectMany(p=>BlueprintDefinition(p).Cost).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>(long)p.Value));
        if(costs.Any(p=>Available(p.Key)<p.Value)){Log("蓝图所需材料不足。");return false;}
        int group = ++_nextConstructionGroup;
        foreach (var part in parts)
        {
            if(!QueueBuild(origin + part.Offset, BlueprintDefinition(part).Id, false, part.Facing))return false;
            Jobs[^1].ConstructionGroup = group;
        }
        Log($"已规划 {parts.Count} 个构件，三位居民开始合作施工。");
        return true;
    }

    public void SaveBlueprint()
    {
        if (Buildings.Count == 0) { Log("还没有已建成的建筑可以保存。"); return; }
        if (Buildings.Keys.Select(c => c.Level).Distinct().Count() > 1)
        { Log("此版蓝图仅支持同一高度，请先保留单层布局。"); return; }
        int minX = Buildings.Keys.Min(c => c.X), minZ = Buildings.Keys.Min(c => c.Z);
        Blueprint = Buildings.Values.Select(b => new BlueprintPart(new Cell(b.Cell.X - minX, b.Cell.Z - minZ), b.Kind, b.Facing){DefinitionId=b.DefinitionId}).ToList();
        Log($"已保存当前全部建筑为蓝图，共 {Blueprint.Count} 个构件（本次游玩内有效）。");
    }

    public void Update(double dt)
    {
        using var timing = Probe?.Measure("simulation.total") ?? default;
        if(!double.IsFinite(dt)||dt<=0)return;
        if(_domainEvents is not null)foreach(var command in _domainEvents.TakeCommands())ActResident(command);
        _supportSafety.Clear();
        _simulationTime += dt;
        AdvanceStoryEvents();
        foreach(var actor in Actors)actor.Combat.Cooldown=Math.Max(0,actor.Combat.Cooldown-dt);
        FlushBlockChanges();
        AdvanceSupport();
        Fluids.Advance(dt);
        RefreshWaterSafety();
        _creatures?.Advance(_simulationTime);
        UpdateHomes(dt);UpdateRoomTemperatures();UpdateTrees(dt);
        UpdateFarming(dt);UpdateProductionOrders(dt);
        _residentElapsed += dt;
        if(_residentElapsed>=2){UpdateResidentNeeds(_residentElapsed);StepLife(_residentElapsed);AssignLifeNeeds();_residentElapsed=0;}
        _assignElapsed += dt;
        bool assign = _assignElapsed >= 0.25;
        if (assign) { foreach(var cached in _jobAccess.Keys.Where(j=>!Jobs.Contains(j)).ToArray()) _jobAccess.Remove(cached); _assignElapsed = 0; Demo?.Advance(this); Underground?.Advance(this); ScheduleHomeJobs(); ScheduleHauling(); SettleItems(); }
        int budget = Math.Max(1, NavigationRequestsPerTick);
        foreach(var enemy in Enemies)UpdateMilitary(enemy,dt,ref budget,true);
        EmergencyMedical();
        int first = _assignmentCursor;
        // Broken routes take precedence over idle job searches. Otherwise hundreds
        // of idle residents can consume the budget before a blocked walker is served.
        for (int offset = 0; offset < Workers.Count; offset++)
        {
            var worker = Workers[(first + offset) % Workers.Count];
            if (!worker.WaterEvacuating && !FloodThreat(worker) && worker.Path.Count > 0) EnsureWorkerRoute(worker, ref budget);
        }
        for (int offset = 0; offset < Workers.Count; offset++)
        {
            int i = (first + offset) % Workers.Count;
            var worker = Workers[i];
            if(UpdateWaterEscape(worker,dt,ref budget))continue;
            if(UpdateMedicalPatient(worker,dt))continue;
            if(UpdateMilitary(worker,dt,ref budget))continue;
            if(ApplyResidentSchedule(worker,ref budget))continue;
            if(worker.Person.Activity is {Kind:"research",Origin:"work"}&&_simulationTime>=worker.NextJobSearch&&budget>0)
            {Assign(worker,i);budget--;worker.NextJobSearch=_simulationTime+2;}
            if(UpdateResidentActivity(worker,dt,ref budget))continue;
            if (worker.Job is null && worker.Path.Count == 0 && Jobs.Any(j => PlannedHazard(j,worker.Tile)))
            {
                foreach (var direction in Directions)
                {
                    var raw = worker.Tile + direction;
                    if (!Inside(raw)) continue;
                    var aside = raw;
                    if (Connected(worker.Tile, aside) && Walkable(aside) && !Jobs.Any(j => PlannedHazard(j,aside)))
                    { worker.Path.Enqueue(aside); break; }
                }
            }
            if (worker.Job is null && worker.Path.Count > 0)
            {
                if (!EnsureWorkerRoute(worker, ref budget)) continue;
                if (worker.Path.Count == 0) continue;
                if (!Walkable(worker.Path.Peek()) || (worker.Tile != worker.Path.Peek() && !Connected(worker.Tile, worker.Path.Peek()))) { StopMovement(worker); worker.Status = "道路被阻挡"; }
                else { Move(worker, dt); worker.Status = "移动中"; }
                continue;
            }
            if (worker.Job is null && _simulationTime >= worker.NextJobSearch && budget > 0)
            {
                Assign(worker, i); budget--;
                worker.NextJobSearch = _simulationTime + 0.25;
                _assignmentCursor = (i + 1) % Workers.Count;
            }
            var job = worker.Job;
            if (job is null) continue;
            if(job.Refuel&&(!Buildings.TryGetValue(job.Cell,out var fire)||fire.Kind!=BuildKind.Campfire)||job.Forestry&&!ForestryReady(job)){CancelJob(job);continue;}
            if(job.Farming&&!FarmReady(job)){CancelJob(job);continue;}
            if (job.Production && (!ProductionReady(job)||!BillAllowsWorker(job,worker)))
            {
                Release(job);
                worker.Status = "等待工坊恢复";
                continue;
            }
            if (worker.Path.Count > 0)
            {
                if (!EnsureWorkerRoute(worker, ref budget)) continue;
                if (worker.Path.Count == 0) continue;
                if (!Walkable(worker.Path.Peek()) || (worker.Tile != worker.Path.Peek() && !Connected(worker.Tile, worker.Path.Peek()))) { Release(job); continue; }
                Move(worker, dt);
                worker.Status = job.Forge ? "前往铁砧" : "走向施工位置";
                continue;
            }
            if(job.Placing&&!PlacementSiteOpen(job)){CancelJob(job);continue;}
            if(AdvanceDelivery(worker,job))continue;
            if(job.Placing&&!PlacementReady(job)){worker.Status="等待放置位置与支撑";continue;}
            if (job.BuildingWork && (Fluids.Amount(job.Cell)>0 || !Supported(job.Cell) || (job.Build==BuildKind.Ladder&&!Solid(job.Cell+StairDirection(job.Facing))) || Actors.Any(other => IntersectsWorker(job.Cell, job.WorkKind, other))))
            { worker.Status = "等待施工位置腾空"; continue; }
            if (!Walkable(worker.Tile)) { Release(job); continue; }
            if(job.Dig && !DigSafe(job)) { Release(job); worker.Status="等待挖掘空间安全"; continue; }
            worker.Status = job.Processing ? "加工物资" : job.Refuel ? "添柴" : job.Chop ? "伐木" : job.PlantTree ? "种树" : job.Sow ? "播种" : job.Harvest ? "收获" : job.Demolish ? "拆除方块" : job.Dig ? "挖掘岩土" : job.Cook ? "烹制口粮" : job.Smelt ? "熔炼粗铁" : job.Forge ? "锻造铁剑" : $"建造{BuildName(job)}";
            double work=dt * worker.Person.WorkRate(JobSkill(job)) * (1+GearWorkBonus(worker,job));
            job.Progress += work;
            if(job.Chop ? DamageBlock(job.Cell,work*20) : job.Dig ? MineStep(job,work) : job.Progress >= (job.Chop ? TreeHeight(job.Cell)*.8 : job.PlantTree?3 : job.Farming ? 2.0 : job.Production ? JobRecipe(job).Work : DemoJob.BuildWorkRequired))Complete(job,worker);
        }
        AdvanceMelee(dt);
        UpdateBattleOutcome();
        _domainEvents?.Dispatch();
    }

    private static bool SameConstructionSite(DemoJob equipment, DemoJob shell)
    {
        if (equipment.ConstructionGroup != 0 && equipment.ConstructionGroup == shell.ConstructionGroup) return true;
        // Individually placed plans have no blueprint group; only nearby shell work can block them.
        return Math.Abs(equipment.Cell.Level - shell.Cell.Level) <= 2 &&
            Math.Abs(equipment.Cell.X - shell.Cell.X) + Math.Abs(equipment.Cell.Z - shell.Cell.Z) <= 2;
    }

    private void Assign(DemoWorker worker, int index)
    {
        using var timing = Probe?.Measure("jobs.assign") ?? default;
        var targets = _assignmentTargets; targets.Clear();
        var occupiedStands = _occupiedStands; occupiedStands.Clear();
        var occupiedJobs = _occupiedJobs; occupiedJobs.Clear();
        _residentFootCells.Clear();
        foreach (var w in Workers)
        {
            _residentFootCells.Add(w.Tile);
            if (w.Path.TryPeek(out var next)) _residentFootCells.Add(next);
            if (w.Job is not null) occupiedStands.Add(AssignmentDestination(w));
        }
        foreach (var j in Jobs) if (j.Worker >= 0) occupiedJobs.Add(j.Cell);
        int bestRank=int.MaxValue;
        int component=Navigation.ComponentAt(worker.Tile);
        foreach (var job in OrderedJobs())
        {
            if (job.Worker >= 0 || !BillAllowsWorker(job,worker) || !CheckDigWater(job)) continue;
            int rank=ResidentWork.Rank(worker.Person,ResidentWork.JobType(job));
            if(rank==int.MaxValue||rank>bestRank)continue;
            if(job.Placing&&(!PlacementSiteOpen(job)||!PlacementSupported(job.Cell,Content.Placement(job.PlaceItem,job.PlaceBlock),false)))continue;
            if (job.Build is BuildKind.Furnace or BuildKind.Anvil && PendingConstructionShell(job)) continue;
            if(job.Forestry&&!ForestryReady(job))continue;
            if(job.Farming&&!FarmReady(job))continue;
            if (job.Production && (!ProductionReady(job)||!BillAllowsWorker(job,worker))) continue;
            if (job.BuildingWork && (Fluids.Amount(job.Cell)>0 || !Supported(job.Cell))) continue;
            if(job.Build==BuildKind.Ladder&&!Solid(job.Cell+StairDirection(job.Facing)))continue;
            if(job.Dig && !DigSafe(job, _residentFootCells)) continue;
            if (occupiedJobs.Contains(job.Cell)) continue;
            if (JobAccess(job).Length == 0) continue;
            if(!PrepareMaterials(worker,job))continue;
            bool requireFlat = !job.Haul && MaterialsDelivered(job) && !job.Dig && !(job.Build is BuildKind stair && BlockCatalog.IsStairs(stair));
            foreach(var stand in AssignmentPositions(job))
            {
                if (requireFlat && Slope(stand) is not null) continue;
                if(occupiedStands.Contains(stand) || Fluids.Amount(stand)>=EvacuationWater) continue;
                if(component==0||Navigation.ComponentAt(stand)!=component||!Navigation.CanReach(stand,worker.Tile))continue;
                if(rank<bestRank){bestRank=rank;targets.Clear();}
                targets.TryAdd(stand,job);
            }
        }
        if(AssignPersonalWork(worker,bestRank))return;
        if(targets.Count==0) { worker.Status=Jobs.Count>0?"暂时无可达工作":"等待工作"; return; }
        var path = Navigation.FindNearest(worker.Tile, targets.Keys, out var endpoint);
        if (path is null) { worker.Status="暂时无可达工作"; return; }
        if(worker.Person.Activity is {Kind:"research",Origin:"work"} research)
        {worker.Person.PausedResearch=research;CancelResidentActivity(worker);}
        worker.Path.Clear(); foreach (var step in path) worker.Path.Enqueue(step);
        worker.CheckedNavigationVersion = Navigation.Version;
        worker.Job=targets[endpoint];worker.Job.Worker=index;
    }

    private bool EnsureWorkerRoute(DemoWorker worker, ref int budget)
    {
        using var timing = Probe?.Measure("navigation.validate-worker-route") ?? default;
        if(worker.Path.TryPeek(out var step)&&worker.Tile.Level-step.Level==2&&
            (worker.Job is not null||worker.Person.Activity is {Origin:not "manual"})&&!Navigation.CanReach(step,worker.Tile))
        {
            if(worker.Job is {} job)Release(job);else {CancelResidentActivity(worker);StopMovement(worker);}
            worker.Status="等待安全返程路线";return false;
        }
        if (Navigation.PathValid(worker.Tile, worker.Path, worker.CheckedNavigationVersion))
        { worker.CheckedNavigationVersion = Navigation.Version; return true; }
        if (budget <= 0) { worker.Status = "等待重新寻路"; return false; }
        budget--;
        var destination = worker.Path.Last();
        StopMovement(worker);
        var replacement = FindPath(worker.Tile, destination);
        worker.CheckedNavigationVersion = Navigation.Version;
        if (replacement is null)
        {
            if (worker.Job is not null) Release(worker.Job);
            worker.NextJobSearch = _simulationTime + 0.25;
            worker.Status = "道路被阻挡";
            return false;
        }
        foreach (var cell in replacement) worker.Path.Enqueue(cell);
        return true;
    }

    private void Move(DemoWorker worker, double dt)
    {
        _supportSafety.Clear();
        worker.TravelFrom ??= worker.Tile;
        var next = worker.Path.Peek();
        if(LadderLink(worker.Tile,next))
        {
            worker.ClimbProgress+=dt*.8*worker.Person.MoveRate;
            if(worker.ClimbProgress>=1){worker.Level=next.Level;worker.TravelFrom=null;worker.ClimbProgress=0;worker.Path.Dequeue();}
            return;
        }
        double dx = next.X - worker.X, dz = next.Z - worker.Z;
        double distance = Math.Sqrt(dx * dx + dz * dz), move = dt * 2.5 * worker.Person.MoveRate / TraversalCost(worker.Tile,next);
        if (distance <= move)
        { worker.X = next.X; worker.Z = next.Z; worker.Level = next.Level; worker.TravelFrom = null; worker.Path.Dequeue(); }
        else { worker.X += dx / distance * move; worker.Z += dz / distance * move; }

    }

    private static void StopMovement(DemoWorker worker)
    {
        var c = worker.Tile; worker.X = c.X; worker.Z = c.Z; worker.Level = c.Level;
        worker.TravelFrom = null; worker.ClimbProgress=0; worker.Path.Clear();
    }
    private IEnumerable<Cell> ActionPositions(DemoJob job)
    {
        if(job.Forestry||job.Refuel){foreach(var d in Directions)yield return job.Cell+d;yield break;}
        if(job.Farming){yield return job.Cell;foreach(var d in Directions)yield return job.Cell+d;yield break;}
        if(job.Dig)
        {
            foreach(var d in Directions)
            {
                yield return job.Cell+d;
                yield return job.Cell+d+new Cell(0,0,1);
                if(job.DigHeight==1)yield return job.Cell+d+new Cell(0,0,-1);
            }
            yield break;
        }
        if(job.Placing)foreach(var d in Directions)yield return job.Cell+d+new Cell(0,0,1);
        if(job.Build==BuildKind.Ladder){yield return job.Cell;yield return job.Cell+new Cell(0,0,1);foreach(var d in Directions)yield return job.Cell+d+new Cell(0,0,1); }
        // A resident on the upper landing can install a stair into the excavated cell below.
        if(job.Build is BuildKind stairKind && BlockCatalog.IsStairs(stairKind))
            foreach(var d in Directions)yield return job.Cell+d+new Cell(0,0,1);
        // Build up to two metres above one's feet, including a ceiling directly overhead.
        for (int dy = 0; dy >= (job.Production ? 0 : -2); dy--)
        {
            foreach (var d in Directions) yield return job.Cell + d + new Cell(0, 0, dy);
            // Arms can reach a corner diagonally (1.42 m); navigation itself remains cardinal.
            if (!job.Production && !job.Haul) foreach(var d in new[]{new Cell(1,1),new Cell(1,-1),new Cell(-1,1),new Cell(-1,-1)})
                yield return job.Cell+d+new Cell(0,0,dy);
            if (dy == -2) yield return job.Cell + new Cell(0, 0, dy);
        }
    }
    private bool IntersectsWorker(Cell c, BuildKind kind, DemoWorker worker)
    {
        // Include both ends of a movement segment, so finishing construction cannot clip a walker.
        if(kind==BuildKind.Ladder)return false;
        if(worker.Path.TryPeek(out var step)&&ClimbEdge(worker.Tile,step))
        {
            var low=worker.Tile.Level<step.Level?worker.Tile:step;
            if(c.X==low.X&&c.Z==low.Z&&c.Level<Math.Max(worker.Tile.Level,step.Level)+WorldScale.DwarfHeightMeters&&c.Level+(TallFixture(kind)?2:1)>low.Level)return true;
        }
        bool At(Cell w) => w.X == c.X && w.Z == c.Z && c.Level < NodeHeight(w) + WorldScale.DwarfHeightMeters && c.Level + (TallFixture(kind) ? 2 : 1) > NodeHeight(w);
        return At(worker.Tile) || (worker.Path.TryPeek(out var next) && At(next));
    }

    public Queue<Cell>? FindPath(Cell start, Cell end) => Navigation.FindPath(start, end);

    private void Complete(DemoJob job, DemoWorker worker)
    {
        using var timing = Probe?.Measure("jobs.complete") ?? default;
        if(job.Refuel){ConsumeMaterials(job);FireFuel[job.Cell]=FireFuel.GetValueOrDefault(job.Cell)+300;Revision++;}
        else if(job.Forestry)CompleteForestry(job);
        else if(job.Farming)CompleteFarm(job);
        else if(job.Dig)
        {
            if(job.Demolish)DamageBlock(job.Cell,double.MaxValue);
            else for(int y=0;y<job.DigHeight;y++)MineWithDrop(job.Cell+new Cell(0,0,y));

            Revision++;
        }
        else if(job.Processing)CompleteProcessing(job,worker);
        else if(job.Cook)
        {
            ConsumeMaterials(job);var recipe=JobRecipe(job);SpawnItem(recipe.Output,recipe.Count,worker.Tile,destinationZone:Bill(job)?.DestinationZone??0);Log($"{worker.Name}做好了 2 份口粮。");
        }
        else if(job.Smelt)
        {
            ConsumeMaterials(job);var recipe=JobRecipe(job);SpawnItem(recipe.Output,recipe.Count,worker.Tile,destinationZone:Bill(job)?.DestinationZone??0);
            Log($"{worker.Name}熔炼出了 2 铁锭。");
        }
        else if (job.Forge)
        {
            ConsumeMaterials(job);var recipe=JobRecipe(job);SpawnItem(recipe.Output,recipe.Count,worker.Tile,destinationZone:Bill(job)?.DestinationZone??0);
            Log($"{worker.Name}完成了一把铁剑！这间工坊开始运转了。");
        }
        else
        {
            if(job.Placing&&!PlacementReady(job))return;
            ConsumeMaterials(job);
            DisplaceItems(job.Cell,worker.Tile,TallFixture(job.WorkKind)?2:1);
            if(job.Placing)Terrain.Place(job.Cell,job.PlaceBlock,Content);
            else Buildings[job.Cell] = new Building(job.Cell, job.Build!.Value, job.Facing){DefinitionId=job.BuildDefinitionId};
            Revision++;
        }
        AwardWork(worker,JobSkill(job),job.Dig?20*job.DigHeight:job.Smelt?80:job.Forge?100:20);
        if(job.Forge&&Workers.Contains(worker))worker.Person.Remember("first_sword","亲手锻造出了铁剑。",_simulationTime);
        if(job.Production)CompleteProductionOrder(job);
        Release(job); Jobs.Remove(job);
        ValidateWorkshop();
    }

    private void Release(DemoJob job)
    {
        if (job.Worker >= 0)
        {
            var worker = Workers[job.Worker];
            DropCargo(job,worker.Tile);
            worker.Job = null; StopMovement(worker); RefreshWeight(worker); worker.Status = "等待工作";
        }
        job.Worker = -1;
    }

    public void ValidateWorkshop()
    {
        using var timing = Probe?.Measure("rooms.validate-workshop") ?? default;
        bool wasReady = WorkshopReady;
        HasFurnace = Buildings.Values.Any(b => b.Kind == BuildKind.Furnace);
        HasAnvil = Buildings.Values.Any(b => b.Kind == BuildKind.Anvil);
        HasRoom = false; _forgeAnvil = null; _forgeFurnace = null;
        // Every eligible room must touch a door. Cached regions include a dependency
        // footprint so edits to their walls, support or headroom invalidate them.
        var checkedRooms = new HashSet<RoomRegion>();
        foreach (var door in Buildings.Values.Where(b => b.Kind == BuildKind.Door))
        foreach (var d in Directions)
        {
            var start = door.Cell + d;
            if (!Inside(start) || NaturalSolid(start + new Cell(0,0,1)) || Boundary(start) ||
                !Solid(start + new Cell(0,0,-1))) continue;
            var region = FindRoomRegion(start);
            if (!region.Outside && region.Cells.Count >= 4) checkedRooms.Add(region);
        }
        // Preserve the original building-seed ordering when several workshops exist.
        int SeedOrder(RoomRegion region)
        {
            int index = 0;
            foreach (var cell in Buildings.Keys) foreach (var d in Directions)
            { if (region.Cells.Contains(cell + d)) return index; index++; }
            return int.MaxValue;
        }
        foreach (var region in checkedRooms.OrderBy(SeedOrder))
        {
            HasRoom = true;
            var furnace = Buildings.Values.FirstOrDefault(b => b.Kind == BuildKind.Furnace && region.Cells.Contains(b.Cell));
            var anvil = Buildings.Values.FirstOrDefault(b => b.Kind == BuildKind.Anvil && region.Cells.Contains(b.Cell));
            if (furnace is null || anvil is null) continue;
            if (!Directions.Any(d => region.Cells.Contains(anvil.Cell + d) && Walkable(anvil.Cell + d))) continue;
            if (!Directions.Any(d => region.Cells.Contains(furnace.Cell + d) && Walkable(furnace.Cell + d))) continue;
            _forgeAnvil = anvil.Cell; _forgeFurnace = furnace.Cell;
        }
        WorkshopIssue = WorkshopReady ? "工坊就绪，可以下达铁剑订单。" :
            !HasRoom ? "需要用墙和门围出房间，内部至少 4 格。" :
            !HasFurnace ? "房间里还需要一座熔炉。" :
            !HasAnvil ? "房间里还需要一座铁砧。" : "炉与砧需要在同一房间，并留出操作位置。";
        if (!wasReady && WorkshopReady) Log("工坊就绪！现在可以锻造第一把铁剑。");
        if (_forgeAnvil is not null)
            foreach (var j in Jobs.Where(j => j.Production && !j.Cook && !j.Processing && j.Worker < 0))
            {
                var destination=(j.Smelt||j.Cook?_forgeFurnace:_forgeAnvil)!.Value;
                if(j.Cell!=destination)foreach(var r in j.Materials.Where(r=>r.Stage==2))r.Stage=0;
                j.Cell=destination;
            }
    }

    private bool Boundary(Cell c) => NaturalSolid(c) || Buildings.TryGetValue(c, out var b) && (BlockCatalog.IsWall(b.Kind) || b.Kind == BuildKind.Door || b.Kind==BuildKind.Floodgate&&!GateOpen(c));
    public event Action<string>? Logged;
    public void Log(string message)
    {
        Logged?.Invoke(message);
        Events.Insert(0, message);
        if (Events.Count > 5) Events.RemoveAt(Events.Count - 1);
    }
}
