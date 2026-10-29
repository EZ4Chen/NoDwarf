using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Text.Json;
namespace NoDwarf;

public partial class Main
{
    private async void ReviewInteriorThreads()
    {
        try
        {
            _clock.Paused=true;_depthReviewActive=true;SetProcess(false);_terrainView.FlushMeshes();
            var rows=new List<object>();
            foreach(string scene in new[]{"room1600","outdoor65025","room1600-terrain-edits"})
            {
                bool outside=scene=="outdoor65025";
                if(!outside)SetupInteriorFixture(40,40);
                else
                {
                    _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);
                    var c=new Cell(127,127);_world.Buildings[c]=new(c,BuildKind.Wall);
                    _foundation=new FoundationPlan(c,c);_layerOffset=0;_interiorView=true;_cutaway=true;
                }
                for(int i=0;i<12;i++)
                {
                    _cameraYaw=i*.17f;_world.Navigation.Invalidate(new(127,127,ActiveLevel));
                    if(scene=="room1600-terrain-edits")_world.Terrain.Excavate(new Cell(60+i,60,-3));
                    long start=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread();
                    PrepareInteriorWalls();
                    double mainMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    long snapshotBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    while(InteriorPending)
                    {
                        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                        long pump=Stopwatch.GetTimestamp();PumpInteriorWork();mainMs+=Stopwatch.GetElapsedTime(pump).TotalMilliseconds;
                        if(Stopwatch.GetElapsedTime(start).TotalSeconds>20)throw new Exception("Interior worker timed out");
                    }
                    rows.Add(new{scene,step=i,mainMs,readyMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                        snapshotBytes,caps=_wallCaps?.Count??0,signature=CapSignature(_wallCaps),background=_backgroundInterior,
                        visited=_interiorTopologies[(ActiveLevel,false)].Data.Visits});
                    if(outside&&_interiorTopologies[(ActiveLevel,false)].Data.Visits>=TerrainMap.Size*TerrainMap.Size/10)
                        throw new Exception("Exterior search did not stop at a connected escape witness");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            string label=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--thread-label="))?.Split('=',2)[1]??"current";
            if(label.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new Exception("Invalid label");
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://.local/interior-thread-{label}.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
            if(OS.GetCmdlineUserArgs().Contains("--thread-checks"))await CheckInteriorThreads();
            GD.Print($"INTERIOR_THREAD_OK: {label}; workers={string.Join(',',_interiorWorkerThreads)} applied={_interiorApplied} discarded={_interiorDiscarded}");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private async Task CheckInteriorThreads()
    {
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        async Task Drain()
        {
            var timer=Stopwatch.StartNew();
            while(InteriorPending){PumpInteriorWork();if(timer.Elapsed.TotalSeconds>20)throw new Exception("Async interior timeout");if(InteriorPending)await Frame();}
        }
        void CheckReference(IReadOnlyList<Cell> room)
        {
            var expected=TraceInteriorReference(_world,new[]{room.ToList()},ActiveLevel,_foundation!.FloorHeight,CameraBack,_cameraPitch);
            Require(CapSignature(_wallCaps)==CapSignature(expected),"Async cut differs from current reference");
        }
        _backgroundInterior=true;
        var room=SetupInteriorFixture(40,40);_cameraYaw=0;PrepareInteriorWalls();await Drain();CheckReference(room);
        Require(_interiorWorkerThreads.Count>0&&!_interiorWorkerThreads.Contains(System.Environment.CurrentManagedThreadId),"Occlusion stayed on the main thread");
        int builds=_interiorOcclusionBuilds;
        _cameraYaw=Mathf.Pi;PrepareInteriorWalls();await Drain();
        _cameraYaw=0;PrepareInteriorWalls();
        Require(!InteriorPending&&_interiorOcclusionBuilds==builds+1,"Cached pose queued new work");CheckReference(room);
        // Complete an old result without pumping it, then invalidate its snapshot.
        _world.Navigation.Invalidate(room[0]);PrepareInteriorWalls();
        var old=_interiorWork!;var timer=Stopwatch.StartNew();
        while(!old.Task.IsCompleted){if(timer.Elapsed.TotalSeconds>20)throw new Exception("Old worker timeout");await Frame();}
        var obstruction=new Cell(110,110,-3);_world.Buildings[obstruction]=new(obstruction,BuildKind.Wall);_world.Navigation.Invalidate(obstruction);
        int discarded=_interiorDiscarded;PrepareInteriorWalls();
        Require(_wallCaps is null,"Terrain/version change retained stale cut geometry");
        await Drain();Require(_interiorDiscarded>discarded,"Superseded result was not discarded");
        CheckReference(room.Where(c=>c!=obstruction).ToArray());
        _world.Buildings.Remove(obstruction);_world.Navigation.Invalidate(obstruction);
        // Camera requests merge into one running task and one latest pending request.
        int topologyBuilds=_roomTopologyBuilds;
        for(int i=0;i<40;i++){_cameraYaw=i*.13f;PrepareInteriorWalls();PumpInteriorWork();await Frame();}
        PrepareInteriorWalls();await Drain();CheckReference(room);
        Require(_roomTopologyBuilds-topologyBuilds==1,"Camera motion repeated whole-room search");
        _layerOffset=-3;PrepareInteriorWalls();Require(_wallCaps is null,"Changed floor reused the previous cut");await Drain();
        Require(_wallCaps!.Count==0,"Wrong floor was published");
        _layerOffset=0;PrepareInteriorWalls();await Drain();CheckReference(room);
        _cameraYaw=.435f;PrepareInteriorWalls();_interiorView=false;PrepareInteriorWalls();await Drain();
        Require(_wallCaps is null,"Disabled cutaway was restored by a late worker");
        // Re-enabling before the cancelled task is collected must queue a replacement.
        _interiorView=true;_world.Navigation.Invalidate(room[0]);PrepareInteriorWalls();
        _interiorView=false;PrepareInteriorWalls();
        _interiorView=true;PrepareInteriorWalls();await Drain();CheckReference(room);
        _interiorView=true;_world.Navigation.Invalidate(room[0]);PrepareInteriorWalls();
        room=SetupInteriorFixture(3,3);PrepareInteriorWalls();await Drain();CheckReference(room);
        // Full normal-frame path: completion must trigger SyncWorld even while paused.
        room=SetupInteriorFixture(40,40);_overview.World=_world;_camera.Size=58;_orbitPitch=Mathf.DegToRad(35);
        _focus=WorldPosition(new(110,110,-3));_terrainView.Visible=true;
        SetProcess(true);
        _cameraYaw=0;_viewQuadrant=0;PositionCamera();SyncWorld();
        _cameraYaw=.049f;PositionCamera();timer.Restart(); // Same 5-degree bucket, no explicit SyncWorld.
        while(InteriorPending||_terrainView.PendingMeshCount>0||_revision!=_world.Revision)
        {if(timer.Elapsed.TotalSeconds>30)throw new Exception("Sub-bucket camera update was lost");await Frame();}
        CheckReference(room);
        foreach(float yaw in new[]{0f,45f,90f,180f,270f})
        {
            _cameraYaw=Mathf.DegToRad(yaw);PositionCamera();SyncWorld();timer.Restart();
            while(InteriorPending||_terrainView.PendingMeshCount>0||_revision!=_world.Revision)
            {if(timer.Elapsed.TotalSeconds>30)throw new Exception("Paused view did not publish background result");await Frame();}
            CheckReference(room);CheckBuildingShapes();await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var front=new Cell(110+(int)(CameraBack.X*17),110+(int)(CameraBack.Z*17),-3);
            Require(Pick(_camera.UnprojectPosition(WorldPosition(front)),1)==front,"Async front-wall picking failed");
        }
        SetProcess(false);
        Require(_backgroundInterior,"Worker failure silently selected the synchronous fallback");
        if(DisplayServer.GetName()!="headless")
        {
            RefreshUi();RenderingServer.ForceDraw();
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/interior-background-preview.png"))!=Error.Ok)
                throw new Exception("Could not save background-interior preview");
        }
        // Return/exit with work in flight; workers must not touch the freed scene.
        _world.Navigation.Invalidate(room[0]);PrepareInteriorWalls();int applied=_interiorApplied;
        StopInteriorWork();await Frame();await Frame();PumpInteriorWork();
        Require(_interiorApplied==applied&&!InteriorPending,"Stopped pipeline still published a result");
        GD.Print("INTERIOR_ASYNC_CHECKS_OK: snapshot isolation, coalescing, stale edits/world/floor/mode, cached fast path, paused publication, picking and teardown");
    }
}
