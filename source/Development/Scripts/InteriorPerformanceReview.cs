using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Text.Json;
namespace NoDwarf;

public partial class Main
{
    // Fixed data/camera sequence. Timings exclude rendering, terrain upload and frame waits.
    private async void ReviewInteriorPerformance()
    {
        try
        {
            _clock.Paused=true;_depthReviewActive=true;SetProcess(false);
            _terrainView.FlushMeshes();_terrainView.Visible=false;
            _buildMenu.Hide();_inspector.Hide();_selectionPanel.Hide();_worldMenu.Hide();
            var rows=new List<object>();
            var room=SetupInteriorFixture(20,20);
            async Task Sample(string scene,int count)
            {
                for(int i=0;i<count;i++)
                {
                    _cameraYaw=Mathf.DegToRad((i%8)*45);_orbitPitch=Mathf.DegToRad(i%16<8?35:55);
                    long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                    PrepareInteriorWalls();
                    double visibilityMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    long visibilityBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    var old=_buildingVisuals.ToDictionary(p=>p.Key,p=>p.Value.Root.GetInstanceId());
                    bytes=GC.GetAllocatedBytesForCurrentThread();start=Stopwatch.GetTimestamp();
                    SyncStructuresAndPlans();
                    double buildingsMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    long buildingBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    int retained=_buildingVisuals.Count(p=>old.TryGetValue(p.Key,out var id)&&id==p.Value.Root.GetInstanceId());
                    rows.Add(new{scene,step=i,visibilityMs,visibilityBytes,buildingsMs,buildingBytes,retained,
                        created=_buildingVisuals.Count-retained,caps=_wallCaps?.Count??0,signature=CapSignature(_wallCaps)});
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            await Sample("room400",48);
            room=SetupInteriorFixture(40,40);
            await Sample("room1600",32);
            // The old production code skipped >512 cells. This uncapped original
            // sampling loop is the equal-work reference for the newly supported room.
            var referenceRows=new List<object>();
            for(int i=0;i<16;i++)
            {
                _cameraYaw=Mathf.DegToRad(i%8*45);_orbitPitch=Mathf.DegToRad(i<8?35:55);
                long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                var caps=TraceInteriorReference(_world,new[]{room},ActiveLevel,3,CameraBack,_cameraPitch);
                referenceRows.Add(new{step=i,ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds,bytes=GC.GetAllocatedBytesForCurrentThread()-bytes,caps=caps.Count,signature=CapSignature(caps)});
            }
            string label=OS.GetCmdlineUserArgs().FirstOrDefault(s=>s.StartsWith("--interior-label="))?.Split('=',2)[1]??"current";
            if(label.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new Exception("Invalid benchmark label");
            string json=JsonSerializer.Serialize(new{rows,referenceRows},new JsonSerializerOptions{WriteIndented=true});
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://.local/interior-performance-{label}.json"),json);
            if(OS.GetCmdlineUserArgs().Contains("--interior-checks"))await CheckInteriorOptimization();
            GD.Print("INTERIOR_PERFORMANCE_OK: "+label);GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private List<Cell> SetupInteriorFixture(int width,int depth)
    {
        _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
        _foundation=new FoundationPlan(new(90,90,-3),new(90+width-1,90+depth-1,-3));
        _layerOffset=0;_placement=0;_heightSelected=true;_interiorView=true;_cutaway=true;_floorWalls=false;_revealUnknown=false;_topDown=false;
        var room=new List<Cell>();
        for(int x=0;x<width;x++)for(int z=0;z<depth;z++)
        {
            var cell=new Cell(90+x,90+z,-3);room.Add(cell);
            for(int y=0;y<2;y++)_world.Terrain.Excavate(cell+new Cell(0,0,y));
            if(x%3==1&&z%3==1)_world.Buildings[cell]=new(cell,BuildKind.Bed);
        }
        foreach(var cell in room)
            foreach(var d in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
            {
                var c=cell+d;if(c.X>=90&&c.X<90+width&&c.Z>=90&&c.Z<90+depth)continue;
                for(int y=0;y<2;y++){var b=c+new Cell(0,0,y);_world.Terrain.Excavate(b);_world.Buildings[b]=new(b,BuildKind.Wall);}
            }
        _world.Navigation.Invalidate(room[0]);CreatePeople();return room;
    }
    private static string CapSignature(IReadOnlyDictionary<Cell,float>? caps)
    {
        var text=string.Join(';',(caps??new Dictionary<Cell,float>()).OrderBy(p=>p.Key.X).ThenBy(p=>p.Key.Z).ThenBy(p=>p.Key.Level)
            .Select(p=>$"{p.Key.X},{p.Key.Z},{p.Key.Level}:{BitConverter.SingleToInt32Bits(p.Value)}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
    // Frozen pre-optimization sampler; deliberately retains duplicate queries.
    private static Dictionary<Cell,float> TraceInteriorReference(DemoWorld world,IEnumerable<List<Cell>> rooms,int floor,int height,Vector3 cameraBack,float pitch)
    {
        var caps=new Dictionary<Cell,float>();var dirs=new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)};
        float ceiling=floor+height-1,reach=Math.Max(1.5f,(ceiling-floor+.5f)/Mathf.Tan(pitch));
        foreach(var room in rooms)
        {
            var roomCells=room.ToHashSet();
            foreach(var c in room)foreach(float ox in OcclusionSamples)foreach(float oz in OcclusionSamples)
                for(float distance=0;distance<=reach;distance+=.2f)
                {
                    int x=(int)Math.Floor(c.X+ox+cameraBack.X*distance+.5f),z=(int)Math.Floor(c.Z+oz+cameraBack.Z*distance+.5f);
                    for(int y=floor;y<ceiling;y++)
                    {
                        var block=new Cell(x,z,y);
                        if(DemoWorld.Inside(block)&&world.Solid(block))
                        {bool rim=y==floor&&(roomCells.Contains(block+dirs[0])||roomCells.Contains(block+dirs[1])||roomCells.Contains(block+dirs[2])||roomCells.Contains(block+dirs[3]));caps[block]=floor+(rim?.2f:0f);}
                    }
                }
        }
        return caps;
    }
    private async Task CheckInteriorOptimization()
    {
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        void Same(IReadOnlyDictionary<Cell,float> expected,string label)
        {
            if(CapSignature(_wallCaps)==CapSignature(expected))return;
            var actual=_wallCaps??throw new Exception("Missing cut dictionary");
            var extra=actual.FirstOrDefault(p=>!expected.TryGetValue(p.Key,out var cap)||cap!=p.Value);
            var missing=expected.FirstOrDefault(p=>!actual.TryGetValue(p.Key,out var cap)||cap!=p.Value);
            throw new Exception($"Cut mismatch {label}: actual={actual.Count} expected={expected.Count} extra={extra} missing={missing}");
        }
        var room=SetupInteriorFixture(40,40);
        foreach(float pitch in new[]{20f,35f,55f,80f})foreach(float yaw in new[]{0f,17f,45f,89f,90f,137f,180f,231f,270f,319f})
        {
            _cameraYaw=Mathf.DegToRad(yaw);_orbitPitch=Mathf.DegToRad(pitch);PrepareInteriorWalls();
            Same(TraceInteriorReference(_world,new[]{room},ActiveLevel,3,CameraBack,_cameraPitch),$"{yaw}/{pitch}");
        }
        _cameraYaw=0;_orbitPitch=Mathf.DegToRad(35);PrepareInteriorWalls();
        int builds=_interiorOcclusionBuilds,topologies=_roomTopologyBuilds;
        _cameraYaw=Mathf.Pi;PrepareInteriorWalls();_cameraYaw=0;PrepareInteriorWalls();
        Require(_interiorOcclusionBuilds==builds&&_roomTopologyBuilds==topologies,"Repeated camera pose recalculated occlusion/topology");
        int version=_world.Terrain.Revision;
        Require(_wallCaps!.Any(p=>!CurrentView.Known(_world,p.Key)),"Unknown foreground fixture missing");
        Require(version==_world.Terrain.Revision,"Cutaway changed physical terrain");
        SyncStructuresAndPlans();
        var bed=_buildingVisuals.First(p=>p.Value.Data.Kind==BuildKind.Bed).Key;
        ulong bedId=_buildingVisuals[bed].Root.GetInstanceId();
        var planCell=new Cell(92,92,-3);
        Require(_world.QueueBuild(planCell,BuildKind.Wall,announce:false),"Plan fixture failed");
        SyncStructuresAndPlans();int planId=_world.Jobs.Single(j=>j.Cell==planCell).Id;
        ulong planNode=_planVisuals[planId].Root.GetInstanceId();
        foreach(float yaw in new[]{45f,90f,180f,270f,0f})
        {
            _cameraYaw=Mathf.DegToRad(yaw);PrepareInteriorWalls();SyncStructuresAndPlans();
            Require(_buildingVisuals[bed].Root.GetInstanceId()==bedId,"Rotating cutaway rebuilt unchanged furniture");
            Require(_planVisuals[planId].Root.GetInstanceId()==planNode,"Rotating cutaway rebuilt unchanged plan");
            CheckBuildingShapes();
        }
        var wall=_buildingVisuals.First(p=>p.Value.Data.Kind==BuildKind.Wall&&p.Value.Height<1).Key;
        ulong cutId=_buildingVisuals[wall].Root.GetInstanceId();
        _interiorView=false;PrepareInteriorWalls();SyncStructuresAndPlans();
        Require(_buildingVisuals[wall].Height==1&&_buildingVisuals[wall].Root.GetInstanceId()!=cutId,"Full wall was not restored");
        Require(_buildingVisuals[bed].Root.GetInstanceId()==bedId,"Mode switch rebuilt unchanged furniture");
        _interiorView=true;PrepareInteriorWalls();SyncStructuresAndPlans();CheckBuildingShapes();
        // Split, open and reclose a room while retaining camera/floor keys.
        int miss=_interiorOcclusionBuilds;
        var split=new Cell(110,110,-3);_world.Buildings[split]=new(split,BuildKind.Wall);_world.Navigation.Invalidate(split);
        PrepareInteriorWalls();Require(_interiorOcclusionBuilds>miss,"Building edit kept stale occlusion");
        Same(TraceInteriorReference(_world,new[]{room.Where(c=>c!=split).ToList()},ActiveLevel,3,CameraBack,_cameraPitch),"interior obstacle");
        _world.Buildings.Remove(split);_world.Navigation.Invalidate(split);PrepareInteriorWalls();
        for(int z=90;z<130;z++){var c=new Cell(110,z,-3);_world.Buildings[c]=new(c,BuildKind.Wall);}
        _world.Navigation.Invalidate(split);PrepareInteriorWalls();
        Require(InteriorRooms(ActiveLevel).Cells.Count==1560&&_wallCaps!.Count>0,"Split large rooms were skipped or truncated");
        _world.Buildings.Remove(split);_world.Navigation.Invalidate(split);PrepareInteriorWalls();
        Require(InteriorRooms(ActiveLevel).Cells.Count==1561,"Reconnected room topology stayed split/stale");
        for(int z=90;z<130;z++)_world.Buildings.Remove(new Cell(110,z,-3));
        _world.Navigation.Invalidate(split);PrepareInteriorWalls();
        // A hole in the support changes room membership and must invalidate cached cuts.
        miss=_interiorOcclusionBuilds;_world.Terrain.Excavate(new Cell(100,100,-4));PrepareInteriorWalls();
        Require(_interiorOcclusionBuilds>miss,"Terrain edit kept stale occlusion");
        Require(_wallCaps!.Count==0,"Unsupported gap was accepted as an enclosed floor");
        // Another floor/world must never restore the previous world's cuts.
        _layerOffset=-3;PrepareInteriorWalls();Require(_wallCaps!.Count==0,"Wrong floor restored cached cuts");
        _layerOffset=0;PrepareInteriorWalls();Require(_wallCaps!.Count==0,"Return to edited floor restored stale cuts");
        room=SetupInteriorFixture(3,3);PrepareInteriorWalls();
        Same(TraceInteriorReference(_world,new[]{room},ActiveLevel,3,CameraBack,_cameraPitch),"world replacement");
        // Exercise the bounded result cache with many footprints/room heights.
        foreach(int height in new[]{3,6,9})
        {
            _foundation!.SetFloorHeight(height);
            for(int i=0;i<120;i++){_cameraYaw=i*.071f;_orbitPitch=Mathf.DegToRad(20+i%50);PrepareInteriorWalls();}
        }
        Require(_interiorCuts.Count<=InteriorCutLimit&&_cachedInteriorCaps<=InteriorCapLimit,"Occlusion cache exceeded budget");
        Require(_cutLru.Count==_interiorCuts.Count&&_cachedInteriorCaps==_interiorCuts.Values.Sum(v=>v.Caps.Count),"Occlusion cache accounting drifted");
        // Full terrain/placed-wall integration, including the frontmost floor tiles.
        room=SetupInteriorFixture(40,40);_terrainView.Visible=true;_camera.Size=58;_orbitPitch=Mathf.DegToRad(35);
        _focus=WorldPosition(new(110,110,-3));
        foreach(float yaw in new[]{0f,45f,90f,135f,180f,225f,270f,315f})
        {
            _cameraYaw=Mathf.DegToRad(yaw);PositionCamera();SyncWorld();
            var timer=Stopwatch.StartNew();
            while(_terrainView.PendingMeshCount>0)
            {if(timer.Elapsed.TotalSeconds>60)throw new Exception("Large-room mesh timeout");await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            var front=new Cell(110+(int)(CameraBack.X*17),110+(int)(CameraBack.Z*17),-3);
            Require(Pick(_camera.UnprojectPosition(WorldPosition(front)),1)==front,$"Large room front floor picking failed at {yaw}");
            CheckBuildingShapes();
        }
        if(DisplayServer.GetName()!="headless")
        {
            RefreshUi();
            // Explicit test draw avoids waiting indefinitely when another app covers
            // the window. Screenshot readback synchronizes this test-only render.
            RenderingServer.ForceDraw();
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/large-room-cutaway.png"))!=Error.Ok)
                throw new Exception("Could not save large-room preview");
        }
        GD.Print("INTERIOR_OPTIMIZATION_OK: reference parity, cache reuse/invalidation/bounds, retained buildings/plans, wall restoration, 1600-cell room and eight-angle picking");
    }
    private void CheckBuildingShapes()
    {
        foreach(var entry in _buildingVisuals)
        {
            if(!BlockCatalog.IsWall(entry.Value.Data.Kind))continue;
            float height=Math.Clamp(CurrentView.CellTop(entry.Key)-entry.Key.Level,0,1);
            if(entry.Value.Height!=height||entry.Value.Faces!=(int)BlockExposure.BuildingFaces(_world,CurrentView,entry.Key))
                throw new Exception("Stale building shape/neighbor faces: "+entry.Key);
            var mesh=entry.Value.Root.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().FirstOrDefault();
            if(mesh is not null&&mesh.Mesh.GetAabb().Size.Y>height+.001f)throw new Exception("Wall mesh exceeded cut height");
        }
    }
}
