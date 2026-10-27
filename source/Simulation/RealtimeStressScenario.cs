namespace NoDwarf.Simulation;

// Deterministic benchmark fixture only. Never used by normal world generation.
// Keeps real work/material/needs/navigation rules; setup edits are outside timing.
public sealed class RealtimeStressScenario
{
    public DemoWorld World { get; private set; } = new(flat: true);
    private Cell _offset;
    private Cell P(Cell c)=>c+_offset;
    public Cell ViewCenter => P(new(102, 105, 0));
    public int GateChanges { get; private set; }
    public int QueuedConstruction { get; private set; }
    public int CompletedConstruction { get; private set; }
    public int LevelTransitions { get; private set; }
    public double BusyResidentSum { get; private set; }
    public double MovingResidentSum { get; private set; }
    public int Samples { get; private set; }
    public int PeakJobs { get; private set; }
    private readonly HashSet<int> _tracked = new();
    private readonly Dictionary<string, int> _levels = new();
    private readonly Queue<Cell> _buildSites = new();
    private Cell _gate = new(100, 100);

    public RealtimeStressScenario()
    {
        var w = World;
        w.Workers.Clear(); w.Items.Clear(); w.Stockpiles.Clear();
        // Underground room and two separate stair connections to the ground.
        for (int x = 70; x < 90; x++)
            for (int z = 95; z < 115; z++)
                for (int y = -3; y <= -2; y++) w.Terrain.Excavate(new Cell(x, z, y));
        foreach (int z in new[] { 100, 110 })
        {
            for (int i = 0; i < 3; i++)
            {
                var c = new Cell(90 + i, z, -3 + i);
                for (int h = 0; h < 3; h++) w.Terrain.Excavate(c + new Cell(0, 0, h));
                Put(c + new Cell(0, 0, -1), BuildKind.Cobblestone);
                Put(c, BuildKind.CobbleStairs);
            }
        }
        // Upper floor and two staircases. No teleporting between floors.
        for (int x = 112; x < 132; x++)
            for (int z = 95; z < 115; z++) Put(new Cell(x, z, 2), BuildKind.Planks);
        foreach (int z in new[] { 100, 110 })
            for (int i = 0; i < 3; i++)
            {
                var c = new Cell(109 + i, z, i);
                for (int h = 0; h < i; h++) Put(new Cell(c.X, c.Z, h), BuildKind.Planks);
                Put(c, BuildKind.PlankStairs);
            }
        // A long divider has two openings; closing one forces a real detour.
        for (int z = 80; z < 140; z++)
            if (z is not (100 or 110)) Put(new Cell(100, z), BuildKind.Wall);
        for (int i = 0; i < 100; i++)
        {
            var c = i < 30 ? new Cell(74 + i % 10, 97 + i / 10, -3)
                : i < 60 ? new Cell(115 + i % 10, 105 + (i - 30) / 10, 3)
                : new Cell(94 + i % 5, 102 + (i - 60) / 5);
            var worker = new DemoWorker
            {
                Id = $"stress-{i}", Name = $"居民{i + 1}", X = c.X, Z = c.Z, Level = c.Level,
                Person = ResidentProfile.Create(i),
                Appearance = CharacterAppearance.Randomize(new Random(7619 + i))
            };
            // Spread need updates across the population; normal rules remain active.
            worker.Person.Hunger = 45 + i % 50;
            worker.Person.Rest = 60 + i % 35;
            w.Workers.Add(worker); _levels[worker.Id] = c.Level;
        }
        foreach (var depot in new[] { new Cell(85, 104, -3), new Cell(97, 95), new Cell(120, 98, 3) })
        {
            w.SetStockpile(depot, depot + new Cell(2, 1));
            w.SpawnItem("cobblestone", 1200, depot);
            w.SpawnItem("wood", 600, depot + new Cell(1, 0));
            w.SpawnItem("meal", 200, depot + new Cell(2, 0));
        }
        for (int i = 0; i < 40; i++)
            w.SpawnItem(i % 2 == 0 ? "raw_iron" : "coal", 5,
                i < 20 ? new Cell(73 + i % 10, 111 + i / 10, -3) : new Cell(102 + i % 5, 115 + i / 5));
        for (int z = 120; z < 139; z += 2)
            for (int x = 84; x < 132; x += 3)
                if (x != 100) _buildSites.Enqueue(new Cell(x, z));
        for (int z = 96; z < 114; z += 3)
            for (int x = 114; x < 131; x += 3) _buildSites.Enqueue(new Cell(x, z, 3));
        AddBuilds(80);
        for (int x = 69; x >= 66; x--)
            for (int z = 95; z < 115; z++) Track(w.QueueDig(new Cell(x, z, -3), announce: false, height: 2));
        foreach (var worker in w.Workers)
            if (!w.Walkable(worker.Tile)) throw new InvalidOperationException("Stress resident spawned outside walkable space");
        if (w.FindPath(new Cell(85, 104, -3), new Cell(120, 104, 3)) is null)
            throw new InvalidOperationException("Stress floors must connect via real stairs");
        Sample();
    }

