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
    private bool MiningCutaway=>_cutaway&&_tool is "dig" or "digstairs";
    private bool LocalWallCutaway=>_interiorView||MiningCutaway;
    private int WallCutHeight=>MiningCutaway&&!_interiorView?2:_foundation?.FloorHeight??3;
    private int WallCutFloor=>MiningCutaway&&!_interiorView&&!_heightSelected&&_foundation is null?ActiveLevel-1:ActiveLevel;
    private (DemoWorld,long,int,int,int,float,float,bool,bool,bool,bool)? _interiorInputs;
    private int _roomTopologyBuilds;
    private static readonly float[] OcclusionSamples={-.4f,0f,.4f};
    private void PrepareInteriorWalls()
    {
        var inputs=(_world,_world.Navigation.Version,_world.Terrain.Revision,WallCutFloor,WallCutHeight,_cameraYaw,_cameraPitch,_topDown,_interiorView,_cutaway,MiningCutaway);
        if(_interiorInputs==inputs)return;
        _interiorInputs=inputs;
        if(!LocalWallCutaway||!_cutaway)
        {CancelInteriorWork();SetInteriorCaps(null);return;}
        using var timing = _world.Probe?.Measure("view.interior-visibility") ?? default;
        if(_backgroundInterior){PrepareInteriorAsync(WallCutFloor,WallCutHeight);return;}
        var caps=InteriorCaps(WallCutFloor,WallCutHeight);
        SetInteriorCaps(caps);
    }
    private void ApplyDefaultEditor()
    {
        _developerMode=true;_creative=true;_revealUnknown=false;_clock.Paused=true;
        _world.DeveloperResidents(false);_selectedWorker=-1;
        _world.Events.Clear();_world.Log("开发模式 · 无居民 · 直接编辑；按 ～ 打开控制台，play 添加居民并开始游戏。");
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
        if(_wallCaps!.Count==0)throw new InvalidOperationException("Large interior must retain front-wall cutaway");
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
