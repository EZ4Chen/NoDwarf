using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private bool _interiorView;
    private IReadOnlyDictionary<Cell,float>? _wallCaps;
    private int _viewStamp;
    private int _viewQuadrant=-1;
    private void SetViewMode(int mode)
    {
        _cutaway=mode!=0;_interiorView=mode==1;_floorWalls=mode==3;
        if(mode!=2)SelectTool("select");
        _revision=-1;_hoverCell=null;Clear(_hover);SyncWorld();RefreshUi();
    }
    private (DemoWorld,long,int,int,float,float,bool,bool,bool)? _interiorInputs;
    private (DemoWorld,long,int)? _roomTopologyInputs;
    private readonly List<List<Cell>> _interiorRooms=new();
    private int _roomTopologyBuilds;
    private static readonly float[] OcclusionSamples={-.4f,0f,.4f};
    private void PrepareInteriorWalls()
    {
        var inputs=(_world,_world.Navigation.Version,ActiveLevel,_foundation?.FloorHeight??3,_cameraYaw,_cameraPitch,_topDown,_interiorView,_cutaway);
        if(_interiorInputs==inputs)return;
        _interiorInputs=inputs;
        var previous=_wallCaps;
        if(!_interiorView||!_cutaway)
        { if(previous is not null)_viewStamp++;_wallCaps=null;return; }
        using var timing = _world.Probe?.Measure("view.interior-visibility") ?? default;
        int floor=ActiveLevel;
        bool Open(Cell c)
        {
            if(!DemoWorld.Inside(c)||_world.NaturalSolid(c)||_world.NaturalRamp(c)||!_world.Solid(c+new Cell(0,0,-1)))return false;
            return !_world.Buildings.TryGetValue(c,out var b)||(!BlockCatalog.IsWall(b.Kind)&&b.Kind!=BuildKind.Door);
        }
        var dirs=new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)};
        var seeds=_world.Terrain.Excavated.Where(c=>c.Level==floor)
            .Concat(_world.Buildings.Keys.Where(c=>c.Level==floor).SelectMany(c=>dirs.Select(d=>c+d))).Distinct();
        var topology=(_world,_world.Navigation.Version,floor);
        if(_roomTopologyInputs!=topology)
        {
        _roomTopologyInputs=topology;_interiorRooms.Clear();_roomTopologyBuilds++;
        var visited=new HashSet<Cell>();
        foreach(var seed in seeds)
        {
            if(visited.Contains(seed)||!Open(seed))continue;
            var room=new List<Cell>();var queue=new Queue<Cell>();queue.Enqueue(seed);visited.Add(seed);
            bool enclosed=true;
            while(queue.Count>0)
            {
                var c=queue.Dequeue();room.Add(c);
                foreach(var d in dirs)
                {
                    var n=c+d;
                    if(!DemoWorld.Inside(n)){enclosed=false;continue;}
                    bool open=Open(n);
                    if(open&&visited.Add(n))queue.Enqueue(n);
                    if(!open&&!_world.Solid(n)&&!(_world.Buildings.TryGetValue(n,out var boundary)&&boundary.Kind==BuildKind.Door))enclosed=false;
                }
            }
            _world.Probe?.Count("view.interior-visited-cells", room.Count);
            if(!enclosed||queue.Count>0||room.Count>512)continue;
            _interiorRooms.Add(room);
        }
        }
        var caps=new Dictionary<Cell,float>();
        var cameraBack=CameraBack;
        float ceiling=floor+(_foundation?.FloorHeight??3)-1;
        float reach=(ceiling-floor+.5f)/Mathf.Tan(_cameraPitch);
        foreach(var room in _interiorRooms)
        {
            // Trace the screen-space footprint of known room floors towards the camera.
            // Unknown foreground solids may occlude too; suppress their geometry without discovering them.
            var roomCells=room.ToHashSet();
            foreach(var c in room)
            foreach(float ox in OcclusionSamples)foreach(float oz in OcclusionSamples)
            for(float distance=0;distance<=reach;distance+=.2f)
            {
                int x=(int)Math.Floor(c.X+ox+cameraBack.X*distance+.5f);
                int z=(int)Math.Floor(c.Z+oz+cameraBack.Z*distance+.5f);
                for(int y=floor;y<ceiling;y++)
                {
                    var block=new Cell(x,z,y);
                    if(DemoWorld.Inside(block)&&_world.Solid(block))
                    {
                        bool rim=y==floor&&(roomCells.Contains(block+dirs[0])||roomCells.Contains(block+dirs[1])||roomCells.Contains(block+dirs[2])||roomCells.Contains(block+dirs[3]));
                        caps[block]=floor+(rim?.2f:0f);
                    }
                }
            }
        }
        if(previous is null||previous.Count!=caps.Count||caps.Any(pair=>!previous.TryGetValue(pair.Key,out float old)||old!=pair.Value))
        {_wallCaps=caps;_viewStamp++;}
        else _wallCaps=previous;
    }
    private void ApplyDefaultEditor()
    {
        _developerMode=true;_creative=true;_revealUnknown=false;_clock.Paused=true;
        _world.DeveloperResidents(false);_selectedWorker=-1;
        _world.Events.Clear();_world.Log("开发模式 · 无居民 · 直接编辑；F1 可手动添加居民。");
        CreatePeople();SyncWorld();RefreshUi();
    }
    private void DrawClearanceOutline(Cell c)
    {
        var p=WorldPosition(c)+Vector3.Up*.5f;var mat=Mat("df8678");
        foreach(float a in new[]{-.49f,.49f})foreach(float b in new[]{-.49f,.49f})
        {
            Box(_hover,p+new Vector3(a,b,0),new Vector3(.025f,.025f,.98f),mat);
            Box(_hover,p+new Vector3(a,0,b),new Vector3(.025f,.98f,.025f),mat);
            Box(_hover,p+new Vector3(0,a,b),new Vector3(.98f,.025f,.025f),mat);
        }
    }
    private async void CaptureInteriorPreview()
    {
        try
        {
            _camera.Size=95;SetViewMode(0);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/terrain-overview-preview.png"))!=Error.Ok)
                throw new IOException("Terrain preview save failed");
            StartUndergroundDemo();_world.DeveloperFinishPlans();_camera.Size=14;
            foreach(var mode in new[]{1,2,0})
            {
                SetViewMode(mode);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var path=ProjectSettings.GlobalizePath($"res://.local/interior-mode-{mode}.png");
                if(GetViewport().GetTexture().GetImage().SavePng(path)!=Error.Ok)throw new IOException("Preview save failed");
            }
            foreach(float yaw in new[]{90f,180f,270f})
            {
                _cameraYaw=Mathf.DegToRad(yaw);PositionCamera();SetViewMode(1);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/interior-angle-{yaw}.png"))!=Error.Ok)
                    throw new IOException("Rotated preview save failed");
            }
            var mineral=_world.Terrain.Ores.Keys.First(c=>c.Level<-32&&c.X>10&&c.Z>10&&c.X<TerrainMap.Size-10&&c.Z<TerrainMap.Size-10);
            var origin=mineral+new Cell(1,-2);
            for(int x=0;x<5;x++)for(int z=0;z<5;z++)for(int y=0;y<2;y++)
                _world.Terrain.Excavate(origin+new Cell(x,z,y));
            _foundation=new FoundationPlan(origin,origin+new Cell(4,4));_layerOffset=0;_heightSelected=true;
            _cameraYaw=0;_focus=WorldPosition(origin+new Cell(2,2));PositionCamera();SetViewMode(1);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/deep-vein-preview.png"))!=Error.Ok)
                throw new IOException("Deep ore preview save failed");
            GD.Print("INTERIOR_PREVIEW_OK");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private void CheckInteriorReview()
    {
        _world=new DemoWorld(flat:true);_overview.World=_world;CreatePeople();
        var origin=new Cell(10,10,-3);
        for(int x=0;x<20;x++)for(int z=0;z<30;z++)for(int y=0;y<2;y++)
            _world.Terrain.Excavate(origin+new Cell(x,z,y));
        _foundation=new FoundationPlan(origin,origin+new Cell(19,29));_layerOffset=0;_placement=0;
        _interiorView=true;_cutaway=true;PrepareInteriorWalls();
        if(_wallCaps!.Count!=0)throw new InvalidOperationException("Large interior must not be split into fake small rooms");
        int stamp=_viewStamp;PrepareInteriorWalls();
        if(_viewStamp!=stamp)throw new InvalidOperationException("Unchanged view must not rebuild terrain");
        _world=new DemoWorld(flat:true);_overview.World=_world;CreatePeople();
        origin=new Cell(20,20,-3);
        for(int x=0;x<3;x++)for(int z=0;z<3;z++)for(int y=0;y<2;y++)
            _world.Terrain.Excavate(origin+new Cell(x,z,y));
        _foundation=new FoundationPlan(origin,origin+new Cell(2,2));_cameraYaw=0;
        PrepareInteriorWalls();
        if(!_wallCaps!.Any(p=>Math.Abs(p.Value-(origin.Level+.2f))<.001f))throw new InvalidOperationException("Cutaway must retain a low wall base at the room boundary");
        var hiddenForeground=_wallCaps!.Keys.Where(c=>!CurrentView.Known(_world,c)&&!CurrentView.CornerRim(_world,c)).ToArray();
        if(hiddenForeground.Length==0)throw new InvalidOperationException("Camera obstruction must include unknown foreground beyond room wall");
        if(hiddenForeground.Any(c=>!_world.NaturalSolid(c)||CurrentView.Known(_world,c)))throw new InvalidOperationException("Hiding foreground must not excavate or discover it");
        _interiorView=false;PrepareInteriorWalls();
        GD.Print("FOREGROUND_OK: unknown foreground is hidden without discovery or terrain changes");
        GD.Print("REVIEW_OK: large-room traversal and stable view cache");
    }
}
