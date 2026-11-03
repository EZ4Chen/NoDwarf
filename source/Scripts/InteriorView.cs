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
        _revision=-1;_hoverCell=null;ClearHover();SyncWorld();RefreshUi();
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
}
