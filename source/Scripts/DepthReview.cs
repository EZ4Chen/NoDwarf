using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Text.Json;
namespace NoDwarf;

public partial class Main
{
    // Frozen camera/clock for reproducible visual and slice-latency comparisons.
    private bool _depthReviewActive;
    private async void RunDepthReview()
    {
        try
        {
            _depthReviewActive=true;_clock.Paused=true;
            Engine.MaxFps=0;
            bool rendered=DisplayServer.GetName()!="headless";
            if(rendered)DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            GetWindow().Title="NoDwarf · 光影与切层对照测试";
            _buildMenu.Hide();_inspector.Hide();_selectionPanel.Hide();_worldMenu.Hide();
            var results=new List<object>();
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Drain()
            {
                var timer=Stopwatch.StartNew();
                while(_terrainView.PendingMeshCount>0){if(timer.Elapsed.TotalSeconds>60)throw new Exception("Depth review terrain timeout");await Frame();}
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            }
            async Task Capture(string name)
            {
                await Drain();for(int i=0;i<12;i++)await Frame();
                if(rendered)
                {
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/depth-{name}.png"));
                    var frames=new List<double>();var timer=Stopwatch.StartNew();double previous=timer.Elapsed.TotalMilliseconds;
                    for(int i=0;i<120;i++){await Frame();double now=timer.Elapsed.TotalMilliseconds;frames.Add(now-previous);previous=now;}
                    frames.Sort();results.Add(new{scene=name,averageMs=frames.Average(),p95Ms=frames[(int)(frames.Count*.95)],p99Ms=frames[(int)(frames.Count*.99)]});
                }
            }
            _world=new DemoWorld(74219,region:"temperate");_overview.World=_world;_terrainRevision=-1;
            _world.QueueBlueprint(_world.Camp,DemoWorld.ExampleBlueprint());_world.DeveloperFinishPlans();
            _foundation=null;_heightSelected=true;_layerOffset=0;_cutaway=true;_interiorView=false;_revealUnknown=false;
            _focus=WorldPosition(_world.Camp+new Cell(2,3));_camera.Size=28;_cameraYaw=0;_topDown=false;_orbitPitch=DefaultCameraPitch;
            CreatePeople();PositionCamera();SyncWorld();RefreshUi();await Capture("outdoor");
            foreach(int direction in new[]{-1,1,-1,1,-1,1})
            {
                var timer=Stopwatch.StartNew();StepLayer(direction);SyncWorld();double callbackMs=timer.Elapsed.TotalMilliseconds;
                var focus=new Vector2(_focus.X+(TerrainMap.Size-1)/2f,_focus.Z+(TerrainMap.Size-1)/2f);
                while(_terrainView.PendingNear(focus,48)>0){if(timer.Elapsed.TotalSeconds>60)throw new Exception("Visible terrain timeout");await Frame();}
                double visibleMs=timer.Elapsed.TotalMilliseconds;
                await Drain();results.Add(new{direction,callbackMs,visibleMs,settledMs=timer.Elapsed.TotalMilliseconds,trees=_terrainView.VisibleTreeCount,bodies=_terrainView.ActiveTreeBodies,cover=_terrainView.VisibleCoverInstances});
            }
            _terrainView.CheckAppliedState(_world,CurrentView);
            // Cached slices must be invalidated by digging or a changed wall contact.
            _world.Terrain.Excavate(_world.Camp+new Cell(-2,0,-1));SyncWorld();await Drain();
            _world.Buildings[_world.Camp+new Cell(6,6)]=new(_world.Camp+new Cell(6,6),BuildKind.Wall);
            foreach(int step in new[]{-1,-1,1,1}){StepLayer(step);SyncWorld();}
            await Drain();_terrainView.CheckAppliedState(_world,CurrentView);
            if(_terrainView.SliceCacheHits==0)throw new Exception("Slice reuse was not exercised");
            var scenario=new RealtimeStressScenario();_world=scenario.World;_world.Jobs.Clear();_overview.World=_world;_terrainRevision=-1;
            _foundation=null;_heightSelected=true;_layerOffset=-3;_cutaway=true;_interiorView=true;
            _focus=WorldPosition(new(80,105,-3));_camera.Size=28;_cameraYaw=0;PositionCamera();CreatePeople();SyncWorld();RefreshUi();await Capture("underground-day");
            _world.Update(300);SyncWorld();RefreshUi();await Capture("underground-night");
            if(_camera.Projection!=Camera3D.ProjectionType.Orthogonal)throw new Exception("Projection changed");
            var floor=new Cell(78,106,-3);
            if(Pick(_camera.UnprojectPosition(WorldPosition(floor)),1)!=floor)throw new Exception("Underground floor picking failed");
            string json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://.local/depth-metrics.json"),json);
            GD.Print("DEPTH_REVIEW_OK: "+json);GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

public partial class TerrainView
{
    internal int VisibleTreeCount=>_trees?.GetChildren().OfType<MultiMeshInstance3D>().Sum(n=>n.Multimesh.VisibleInstanceCount<0?n.Multimesh.InstanceCount:n.Multimesh.VisibleInstanceCount)??0;
    internal int ActiveTreeBodies=>_trees?.GetChildren().OfType<StaticBody3D>().Count(n=>n.CollisionLayer!=0)??0;
    internal int PendingNear(Vector2 focus,float radius)=>_pendingMeshes.Keys.Concat(_meshWorkers.Where(w=>!w.Task.IsCanceled&&Current(w.Request)).Select(w=>w.Request.Key))
        .Count(key=>new Vector2(key.Item1+8,key.Item2+8).DistanceSquaredTo(focus)<radius*radius);
}