    public void AddCurrentEnvironment()
    {
        var previous=World;var next=new DemoWorld(region:"temperate");_offset=new(25,22,next.Camp.Level);
        // Preserve the identical 100-person workload, transplant it into a regional map.
        // Flatten only the test yard; surrounding region trees/grass remain rendered.
        foreach(var tree in next.Trees.Values.Where(t=>t.Cell.X>=90&&t.Cell.X<166&&t.Cell.Z>=100&&t.Cell.Z<165).ToArray())
        {next.Trees.Remove(tree.Cell);next.Terrain.SetTree(tree.Cell,false);}
        for(int x=65;x<141;x++)for(int z=78;z<143;z++)
        {
            var p=P(new(x,z));for(int y=p.Level;y<=next.Terrain[p.X,p.Z].Level;y++)next.Terrain.Excavate(new(p.X,p.Z,y));
            var floor=p+new Cell(0,0,-1);if(!next.Solid(floor))next.Buildings[floor]=new(floor,BuildKind.Cobblestone);
        }
        foreach(var cell in previous.Terrain.Excavated)next.Terrain.Excavate(P(cell));
        foreach(var b in previous.Buildings.Values)next.Buildings[P(b.Cell)]=b with {Cell=P(b.Cell)};
        next.Workers.Clear();foreach(var worker in previous.Workers){worker.X+=_offset.X;worker.Z+=_offset.Z;worker.Level+=_offset.Level;next.Workers.Add(worker);_levels[worker.Id]=worker.Level;}
        next.Items.Clear();foreach(var item in previous.Items){item.Cell=P(item.Cell);next.Items.Add(item);}
        next.Jobs.Clear();foreach(var job in previous.Jobs){job.Cell=P(job.Cell);next.Jobs.Add(job);}
        next.Stockpiles.Clear();foreach(var cell in previous.Stockpiles)next.Stockpiles.Add(P(cell));
        next.StorageZones.Clear();foreach(var zone in previous.StorageZones){zone.Cells=zone.Cells.Select(P).ToHashSet();next.StorageZones.Add(zone);}
        var sites=_buildSites.Select(P).ToArray();_buildSites.Clear();foreach(var cell in sites)_buildSites.Enqueue(cell);_gate=P(_gate);
        var basin=new Cell(147,94,_offset.Level-2);
        for(int x=0;x<6;x++)for(int z=0;z<6;z++)for(int y=0;y<3;y++)next.Terrain.Excavate(basin+new Cell(x,z,y));
        next.Fluids.AddSource(new Aquifer{Cell=basin+new Cell(-1,3),Capacity=80000,Stored=80000,Head=basin.Level+3,Rate=500},true);
        next.SetWeather("rain");World=DemoWorld.LoadJson(next.SaveJson());
        foreach(var worker in World.Workers)if(!World.Walkable(worker.Tile))throw new InvalidOperationException("Regional benchmark spawn blocked");
    }

    private void Put(Cell c, BuildKind kind) => World.Buildings[c] = new Building(c, kind, 0);
    private void Track(bool queued)
    {
        if (!queued) return;
        _tracked.Add(World.Jobs.Last().Id); QueuedConstruction++;
    }
    private void AddBuilds(int count)
    {
        for (int i = 0; i < count && _buildSites.TryDequeue(out var cell); i++)
            Track(World.QueueBuild(cell, BuildKind.Wall, announce: false));
    }

    public void BeforeTick(long tick)
    {
        if (tick > 0 && tick % 150 == 0)
        {
            bool changed = World.Buildings.ContainsKey(_gate)
                ? World.DeveloperErase(_gate) : World.DeveloperPlace(_gate, BuildKind.Wall);
            if (changed) GateChanges++;
            if (World.Jobs.Count(j => j.Construction) < 160) AddBuilds(12);
        }
    }
    public void Sample()
    {
        Samples++;
        BusyResidentSum += World.Workers.Count(w => w.Job is not null || w.Person.Activity is not null);
        MovingResidentSum += World.Workers.Count(w => w.Path.Count > 0);
        PeakJobs = Math.Max(PeakJobs, World.Jobs.Count);
        var alive = World.Jobs.Select(j => j.Id).ToHashSet();
        foreach (int id in _tracked.Where(id => !alive.Contains(id)).ToArray())
        { _tracked.Remove(id); CompletedConstruction++; }
        foreach (var worker in World.Workers)
        {
            if (_levels[worker.Id] != worker.Level) LevelTransitions++;
            _levels[worker.Id] = worker.Level;
        }
    }
}
