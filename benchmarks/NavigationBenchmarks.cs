using System.Diagnostics;
using NoDwarf.Simulation;

internal static class NavigationBenchmarks
{
    // Independent live-geometry A* and Dijkstra reference kernels.
    // They query live geometry, exactly as the original implementation did.
    internal static Queue<Cell>? LegacyPath(DemoWorld world, Cell start, Cell end)
    {
        if (!world.Walkable(start) || !world.Walkable(end)) return null;
        var frontier = new PriorityQueue<Cell, int>(); frontier.Enqueue(start, 0);
        var previous = new Dictionary<Cell, Cell> { [start] = start };
        var costs = new Dictionary<Cell, int> { [start] = 0 };
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (current == end) return Trace(previous, start, current);
            foreach (var next in world.Neighbors(current))
            {
                if (!world.Walkable(next) || !world.Connected(current, next)) continue;
                int cost = costs[current] + world.TraversalCost(current,next);
                if (costs.TryGetValue(next, out int old) && old <= cost) continue;
                previous[next] = current; costs[next] = cost;
                frontier.Enqueue(next, cost + Math.Abs(next.X - end.X) + Math.Abs(next.Z - end.Z));
            }
        }
        return null;
    }

    internal static Queue<Cell>? LegacyNearest(DemoWorld world, Cell start, IEnumerable<Cell> candidates)
    {
        if (!world.Walkable(start)) return null;
        var targets = candidates.ToHashSet();
        var previous = new Dictionary<Cell, Cell> { [start] = start };
        var costs=new Dictionary<Cell,int>{{start,0}};
        var queue=new PriorityQueue<Cell,int>();queue.Enqueue(start,0);
        while(queue.TryDequeue(out var current,out var cost))
        {
            if(cost!=costs[current])continue;
            if(targets.Contains(current))return Trace(previous,start,current);
            foreach(var next in world.Neighbors(current))
            {
                if(!world.Connected(current,next))continue;
                int nextCost=cost+world.TraversalCost(current,next);
                if(costs.TryGetValue(next,out var old)&&old<=nextCost)continue;
                costs[next]=nextCost;previous[next]=current;queue.Enqueue(next,nextCost);
            }
        }
        return null;
    }

    private static Queue<Cell> Trace(Dictionary<Cell, Cell> previous, Cell start, Cell end)
    {
        var path = new List<Cell>();
        while (end != start) { path.Add(end); end = previous[end]; }
        path.Reverse(); return new Queue<Cell>(path);
    }

    public static void Run()
    {
        Console.WriteLine($"Map {DemoWorld.Width}x{DemoWorld.Depth}, minimum level {TerrainMap.MinLevel}; Release; 100 queries/case; median of 3 runs; milliseconds. Cold graph preparation listed separately.");
        Console.WriteLine("case | legacy | cached exact | hierarchical | cold build | rebuilt chunks | mean/max path cost ratio");
        Case("flat-independent", () => new DemoWorld(flat: true));
        Case("terrain-independent", () => new DemoWorld(74219));
        Case("sealed-wall", Barrier);
        Case("edit-every-query", () => new DemoWorld(flat: true), editEvery: 1);
        Case("edit-every-10-queries", () => new DemoWorld(flat: true), editEvery: 10);
        NearestCase();
        GameplayWorkload.Run();
    }

    private static DemoWorld Barrier()
    {
        var world = new DemoWorld(flat: true);
        for (int z = 0; z < DemoWorld.Depth; z++)
        { for(int y=0;y<2;y++){var c = new Cell(DemoWorld.Width / 2, z,y); world.Buildings[c] = new Building(c, BuildKind.Wall);} }
        return world;
    }

    private static (Cell Start, Cell End)[] Queries(DemoWorld world)
    {
        var random = new Random(29017);
        var queries = new List<(Cell, Cell)>();
        while (queries.Count < 100)
        {
            var start = world.Surface(random.Next(2, DemoWorld.Width / 3), random.Next(2, DemoWorld.Depth - 2));
            var end = world.Surface(random.Next(DemoWorld.Width * 2 / 3, DemoWorld.Width - 2), random.Next(2, DemoWorld.Depth - 2));
            if (world.Walkable(start) && world.Walkable(end)) queries.Add((start, end));
        }
        return queries.ToArray();
    }

    private static void Case(string name, Func<DemoWorld> create, int editEvery = 0)
    {
        var queries = Queries(create());
        var times = new double[3, 3]; var cold = new List<double>();
        int[][] lengths = new int[3][];
        long rebuilt = 0;
        for (int repeat = 0; repeat < 3; repeat++)
            for (int mode = 0; mode < 3; mode++)
            {
                var world = create();
                var watch = Stopwatch.StartNew();
                if (mode != 0) world.FindPath(queries[0].Start, queries[0].End);
                if (mode == 2) cold.Add(watch.Elapsed.TotalMilliseconds);
                long before = world.Navigation.RebuiltChunks;
                lengths[mode] = new int[queries.Length];
                watch.Restart();
                for (int i = 0; i < queries.Length; i++)
                {
                    if (editEvery > 0 && i % editEvery == 0)
                    {
                        var changed = new Cell(DemoWorld.Width / 2 / 8 * 8 + 2, DemoWorld.Depth / 2 / 8 * 8 + 2);
                        if (i / editEvery % 2 == 0) world.Buildings[changed] = new Building(changed, BuildKind.Wall);
                        else world.Buildings.Remove(changed);
                    }
                    var q = queries[i];
                    var path = mode switch
                    {
                        0 => LegacyPath(world, q.Start, q.End),
                        1 => world.Navigation.FindExactPath(q.Start, q.End),
                        _ => world.Navigation.FindHierarchicalPath(q.Start, q.End)
                    };
                    int cost=0;var previous=q.Start;
                    if(path is not null)foreach(var step in path){cost+=world.TraversalCost(previous,step);previous=step;}
                    lengths[mode][i] = path is null?-1:cost;
                }
                times[mode, repeat] = watch.Elapsed.TotalMilliseconds;
                if (mode == 2) rebuilt = world.Navigation.RebuiltChunks - before;
            }
        var ratios = new List<double>();
        for (int i = 0; i < queries.Length; i++)
        {
            if (lengths[0][i] != lengths[1][i]) throw new Exception("Exact cached A* changed traversal cost");
            if ((lengths[0][i] < 0) != (lengths[2][i] < 0)) throw new Exception("Hierarchy changed reachability");
            if (lengths[0][i] > 0) ratios.Add((double)lengths[2][i] / lengths[0][i]);
        }
        double Median(int mode) => Enumerable.Range(0, 3).Select(r => times[mode, r]).Order().ElementAt(1);
        Console.WriteLine($"{name} | {Median(0):F2} | {Median(1):F2} | {Median(2):F2} | {cold.Order().ElementAt(1):F2} | {rebuilt} | {(ratios.Count == 0 ? "n/a" : $"{ratios.Average():F3}/{ratios.Max():F3}")}");
    }

    private static void NearestCase()
    {
        foreach (bool blocked in new[] { false, true })
        {
            var targets = new[] { new Cell(DemoWorld.Width - 20, 20), new Cell(DemoWorld.Width - 10, DemoWorld.Depth / 2), new Cell(DemoWorld.Width - 20, DemoWorld.Depth - 20) };
            var times = new double[2, 3];
            for (int repeat = 0; repeat < 3; repeat++)
                for (int mode = 0; mode < 2; mode++)
                {
                    var world = blocked ? Barrier() : new DemoWorld(flat: true);
                    if (mode == 1) world.Navigation.FindNearest(new Cell(5, 5), targets, out _);
                    var watch = Stopwatch.StartNew();
                    for (int i = 0; i < 100; i++)
                    {
                        var start = new Cell(2 + i % 10, 2 + i % 90);
                        if (mode == 0) LegacyNearest(world, start, targets);
                        else world.Navigation.FindNearest(start, targets, out _);
                    }
                    times[mode, repeat] = watch.Elapsed.TotalMilliseconds;
                }
            double Median(int mode) => Enumerable.Range(0, 3).Select(r => times[mode, r]).Order().ElementAt(1);
            Console.WriteLine($"nearest-work-{(blocked ? "blocked" : "reachable")} | {Median(0):F2} | {Median(1):F2} | weighted Dijkstra; shared graph (legacy per-worker failure cache excluded)");
        }
    }
}
