using System.Diagnostics;
using System.Text.Json;
using NoDwarf.Simulation;

internal static class RealtimeSimulationBenchmark
{
    public static void Run(bool profile = false)
    {
        DiagnosticTrace.Initialize();
        try
        {
        var scenario = new RealtimeStressScenario();
        for (int i = 0; i < 5*SimulationClock.BaseTicksPerSecond; i++)
        { scenario.BeforeTick(i); scenario.World.Update(SimulationClock.TickSeconds); if (i % SimulationClock.BaseTicksPerSecond == 0) scenario.Sample(); }
        if (profile) scenario.World.Probe = new PerformanceProbe();
        long allocatedStart=GC.GetTotalAllocatedBytes(true);
        var gcStart=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
        var times = new List<double>();
        DiagnosticTrace.Mark("measurement","start");
        for (int i = 5*SimulationClock.BaseTicksPerSecond; i < 65*SimulationClock.BaseTicksPerSecond; i++)
        {
            using var trace=DiagnosticTrace.Span("simulation.tick");
            long start = Stopwatch.GetTimestamp();
            scenario.BeforeTick(i); scenario.World.Update(SimulationClock.TickSeconds);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if (i % SimulationClock.BaseTicksPerSecond == 0) scenario.Sample();
        }
        DiagnosticTrace.Mark("measurement","end");
        long allocatedBytes=GC.GetTotalAllocatedBytes(true)-allocatedStart;
        var collections=Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gcStart[g]).ToArray();
        string stateHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scenario.World.SaveJson())));
        var sorted = times.Order().ToArray();
        double Quantile(double q) => sorted[(int)Math.Ceiling(sorted.Length * q) - 1];
        var result = new { mode = "simulation-only, no frame pacing or scene synchronization", residents = 100,
            mapSize = DemoWorld.Width, ticks = times.Count, gameSeconds = times.Count / (double)SimulationClock.BaseTicksPerSecond, allocatedBytes, collections, stateHash,
            totalComputeSeconds = times.Sum() / 1000, meanTickMs = times.Average(), p50 = Quantile(.5), p95 = Quantile(.95), p99 = Quantile(.99), max = times.Max(),
            ticksOverBaseBudget = times.Count(v => v > 1000d / SimulationClock.BaseTicksPerSecond), ticksOver3xBudget = times.Count(v => v > 1000d / (3*SimulationClock.BaseTicksPerSecond)),
            scenario.GateChanges, scenario.CompletedConstruction, scenario.LevelTransitions, profiling = scenario.World.Probe?.Report(),
            averageBusy = scenario.BusyResidentSum / scenario.Samples, averageMoving = scenario.MovingResidentSum / scenario.Samples };
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if(Environment.GetEnvironmentVariable("NODWARF_PERF_OUTPUT_DIR") is not null)
            File.WriteAllText(DiagnosticTrace.OutputPath("simulation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        DiagnosticTrace.FinishCaptureAsync().GetAwaiter().GetResult();
        }
        finally{DiagnosticTrace.Save();}
    }
}
