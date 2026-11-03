using System.Text.Json;
namespace NoDwarf.Simulation;

public sealed class SessionState
{
    public static readonly float DefaultCameraPitch=MathF.Atan2(38,50);
    public const float MinCameraPitch=20f*(MathF.PI/180f),MaxCameraPitch=80f*(MathF.PI/180f);
    [System.Text.Json.Serialization.JsonRequired] public int Version {get;set;}=2;
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WorldSnapshot? WorldState {get;set;}
    [System.Text.Json.Serialization.JsonRequired] public int TickRate {get;set;}=SimulationClock.BaseTicksPerSecond;
    public long Tick {get;set;} public int Speed {get;set;} public bool Paused {get;set;} public double Pending {get;set;}
    public bool Developer {get;set;} public bool Creative {get;set;}
    public Cell? FoundationMin {get;set;} public Cell? FoundationMax {get;set;} public int Floor {get;set;} public int FloorHeight {get;set;}=3;
    public int LayerOffset {get;set;} public int Placement {get;set;} public bool HeightSelected {get;set;}
    public int ViewMode {get;set;} public bool TopDown {get;set;} public float Yaw {get;set;} public float Zoom {get;set;}
    public float Pitch {get;set;}=DefaultCameraPitch;
    public float FocusX {get;set;} public float FocusY {get;set;} public float FocusZ {get;set;}
}

public sealed record LoadedSession(SessionState State,DemoWorld World,SimulationClock Clock,FoundationPlan? Foundation);

public static class SessionPersistence
{
    public static LoadedSession Read(string path)
    {
        if(new FileInfo(path).Length>64*1024*1024)throw new InvalidDataException("存档文件过大。");
        var s=JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path),DemoWorld.SaveOptions)??throw new InvalidDataException("无效存档。");
        if(!float.IsFinite(s.Pitch)||s.Pitch<SessionState.MinCameraPitch||s.Pitch>SessionState.MaxCameraPitch)throw new InvalidDataException("视角俯仰无效。");
        if(s.Version!=2)throw new InvalidDataException("不支持的存档版本。开发阶段请新建游戏；原存档未改写。");
        if(s.ViewMode is <0 or >3||s.Placement is <-1 or >1||!float.IsFinite(s.Yaw)||!float.IsFinite(s.Zoom)||s.Zoom is <12 or >240||!float.IsFinite(s.FocusX)||!float.IsFinite(s.FocusY)||!float.IsFinite(s.FocusZ))throw new InvalidDataException("视图存档无效。");
        if(s.WorldState is null)throw new InvalidDataException("世界存档结构无效。");
        var loaded=DemoWorld.LoadSnapshot(s.WorldState!);var clock=new SimulationClock();clock.Restore(s.Tick,s.Speed,s.Paused,s.Pending,s.TickRate);
        FoundationPlan? foundation=null;
        if(s.FoundationMin is Cell a&&s.FoundationMax is Cell b){if(!DemoWorld.Inside(a)||!DemoWorld.Inside(b))throw new InvalidDataException("地基越界。");foundation=new FoundationPlan(a,b);foundation.SetFloorHeight(s.FloorHeight);foundation.Step(s.Floor);}
        int level=(foundation?.WorldLevel??loaded.Camp.Level)+s.LayerOffset;
        if(level<=TerrainMap.MinLevel||level>TerrainMap.MaxLevel)throw new InvalidDataException("视图高度越界。");
        return new(s,loaded,clock,foundation);
    }
}
