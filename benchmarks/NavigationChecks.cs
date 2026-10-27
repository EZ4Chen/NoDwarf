using NoDwarf.Simulation;

internal static class NavigationChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static void Valid(DemoWorld world, Cell start, Cell end, Queue<Cell>? path)
    {
        Check(path is not null, "Expected reachable path");
        foreach (var cell in path!)
        { Check(world.Connected(start, cell), $"Invalid navigation edge {start} -> {cell}"); start = cell; }
        Check(start == end, "Path ended at wrong destination");
    }

    public static void Run()
    {
        var world = new DemoWorld(flat: true);
        var start = new Cell(2, 40); var end = new Cell(85, 40);
        Valid(world, start, end, world.FindPath(start, end));
        long built = world.Navigation.RebuiltChunks, version = world.Navigation.Version;
        Check(world.QueueBuild(new Cell(80, 80), BuildKind.Wall), "Queue distant plan");
        world.FindPath(start, end);
        Check(world.Navigation.RebuiltChunks == built && world.Navigation.Version == version, "Plans must not invalidate geometry");
        world.Erase(new Cell(80, 80));
        Check(world.Navigation.Version == version, "Cancelling a plan must not invalidate geometry");

        var remote = new Cell(82, 82);
        world.Buildings[remote] = new Building(remote, BuildKind.Wall);
        Valid(world, start, end, world.FindPath(start, end));
        Check(world.Navigation.RebuiltChunks - built == 1, "Interior edit only rebuilds one chunk");
        built = world.Navigation.RebuiltChunks;
        var corner = new Cell(7, 7);
        world.Buildings[corner] = new Building(corner, BuildKind.Wall);
        world.FindPath(start, end);
        Check(world.Navigation.RebuiltChunks - built == 4, "Corner edit invalidates adjacent chunk dependencies");

        for (int z = 0; z < DemoWorld.Depth; z++)
        { var c = new Cell(47, z); world.Buildings[c] = new Building(c, BuildKind.Wall); }
        Check(world.FindPath(start, end) is null, "Barrier splits global connectivity");
        long expanded = world.Navigation.ExpandedNodes;
        Check(world.Navigation.FindNearest(start, new[] { end }, out _) is null, "Unreachable work rejected");
        Check(world.Navigation.ExpandedNodes == expanded, "Unreachable work must not flood the voxel map");
        world.Buildings.Remove(new Cell(47, 40));
        Valid(world, start, end, world.FindPath(start, end));
        Valid(world, start, end, world.Navigation.FindNearest(start, new[] { end }, out _));
        world.Buildings[new Cell(47, 40, 1)] = new Building(new Cell(47, 40, 1), BuildKind.Wall);
        Check(world.FindPath(start, end) is null, "Headroom change closes the gate below");
        world.Buildings.Remove(new Cell(47, 40, 1));
        Check(world.Terrain.Excavate(new Cell(47, 40, -1)), "Excavate support directly");
        Check(world.FindPath(start, end) is null, "Direct terrain edits invalidate support and connectivity");
        world.Buildings[new Cell(47, 40, -1)] = new Building(new Cell(47, 40, -1), BuildKind.Wall);
        Valid(world, start, end, world.FindPath(start, end));
        world.Buildings.Clear();
        Valid(world, start, end, world.FindPath(start, end));

        ReplanningChecks();
        ReplanningBudgetChecks();
        AssignmentChecks();
        DifferentialChecks();
    }

    private static void ReplanningChecks()
    {
        var world = new DemoWorld(flat: true);
        var worker = world.Workers[0]; worker.X = 10; worker.Z = 20;
        var end = new Cell(35, 20);
        foreach (var step in world.FindPath(worker.Tile, end)!) worker.Path.Enqueue(step);
        world.Update(1.0 / 30);
        long requests = world.Navigation.Searches;
        var remote = new Cell(80, 80);
        world.Buildings[remote] = new Building(remote, BuildKind.Wall);
        world.Update(1.0 / 30);
        Check(world.Navigation.Searches == requests, "Remote edits preserve existing paths");
        var block = worker.Path.ElementAt(5);
        world.Buildings[block] = new Building(block, BuildKind.Wall);
        world.Update(1.0 / 30);
        Check(world.Navigation.Searches == requests + 1, "Replan before walking into a changed route");
        Valid(world, worker.Tile, end, worker.Path);
        for (int i = 0; i < 600; i++) world.Update(1.0 / 30);
        Check(worker.Tile == end && worker.Path.Count == 0, "Manual movement reaches goal after reroute");
    }

    private static void AssignmentChecks()
    {
        var world = new DemoWorld(flat: true);
        for (int z = 0; z < DemoWorld.Depth; z++)
        { var c = new Cell(47, z); world.Buildings[c] = new Building(c, BuildKind.Wall); }
        Check(world.QueueBuild(new Cell(80, 40), BuildKind.Wall), "Unreachable task planned");
        for (int i = 0; i < 30; i++) world.Update(1.0 / 30);
        Check(world.Workers.All(w => w.Job is null), "No job through sealed barrier");
        world.Buildings.Remove(new Cell(47, 40));
        for (int i = 0; i < 30; i++) world.Update(1.0 / 30);
        Check(world.Workers.Any(w => w.Job is not null), "New opening wakes previously unreachable work");

        var many = new DemoWorld(flat: true) { NavigationRequestsPerTick = 3 };
        many.Workers.Clear();
        // Each job needs a distinct pickup stand now that materials are physically
        // hauled. A shared depot tests contention, not search-budget fairness.
        many.Items.Clear(); many.Stockpiles.Clear();
        for (int i = 0; i < 40; i++)
        {
            many.Workers.Add(new DemoWorker { Name = $"worker{i}", X = 2, Z = 2 + i });
            many.SpawnItem("cobblestone", 2, new Cell(59 + i / 20 * 3, 2 + i % 20 * 3));
            Check(many.QueueBuild(new Cell(60 + i / 20 * 3, 2 + i % 20 * 3), BuildKind.Wall), "Many jobs planned");
        }
        for (int i = 0; i < 60; i++)
        {
            long before = many.Navigation.Searches;
            many.Update(1.0 / 30);
            Check(many.Navigation.Searches - before <= 3, "Per-tick search request limit");
        }
        Check(many.Workers.All(w => w.Job is not null), "Search budget does not starve later residents");
    }

    private static void ReplanningBudgetChecks()
    {
        var world = new DemoWorld(flat: true) { NavigationRequestsPerTick = 2 };
        world.Workers.Clear();
        for (int i = 0; i < 100; i++)
        {
            var worker = new DemoWorker { Name = $"budget{i}", X = 2, Z = 10 + i % 20 };
            world.Workers.Add(worker);
            if (i < 20)
                foreach (var cell in world.FindPath(worker.Tile, new Cell(85, worker.Tile.Z))!) worker.Path.Enqueue(cell);
        }
        Check(world.QueueBuild(new Cell(80, 60), BuildKind.Wall), "Idle residents have a task to search for");
        for (int z = 0; z < DemoWorld.Depth; z++)
        { var c = new Cell(47, z); world.Buildings[c] = new Building(c, BuildKind.Wall); }
        for (int i = 0; i < 12; i++) world.Update(1.0 / 30);
        Check(world.Workers.All(w => w.Path.Count == 0), "Idle work searches must not starve broken-route replanning");
        Check(world.Workers.All(w => w.X == 2), "No movement along a pending invalid route");
    }

    private static void DifferentialChecks()
    {
        var random = new Random(20261026);
        foreach (int seed in new[] { 74219, 12345, 42 })
        {
            var world = new DemoWorld(seed);
            var cells = new List<Cell>();
            for (int x = 0; x < DemoWorld.Width; x++)
                for (int z = 0; z < DemoWorld.Depth; z++)
                    if (world.Walkable(world.Surface(x, z))) cells.Add(world.Surface(x, z));
            for (int i = 0; i < 100; i++)
            {
                var start = cells[random.Next(cells.Count)]; var end = cells[random.Next(cells.Count)];
                var reference = NavigationBenchmarks.LegacyPath(world, start, end);
                var result = world.Navigation.FindHierarchicalPath(start, end);
                Check((reference is null) == (result is null), "Hierarchical reachability differs from full A*");
                if (result is not null) Valid(world, start, end, result);
                var targets = new[] { end, cells[random.Next(cells.Count)], cells[random.Next(cells.Count)] };
                var nearest = world.Navigation.FindNearest(start, targets, out var endpoint);
                var expected = NavigationBenchmarks.LegacyNearest(world, start, targets);
                Check((nearest is null) == (expected is null), "Nearest work reachability changed");
                if (nearest is not null)
                {
                    Valid(world, start, endpoint, nearest);
                    Check(nearest.Count == expected!.Count, "Work assignment no longer chooses nearest reachable stand");
                }
                if (i % 10 == 0)
                {
                    var changed = cells[random.Next(cells.Count)];
                    world.Buildings[changed] = new Building(changed, BuildKind.Wall);
                }
            }
        }
    }
}
