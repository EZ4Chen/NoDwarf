using System.Diagnostics;
using NoDwarf.Simulation;

// Kept independent of WorldNavigation so this same workload also runs against
// the pre-change simulation snapshot when comparing end-to-end assignment cost.
internal static class GameplayWorkload
{
    public static void Run()
    {
        Console.WriteLine("gameplay | residents | simulation ms (6 seconds at 30 TPS) | max tick ms | assigned | allocation MB");
        foreach (int count in new[] { 10, 100, 300 })
            foreach (bool dynamic in new[] { false, true })
            {
                var totals = new List<double>(); var peaks = new List<double>(); var allocations = new List<double>();
                int assigned = 0;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    var world = new DemoWorld(flat: true);
                    world.Workers.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        world.Workers.Add(new DemoWorker { Name = $"worker{i}", X = 2 + i % 10, Z = 2 + i / 10 });
                        if (!world.QueueBuild(new Cell(55 + i % 20 * 2, 3 + i / 20 * 2), BuildKind.Wall))
                            throw new Exception("Gameplay benchmark could not queue task");
                    }
                    if (dynamic)
                        for (int z = 0; z < DemoWorld.Depth; z++)
                        { var c = new Cell(47, z); world.Buildings[c] = new Building(c, BuildKind.Wall); }
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    double total = 0, peak = 0;
                    for (int tick = 0; tick < 180; tick++)
                    {
                        long begin = Stopwatch.GetTimestamp();
                        if (dynamic && tick == 30) world.Erase(new Cell(47, 30));
                        if (dynamic && tick == 120)
                            world.DeveloperPlace(new Cell(47, 30), BuildKind.Wall);
                        world.Update(1.0 / 30);
                        double elapsed = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
                        total += elapsed; peak = Math.Max(peak, elapsed);
                    }
                    totals.Add(total); peaks.Add(peak);
                    allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocated) / 1048576.0);
                    assigned = world.Workers.Count(w => w.Job is not null);
                    if (!dynamic && assigned != count) throw new Exception("Workload did not assign all residents");
                }
                Console.WriteLine($"{(dynamic ? "open-close-gate" : "independent-work")} | {count} | {totals.Order().ElementAt(1):F2} | {peaks.Order().ElementAt(1):F2} | {assigned} | {allocations.Order().ElementAt(1):F2}");
            }
    }
}
