using System.Diagnostics;
using System.Text.Json;
using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private TpsReview? _tpsReview;

    private sealed class TpsReview
    {
        public readonly RealtimeStressScenario Scenario = new();
        public readonly Stopwatch Wall = new();
        public readonly List<double> TickMs = new(), FrameMs = new(), SyncMs = new(), WindowTps = new();
        public readonly List<double> ProcessMs = new(), UiMs = new(), OutsideProcessMs = new();
        public readonly List<object> Windows = new();
        public int Frames, Ticks, LastTicks, LastFrames;
        public double LastWindow, LastFrame;
        public bool Measuring;
        public int Speed;
        public double Duration;
        public long AllocatedAtStart, NavigationAtStart, TerrainAtStart;
        public int[] GcStart = new int[3];
        public double PeakLag;
        public double ClockInputSeconds;
        public bool Profiling;
        public double CpuSecondsAtStart;
        public double CpuReadAtStartMs;
        public bool Quiet;
        public bool CurrentEnvironment;
        public bool Canopy;
        public double PeakResidentMB;
        public readonly List<double> PrintMs = new();
    }

    private void StartTpsReview()
    {
        var args = OS.GetCmdlineUserArgs();
        int speed = args.Contains("--tps-3x") ? 3 : 1;
        _tpsReview = new TpsReview { Speed = speed, Duration = args.Contains("--tps-quick") ? 10 : args.Contains("--diag-long") ? 60 : args.Contains("--frame-diagnostic") ? 20 : 60, Profiling = args.Contains("--tps-profile") };
        _tpsReview.CurrentEnvironment=args.Contains("--tps-environment");
        _tpsReview.Canopy=args.Contains("--tps-canopy");
        if(_tpsReview.CurrentEnvironment)_tpsReview.Scenario.AddCurrentEnvironment();
        _tpsReview.Quiet = args.Contains("--diag-quiet");
        _world = _tpsReview.Scenario.World;
        _clock = new SimulationClock(); _clock.SetSpeed(speed);
        _developerMode = false; _creative = false; _selectedWorker = -1;
        _overview.World = _world; _overview.RefreshMap();
        _foundation = new FoundationPlan(_tpsReview.Scenario.ViewCenter+new Cell(-32,-25),_tpsReview.Scenario.ViewCenter+new Cell(30,35));
        _layerOffset = 0; _placement = 0; _cutaway = true; _interiorView = false;
        _focus = WorldPosition(_tpsReview.Scenario.ViewCenter); _camera.Size = 66; _cameraYaw = 0;
        if(_tpsReview.Canopy){_cutaway=false;_camera.Size=95;}
        CreatePeople(); PositionCamera(); DrawFoundation(); SyncWorld(); RefreshUi();
        Engine.MaxFps = 60;
        StartFrameDiagnostic(args);
        if(_tpsReview.CurrentEnvironment&&_frameDiagnostic is not null)_frameDiagnostic.Name+=$"-environment-{speed}x";
        if(_tpsReview.Canopy&&_frameDiagnostic is not null)_frameDiagnostic.Name+="-canopy";
        GetWindow().Title = $"NoDwarf · 100 人实时性能测试 · {speed}× · 自动结束";
        _diagnosticsPanel.Show();
        if (_tpsReview.Profiling) _world.Probe = new PerformanceProbe();
        _tpsReview.Wall.Start();
        GD.Print($"TPS_REVIEW_START: {speed}x; 100 residents; {DemoWorld.Width}x{DemoWorld.Depth}; 3 floors; construction/mining/hauling/needs; 5-second warmup, {_tpsReview.Duration}-second measurement");
    }

    private void ReviewTick()
    {
        using var tickTrace=DiagnosticTrace.Span("simulation.tick");
        var review = _tpsReview!;
        long begin = Stopwatch.GetTimestamp();
        review.Scenario.BeforeTick(review.Ticks);
        _world.Update(1d / SimulationClock.BaseTicksPerSecond);
        double ms = Stopwatch.GetElapsedTime(begin).TotalMilliseconds;
        review.Ticks++;
        if (review.Measuring) review.TickMs.Add(ms);
        if (review.Ticks % 30 == 0) review.Scenario.Sample();
    }

    private void FinishTpsFrame(double syncMs, double uiMs, double processMs, double delta)
    {
        var r = _tpsReview!;
        double elapsed = r.Wall.Elapsed.TotalSeconds;
        r.Frames++;
        bool renderWarmed = _frameDiagnostic is not { RequireRenderWarmup: true } diagnostic ||
            (diagnostic.RenderedFrames >= 60 && diagnostic.Wall.Elapsed.TotalMilliseconds - diagnostic.FirstDrawAtMs >= 5000);
        if (!r.Measuring && elapsed >= 5 && renderWarmed)
        {
            if(_frameDiagnostic is {ResumeSimulation:true})_clock.Paused=false;
            r.Measuring = true; r.Wall.Restart(); elapsed = 0;
            r.LastWindow = 0; r.LastFrame = 0; r.LastTicks = r.Ticks; r.LastFrames = r.Frames;
            r.AllocatedAtStart = GC.GetTotalAllocatedBytes();
            long cpuReadStarted = Stopwatch.GetTimestamp();
            r.CpuSecondsAtStart = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
            r.CpuReadAtStartMs = Stopwatch.GetElapsedTime(cpuReadStarted).TotalMilliseconds;
            GD.Print($"TPS_CPU_SAMPLE_MS: {r.CpuReadAtStartMs:F3}");
            r.NavigationAtStart = _world.Navigation.Searches; r.TerrainAtStart = _world.Terrain.Revision;
            for (int g = 0; g < 3; g++) r.GcStart[g] = GC.CollectionCount(g);
            GD.Print("TPS_REVIEW_MEASURING");
            DiagnosticTrace.Mark("measurement","start");
            return;
        }
        if (!r.Measuring) return;
        r.FrameMs.Add((elapsed - r.LastFrame) * 1000); r.LastFrame = elapsed;
        r.SyncMs.Add(syncMs);
        r.UiMs.Add(uiMs); r.ProcessMs.Add(processMs);
        r.OutsideProcessMs.Add(Math.Max(0, r.FrameMs[^1] - processMs));
        r.ClockInputSeconds += delta;
        // Clock.PendingSeconds excludes wall time spent after Advance in this frame.
        double lag = Math.Max(0, elapsed * r.Speed - r.TickMs.Count / 30d);
        r.PeakLag = Math.Max(r.PeakLag, lag);
        if (elapsed - r.LastWindow >= 1)
        {
            using(var process=Process.GetCurrentProcess()){process.Refresh();r.PeakResidentMB=Math.Max(r.PeakResidentMB,process.WorkingSet64/1048576d);}
            double seconds = elapsed - r.LastWindow;
            double tps = (r.Ticks - r.LastTicks) / seconds, fps = (r.Frames - r.LastFrames) / seconds;
            r.WindowTps.Add(tps);
            r.Windows.Add(new { atSeconds = elapsed, tps, fps, lagGameSeconds = lag,
                active = _world.Workers.Count(w => w.Job is not null), moving = _world.Workers.Count(w => w.Path.Count > 0), jobs = _world.Jobs.Count });
            if (!r.Quiet)
            {
                long printStarted = Stopwatch.GetTimestamp();
                GD.Print($"TPS_WINDOW: wall={elapsed:F1}s TPS={tps:F1}/{30 * r.Speed} FPS={fps:F1} lag={lag:F2}s active={_world.Workers.Count(w => w.Job is not null)} jobs={_world.Jobs.Count}");
                r.PrintMs.Add(Stopwatch.GetElapsedTime(printStarted).TotalMilliseconds);
            }
            r.LastWindow = elapsed; r.LastTicks = r.Ticks; r.LastFrames = r.Frames;
        }
        if (elapsed < r.Duration) return;
        DiagnosticTrace.Mark("measurement","end");
        var s = r.Scenario;
        object Stats(List<double> values) => new { count = values.Count, mean = values.Count == 0 ? 0 : values.Average(),
            p50 = Quantile(values, .5), p95 = Quantile(values, .95), p99 = Quantile(values, .99), max = values.Count == 0 ? 0 : values.Max() };
        var result = new
        {
            mode = DisplayServer.GetName(), speed = r.Speed, targetTps = 30 * r.Speed,
            mapSize = DemoWorld.Width, residents = _world.Workers.Count, currentEnvironment=r.CurrentEnvironment,
            canopy=r.Canopy,
            trees=_world.Trees.Count,visibleGrass=_terrainView.VisibleCoverInstances,weather=_world.Weather.Id,waterCells=_world.Fluids.Cells.Count,peakResidentMB=r.PeakResidentMB,managedMB=GC.GetTotalMemory(false)/1048576d,
            wallSeconds = elapsed, gameSeconds = r.TickMs.Count / 30d,
            achievedTps = r.TickMs.Count / elapsed, observedFps = r.FrameMs.Count / elapsed,
            peakLagGameSeconds = r.PeakLag, endLagGameSeconds = lag,
            tickMilliseconds = Stats(r.TickMs), frameMilliseconds = Stats(r.FrameMs), syncMilliseconds = Stats(r.SyncMs),
            processMilliseconds = Stats(r.ProcessMs), uiMilliseconds = Stats(r.UiMs), outsideProcessMilliseconds = Stats(r.OutsideProcessMs),
            clockInputSeconds = r.ClockInputSeconds, clockBacklogGameSeconds = _clock.PendingSeconds,
            cpuReadAtStartMs = r.CpuReadAtStartMs,
            consolePrintMilliseconds = Stats(r.PrintMs),
            profilingWholeRunIncludingWarmup = _world.Probe?.Report(),
            logicalProcessors = System.Environment.ProcessorCount,
            processCpuCoreEquivalent = (Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds - r.CpuSecondsAtStart) / elapsed,
            ticksOver33ms = r.TickMs.Count(v => v > 1000d / 30), ticksOver11ms = r.TickMs.Count(v => v > 1000d / 90),
            oneSecondTps = Stats(r.WindowTps),
            allocationMB = (GC.GetTotalAllocatedBytes() - r.AllocatedAtStart) / 1048576d,
            collections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - r.GcStart[g]).ToArray(),
            searches = _world.Navigation.Searches - r.NavigationAtStart, excavatedDuringMeasurement = _world.Terrain.Revision - r.TerrainAtStart,
            fullRun = new { s.GateChanges, s.QueuedConstruction, s.CompletedConstruction, s.LevelTransitions, s.PeakJobs,
                averageBusy = s.BusyResidentSum / s.Samples, averageMoving = s.MovingResidentSum / s.Samples },
            windows = r.Windows
        };
        string path = ProjectSettings.GlobalizePath(_frameDiagnostic is null
            ? $"res://.local/realtime-tps-{r.Speed}x-{DisplayServer.GetName()}{(r.Profiling ? "-profile" : "")}.json"
            : $"res://.local/frame-tps-{_frameDiagnostic.Name}.json");
        path=DiagnosticTrace.OutputPath(path);
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"TPS_REVIEW_OK: {path}; TPS={r.TickMs.Count / elapsed:F2}/{30 * r.Speed}; FPS={r.FrameMs.Count / elapsed:F2}; tick p99={Quantile(r.TickMs, .99):F2}ms; frame p99={Quantile(r.FrameMs, .99):F2}ms");
        if(r.CurrentEnvironment&&DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(DiagnosticTrace.OutputPath(ProjectSettings.GlobalizePath($"res://.local/environment-perf-{r.Speed}x{(r.Canopy?"-canopy":"")}.png")));
        SaveFrameDiagnostic();
        SetProcess(false);_clock.Paused=true;
        FinishProfileCapture();
    }

    private async void FinishProfileCapture()
    {
        try{await DiagnosticTrace.FinishCaptureAsync();GetTree().Quit();}
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }

    private static double Quantile(List<double> values, double fraction)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
    }
}
