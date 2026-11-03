using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Text.Json;
namespace NoDwarf;

public partial class Main
{
    private async void CheckTerrainCache()
    {
        try
        {
            _clock.Paused=true;_depthReviewActive=true;SetProcess(false);
            _terrainView.FlushMeshes();_terrainView.Visible=false;
            var world=new DemoWorld(flat:true);world.DeveloperResidents(false);
            var terrain=new TerrainView();AddChild(terrain);
            _focus=TerrainView.Point(127,-1,127);_camera.Size=48;PositionCamera();
            bool rendered=DisplayServer.GetName()!="headless";
            var rows=new List<object>();
            async Task Drain()
            {
                var timer=Stopwatch.StartNew();
                while(terrain.PendingMeshCount>0)
                {
                    if(timer.Elapsed.TotalSeconds>60)throw new Exception("Slice cache timeout");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            async Task Measure(string name,WorldView view)
            {
                int applied=terrain.AppliedMeshCount,hits=terrain.SliceCacheHits;
                var timer=Stopwatch.StartNew();terrain.Generate(world,view);
                double callbackMs=timer.Elapsed.TotalMilliseconds;
                await Drain();
                double settledMs=timer.Elapsed.TotalMilliseconds;
                if(rendered)
                {
                    await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                }
                rows.Add(new{name,callbackMs,settledMs,presentedMs=rendered?(double?)timer.Elapsed.TotalMilliseconds:null,
                    rebuilt=terrain.AppliedMeshCount-applied,hits=terrain.SliceCacheHits-hits});
            }
            var a=new WorldView(-1);var b=new WorldView(-2);
            await Measure("cold-a",a);await Measure("cold-b",b);
            for(int i=0;i<3;i++){await Measure("warm-a-"+i,a);await Measure("warm-b-"+i,b);}
            // Include view-local cuts (different global stamps must still reuse equal geometry).
            var caps=new Dictionary<Cell,float>{{new(127,127,-2),-1.8f}};
            await Measure("cut-cold",new(-2,false,caps,1));
            await Measure("cut-away",a);
            await Measure("cut-return",new(-2,false,new Dictionary<Cell,float>(caps),2));
            world.Terrain.Excavate(new Cell(127,127,-2));
            await Measure("edited-current",new(-2,false,caps,2));
            await Measure("edited-other-layer",a);
            terrain.CheckAppliedState(world,a);
            string label=OS.GetCmdlineUserArgs().FirstOrDefault(s=>s.StartsWith("--cache-label="))?.Split('=',2)[1]??"current";
            if(label.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new Exception("Invalid cache label");
            string json=JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true});
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://.local/terrain-cache-{label}.json"),json);
            GD.Print("TERRAIN_CACHE_METRICS: "+json);
            if(OS.GetCmdlineUserArgs().Contains("--cache-assert"))
            {
                terrain.CheckSliceCache(world);
                // Restore the lower slice: inactive higher collision must not intercept the ray.
                terrain.Generate(world,b);terrain.FlushMeshes();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                using var query=PhysicsRayQueryParameters3D.Create(TerrainView.Point(140,3,140),TerrainView.Point(140,-5,140),1);
                var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);
                if(hit.Count==0||Math.Abs(hit["position"].AsVector3().Y+1)>.001f)throw new Exception("Restored slice ray hit inactive geometry");
            }
            RemoveChild(terrain);terrain.Free();
            GD.Print("TERRAIN_CACHE_OK");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

public partial class TerrainView
{
    internal void CheckSliceCache(DemoWorld world)
    {
        void Show(WorldView view){Generate(world,view);FlushMeshes();}
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        var a=new WorldView(-1);var b=new WorldView(-2);
        Show(a);Show(b);Show(a);
        var ids=_chunks.ToDictionary(p=>p.Key,p=>p.Value.GetInstanceId());
        int built=AppliedMeshCount;Show(b);Show(a);
        Require(AppliedMeshCount==built,"Warm slices rebuilt geometry");
        Require(_chunks.All(p=>ids[p.Key]==p.Value.GetInstanceId()),"Warm slice did not reuse original nodes");
        // Four chunks share this edited corner. Other heights retain every unrelated chunk.
        Require(world.Terrain.Excavate(new Cell(63,63,-1)),"Edit fixture failed");
        built=AppliedMeshCount;Show(a);Require(AppliedMeshCount-built==4,"Current slice invalidation was not local");
        built=AppliedMeshCount;Show(b);Require(AppliedMeshCount-built==4,"Cached slice invalidation was not local");
        CheckAppliedState(world,b);
        built=AppliedMeshCount;Show(a);Require(AppliedMeshCount==built,"Unaffected cached slice was lost");
        CheckAppliedState(world,a);
        // Equal per-chunk cuts reuse across stamps and dictionary insertion order.
        var c1=new Cell(127,127,-2);var c2=new Cell(128,128,-2);
        var cuts=new WorldView(-2,false,new Dictionary<Cell,float>{{c1,-1.8f},{c2,-1.6f}},101);
        Show(cuts);Show(a);built=AppliedMeshCount;
        var sameCuts=new WorldView(-2,false,new Dictionary<Cell,float>{{c2,-1.6f},{c1,-1.8f}},102);
        Show(sameCuts);Require(AppliedMeshCount==built,"Equivalent local cuts missed cache");
        CheckAppliedState(world,sameCuts);
        var otherCuts=new WorldView(-2,false,new Dictionary<Cell,float>{{c1,-1.4f},{c2,-1.6f}},103);
        Show(otherCuts);CheckAppliedState(world,otherCuts);
        built=AppliedMeshCount;Show(sameCuts);Require(AppliedMeshCount==built,"Prior cut profile was not retained");
        // Discovery and wall-contact shading must also invalidate inactive layers.
        var wall=new Cell(63,63,-1);
        world.Buildings[wall]=new(wall,BuildKind.Wall);world.Navigation.Invalidate(wall);
        built=AppliedMeshCount;Show(sameCuts);Require(AppliedMeshCount-built==4,"Wall addition invalidation was not local");
        CheckAppliedState(world,sameCuts);Show(a);CheckAppliedState(world,a);
        world.Buildings.Remove(wall);world.Navigation.Invalidate(wall);
        Show(a);CheckAppliedState(world,a);Show(sameCuts);CheckAppliedState(world,sameCuts);
        var revealed=new WorldView(-2,true);Show(revealed);CheckAppliedState(world,revealed);
        Show(b);CheckAppliedState(world,b);
        // Switch back while obsolete cold jobs are still in flight.
        Generate(world,new WorldView(-12));Generate(world,b);FlushMeshes();CheckAppliedState(world,b);
        // Force LRU eviction with several fully built levels, then revisit an old one.
        for(int level=-3;level>=-9;level--)Show(new(level));
        Require(_sliceCache.Count<=SliceCacheLimit&&_cachedBytes<=SliceCacheBudget,"Slice cache exceeded its limits");
        Show(a);CheckAppliedState(world,a);
        foreach(var entry in _sliceCache.Values)
            Require(!entry.Node.Visible&&entry.Node.GetChildren().OfType<StaticBody3D>().All(body=>body.CollisionLayer==0&&body.CollisionMask==0),"Inactive cached slice remains visible or collidable");
        Require(_sliceLru.Count==_sliceCache.Count&&_cachedBytes==_sliceCache.Values.Sum(e=>e.Bytes),"Cache accounting drifted");
        GD.Print($"SLICE_CACHE_CHECKS_OK: reuse, local edits/walls, cut profiles, discovery, LRU and inactive collision; entries={CachedSliceCount} estimatedBytes={CachedSliceBytes}");
    }
}
