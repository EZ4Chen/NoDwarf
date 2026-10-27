using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
namespace NoDwarf;

public partial class TerrainView
{
    internal void CheckReference(DemoWorld world,WorldView view,(int,int) key)
    {
        var input=CaptureMeshInput(world,view);
        var result=Task.Run(()=>TerrainMeshBuilder.Build(input,key)).GetAwaiter().GetResult();
        if(result.WorkerThread==System.Environment.CurrentManagedThreadId)throw new Exception("Mesh calculation did not leave main thread");
        var reference=TerrainMeshReference.Build(world,view,key);
        try
        {
            var meshes=reference.GetChildren().OfType<MeshInstance3D>().Select(n=>(ArrayMesh)n.Mesh).ToArray();
            var expected=meshes.LastOrDefault();
            if(result.Surfaces.Count==0){if(expected is not null&&expected.GetSurfaceCount()>0)throw new Exception("Missing terrain mesh");return;}
            if(expected is null||expected.GetSurfaceCount()!=result.Surfaces.Count)throw new Exception("Surface count differs");
            int surface=0;
            void Equal(Vector3[] a,Vector3[] b,string what)
            {
                if(a.Length!=b.Length)throw new Exception(what+" array length differs");
                for(int i=0;i<a.Length;i++)if(a[i].DistanceTo(b[i])>(what=="normals"?.0001:.00001))throw new Exception($"{what} differs at {i}: {a[i]} != {b[i]}");
            }
            foreach(var actual in result.Surfaces.Values)
            {
                using var arrays=expected.SurfaceGetArrays(surface++);
                Equal(actual.Vertices,arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),"vertices");
                Equal(actual.Normals,arrays[(int)Mesh.ArrayType.Normal].AsVector3Array(),"normals");
                var uv=arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                if(!actual.Uvs.SequenceEqual(uv))throw new Exception("UVs differ");
            }
            Equal(result.Collision,expected.GetFaces(),"collision");
            if(result.Edges.Vertices.Length>0)
            {
                using var arrays=meshes[0].SurfaceGetArrays(0);
                Equal(result.Edges.Vertices,arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array(),"contact edges");
            }
            // A captured snapshot must remain stable after live terrain and view edits.
            var c=world.Surface(key.Item1+2,key.Item2+2)+new Cell(0,0,-1);
            world.Terrain.Excavate(c);
            if(view.WallCaps is Dictionary<Cell,float> caps)caps[c]=c.Level;
            var again=Task.Run(()=>TerrainMeshBuilder.Build(input,key)).GetAwaiter().GetResult();
            Equal(result.Collision,again.Collision,"snapshot changed after edit");
        }
        finally{reference.Free();}
    }
    internal void CheckAppliedState(DemoWorld world,WorldView view)
    {
        if(PendingMeshCount!=0)throw new Exception("Mesh queue did not drain");
        var snapshot=CaptureMeshInput(world,view);
        foreach(var entry in _chunks)
        {
            var expected=TerrainMeshBuilder.Build(snapshot,entry.Key);
            var body=entry.Value.GetChildren().OfType<StaticBody3D>().SingleOrDefault();
            var actual=body?.GetChild<CollisionShape3D>(0).Shape as ConcavePolygonShape3D;
            if(expected.Collision.Length==0){if(actual is not null)throw new Exception("Unexpected collision");continue;}
            if(actual is null||body!.CollisionLayer==0||!actual.GetFaces().SequenceEqual(expected.Collision))throw new Exception("Stale or missing chunk collision: "+entry.Key);
            if(!entry.Value.Visible)throw new Exception("Final chunk stayed hidden");
        }
    }
}
public partial class Main
{
    private async void CheckTerrainPipeline()
    {
        try
        {
            _clock.Paused=true;
            var scenario=new RealtimeStressScenario();
            var fixture=scenario.World;
            _terrainView.CheckReference(fixture,new WorldView(null),(80,96));
            _terrainView.CheckReference(fixture,new WorldView(-3),(80,96));
            _terrainView.CheckReference(fixture,new WorldView(-3,false,new Dictionary<Cell,float>{{new(69,95,-3),-2.8f}},1),(64,80));
            var regional=new DemoWorld(74219,region:"boreal");
            _terrainView.CheckReference(regional,new WorldView(null),(64,64));
            _terrainView.CheckReference(regional,new WorldView(70,true),(64,64));
            _terrainView.CheckReference(regional,new WorldView(-64),(0,0));
            _world=fixture;_overview.World=_world;CreatePeople();_heightSelected=true;_layerOffset=-3;_foundation=null;
            _cutaway=true;_interiorView=true;_focus=WorldPosition(new(80,105,-3));_camera.Size=32;PositionCamera();
            _terrainView.SynchronousMeshing=false;SyncWorld();
            int appliedBefore=_terrainView.AppliedMeshCount;
            for(int i=0;i<20;i++)
            {
                _cameraYaw=i*.17f;PositionCamera();_revision=-1;SyncWorld();
                _world.Terrain.Excavate(new Cell(68,96+i%10,-3));
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            if(_terrainView.AppliedMeshCount==appliedBefore)throw new Exception("Continuous edits starved all mesh submission");
            // Replace the map with jobs still queued, then change slice and reveal policy.
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;CreatePeople();
            _heightSelected=false;_layerOffset=0;_interiorView=false;_revealUnknown=false;_cutaway=false;SyncWorld();
            _heightSelected=true;_layerOffset=-3;_revealUnknown=true;_cutaway=true;SyncWorld();
            _layerOffset=0;_revealUnknown=false;SyncWorld();
            var deadline=Stopwatch.StartNew();
            while(_terrainView.PendingMeshCount>0)
            {
                if(deadline.Elapsed.TotalSeconds>30)throw new Exception("Terrain queue timed out");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            _terrainView.CheckAppliedState(_world,CurrentView);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            _focus=WorldPosition(_world.Camp);_cameraYaw=0;_camera.Size=32;PositionCamera();
            if(Pick(_camera.UnprojectPosition(WorldPosition(_world.Camp)))!=_world.Camp)throw new Exception("Async terrain ray picking failed");
            if(_terrainView.MeshWorkerThreads.Count==0||_terrainView.MeshWorkerThreads.Contains(System.Environment.CurrentManagedThreadId))throw new Exception("Async mesh thread identity invalid");
            // Teardown while workers still run must not reference freed scene objects.
            var temporary=new TerrainView();AddChild(temporary);temporary.Generate(regional,new WorldView(null));RemoveChild(temporary);temporary.Free();
            if(DisplayServer.GetName()!="headless")
            {
                _world=fixture;_overview.World=_world;CreatePeople();_heightSelected=true;_layerOffset=-3;
                _interiorView=true;_cutaway=true;_focus=WorldPosition(new(80,105,-3));_cameraYaw=Mathf.DegToRad(45);_camera.Size=32;PositionCamera();SyncWorld();
                deadline.Restart();
                while(_terrainView.PendingMeshCount>0)
                {
                    if(deadline.Elapsed.TotalSeconds>30)throw new Exception("Preview terrain timed out");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                var floor=new Cell(78,106,-3);
                if(Pick(_camera.UnprojectPosition(WorldPosition(floor)),1)!=floor)throw new Exception("Async interior floor picking failed");
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/terrain-pipeline-preview.png"))!=Error.Ok)throw new Exception("Terrain preview save failed");
            }
            GD.Print($"TERRAIN_PIPELINE_OK: original mesh/UV/normal/collision parity; immutable snapshots; edits, rotation, world/slice replacement, picking, teardown; workers={string.Join(',',_terrainView.MeshWorkerThreads)} discarded={_terrainView.DiscardedMeshCount}");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
