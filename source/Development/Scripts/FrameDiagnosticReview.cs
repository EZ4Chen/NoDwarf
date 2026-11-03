using System.Diagnostics;
using System.Text.Json;
using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private FrameDiagnostic? _frameDiagnostic;

    // Opt-in diagnostic only. Render signals delimit CPU submission, not GPU completion.
    private sealed class FrameDiagnostic
    {
        public sealed class Frame
        {
            public int index, scriptThread, preDrawThread, postDrawThread;
            public bool measuring;
            public double atMs, reviewClockMs, deltaMs, startIntervalMs, beforeProcessGapMs, previousPostDrawGapMs;
            public double callbackMs, reviewBookkeepingMs, scriptEndToPreDrawMs, renderSignalSpanMs;
            public double rendererCpuMs, rendererGpuMs, rendererSetupMs, physicsMonitorMs;
            public double drawCalls, objects, primitives, nodes;
            public int gc0, gc1, gc2;
            public long allocatedBytes;
            public bool renderComplete;
            [System.Text.Json.Serialization.JsonIgnore] public long Start, End, PreDraw, PostDraw;
        }
        public readonly Stopwatch Wall = Stopwatch.StartNew();
        public readonly List<Frame> Frames = new();
        public Frame? Current;
        public string Name = "";
        public string SceneStateHash = "";
        public bool Orbit;
        public bool ReadRenderTimes;
        public bool RequireRenderWarmup;
        public bool ResumeSimulation;
        public bool FixedInitialState;
        public int RenderedFrames;
        public double FirstDrawAtMs;
        private long _allocated;
        private int _gc0, _gc1, _gc2;
        public void Begin(double delta, bool measuring)
        {
            long now = Stopwatch.GetTimestamp();
            long allocated = GC.GetTotalAllocatedBytes(false);
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var f = new Frame { index = Frames.Count, atMs = Wall.Elapsed.TotalMilliseconds, deltaMs = delta * 1000,
                measuring = measuring, scriptThread = System.Environment.CurrentManagedThreadId, Start = now, allocatedBytes = Current is null ? 0 : allocated - _allocated,
                gc0 = g0 - _gc0, gc1 = g1 - _gc1, gc2 = g2 - _gc2 };
            if (Current is { } previous)
            {
                f.startIntervalMs = Milliseconds(previous.Start, now);
                f.beforeProcessGapMs = Milliseconds(previous.End, now);
                if (previous.PostDraw != 0) f.previousPostDrawGapMs = Milliseconds(previous.PostDraw, now);
            }
            _allocated = allocated; _gc0 = g0; _gc1 = g1; _gc2 = g2;
            Current = f; Frames.Add(f);
        }
        public void End()
        {
            if (Current is not { } f) return;
            f.End = Stopwatch.GetTimestamp(); f.callbackMs = Milliseconds(f.Start, f.End);
        }
        public static double Milliseconds(long a, long b) => (b-a)*1000d/Stopwatch.Frequency;
    }

    private void StartFrameDiagnostic(string[] args)
    {
        if (!args.Contains("--frame-diagnostic")) return;
        bool interior = args.Contains("--diag-interior"), orbit = args.Contains("--diag-orbit");
        bool uncapped = args.Contains("--diag-uncapped"), noVsync = args.Contains("--diag-no-vsync");
        _frameDiagnostic = new FrameDiagnostic { Orbit = orbit, ReadRenderTimes = !args.Contains("--diag-no-gpu-timing"),
            Name = $"{(interior ? "interior" : "cutaway")}-{(orbit ? "orbit" : "fixed")}-{(uncapped ? "unlimited" : "cap60")}-{(noVsync ? "vsyncoff" : "vsyncon")}{(args.Contains("--diag-no-gpu-timing") ? "-no-gpu-timing" : "")}{(args.Contains("--diag-hide-plans") ? "-hide-plans" : "")}{(args.Contains("--diag-hide-terrain") ? "-hide-terrain" : "")}" };
        _interiorView = interior;
        if(args.Contains("--diag-hide-forest"))_frameDiagnostic.Name+="-no-forest";
        _cameraYaw = 0;
        if (args.Contains("--diag-static") || args.Contains("--diag-fixed-workload"))
        {
            _frameDiagnostic.FixedInitialState=true;
            _frameDiagnostic.ResumeSimulation=args.Contains("--diag-fixed-workload");
            _frameDiagnostic.Name += _frameDiagnostic.ResumeSimulation?"-fixed-workload":"-static";
            // Identical simulation state in every render-only A/B run.
            for (int i = 0; i < 5*SimulationClock.BaseTicksPerSecond; i++)
            {
                _tpsReview!.Scenario.BeforeTick(i);
                _world.Update(SimulationClock.TickSeconds);
            }
            _clock.Paused = true;
            if(_frameDiagnostic.ResumeSimulation)_tpsReview!.Ticks=5*SimulationClock.BaseTicksPerSecond;
        }
        if (args.Contains("--diag-render-warmup"))
        {
            _frameDiagnostic.Name += "-render-warmup";
            _frameDiagnostic.RequireRenderWarmup = true;
        }
        if (args.Contains("--diag-quiet")) _frameDiagnostic.Name += "-quiet";
        if (args.Contains("--diag-basement"))
        {
            _frameDiagnostic.Name += "-basement";
            _layerOffset = -3; _focus = WorldPosition(new NoDwarf.Simulation.Cell(80,105,-3));
            PositionCamera();
        }
        if (args.Contains("--diag-no-shadows"))
        {
            _frameDiagnostic.Name += "-no-shadows";
            _sun.ShadowEnabled = false;
        }
        if (uncapped) Engine.MaxFps = 0;
        if (noVsync) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        if (_frameDiagnostic.ReadRenderTimes) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        RenderingServer.FramePreDraw += DiagnosticPreDraw;
        RenderingServer.FramePostDraw += DiagnosticPostDraw;
        SyncWorld();
        if (args.Contains("--diag-hide-plans")) _ghosts.Visible = false;
        if (args.Contains("--diag-hide-terrain")) _terrainView.Visible = false;
        _frameDiagnostic.SceneStateHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_world.SaveJson())));
    }
    private void BeginFrameDiagnostic(double delta)
    {
        if (_frameDiagnostic is not { } d) return;
        if(d.FixedInitialState)_clock.Paused=!d.ResumeSimulation||_tpsReview?.Measuring!=true;
        d.Begin(delta, _tpsReview?.Measuring == true);
        d.Current!.reviewClockMs = _tpsReview?.Wall.Elapsed.TotalMilliseconds ?? 0;
        if (d.Orbit)
        {
            _cameraYaw = Mathf.DegToRad((float)(d.Wall.Elapsed.TotalSeconds * 30 % 360));
            PositionCamera(); _revision = -1;
        }
    }
    private void DiagnosticPreDraw()
    {
        if (_frameDiagnostic?.Current is not { } f) return;
        f.PreDraw = Stopwatch.GetTimestamp();
        f.preDrawThread = System.Environment.CurrentManagedThreadId;
        if (f.End != 0) f.scriptEndToPreDrawMs = FrameDiagnostic.Milliseconds(f.End, f.PreDraw);
    }
    private void DiagnosticPostDraw()
    {
        if (_frameDiagnostic is not { Current: { } f } d) return;
        f.PostDraw = Stopwatch.GetTimestamp();
        f.postDrawThread = System.Environment.CurrentManagedThreadId;
        if (f.PreDraw != 0) f.renderSignalSpanMs = FrameDiagnostic.Milliseconds(f.PreDraw, f.PostDraw);
        if (d.ReadRenderTimes)
        {
            var rid = GetViewport().GetViewportRid();
            f.rendererCpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
            f.rendererGpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
            f.rendererSetupMs = RenderingServer.GetFrameSetupTimeCpu();
        }
        f.physicsMonitorMs = Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimePhysicsProcess) * 1000;
        f.drawCalls = Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalDrawCallsInFrame);
        f.objects = Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalObjectsInFrame);
        f.primitives = Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalPrimitivesInFrame);
        f.nodes = Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectNodeCount);
        f.renderComplete = true;
        if (d.RenderedFrames++ == 0) d.FirstDrawAtMs = d.Wall.Elapsed.TotalMilliseconds;
    }
    private void SaveFrameDiagnostic()
    {
        if (_frameDiagnostic is not { } d) return;
        string path = DiagnosticTrace.OutputPath(ProjectSettings.GlobalizePath($"res://.local/frame-diagnostic-{d.Name}.json"));
        var result = new { d.Name, d.SceneStateHash, maxFps = Engine.MaxFps, vsync = DisplayServer.WindowGetVsyncMode().ToString(),
            renderTimingEnabled = d.ReadRenderTimes, viewportSize = GetViewport().GetVisibleRect().Size.ToString(),
            windowSize = GetWindow().Size.ToString(), cameraSize = _camera.Size, shadows = _sun.ShadowEnabled,
            note = "Includes warmup. Frame timestamps use monotonic time. Renderer/physics monitor values may lag a frame; zero render timing can mean unavailable. Render signals do not fence GPU completion.",
            warmupCpuReadMs = _tpsReview?.CpuReadAtStartMs,
            d.FirstDrawAtMs, d.RenderedFrames, d.RequireRenderWarmup,
            terrainPipeline = new { synchronous = _terrainView.SynchronousMeshing, pending = _terrainView.PendingMeshCount,
                applied = _terrainView.AppliedMeshCount, discarded = _terrainView.DiscardedMeshCount, workers = _terrainView.MeshWorkerThreads.Order().ToArray(),
                cacheHits = _terrainView.SliceCacheHits, cachedSlices = _terrainView.CachedSliceCount, estimatedCacheBytes = _terrainView.CachedSliceBytes },
            interiorCache = new { hits = _interiorCacheHits, builds = _interiorOcclusionBuilds, topologyBuilds = _roomTopologyBuilds,
                cachedViews = _interiorCuts.Count, cachedCapEntries = _cachedInteriorCaps, cachedFloors = _interiorTopologies.Count,
                background = _backgroundInterior, pending = InteriorPending, applied = _interiorApplied, discarded = _interiorDiscarded,
                workerComputeMs = _interiorWorkerMs, workers = _interiorWorkerThreads.Order().ToArray() },
            frames = d.Frames.Where(f=>f.End != 0).ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        RenderingServer.FramePreDraw -= DiagnosticPreDraw;
        RenderingServer.FramePostDraw -= DiagnosticPostDraw;
        GD.Print($"FRAME_DIAGNOSTIC_OK: {path}");
    }
}
