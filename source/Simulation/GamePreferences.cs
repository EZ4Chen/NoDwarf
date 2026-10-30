using System.Text.Json;

namespace NoDwarf.Simulation;

public sealed record DialogueOptions
{
    public bool Enabled {get;init;}
    public bool Summarize {get;init;}=true;
    public int IntervalHours {get;init;}=2;
    public int ShortTermRounds {get;init;}=6;
    public int LongTermRounds {get;init;}=80;
    public int RecallRounds {get;init;}=6;
    public int DetailedRounds {get;init;}=2;
    public int RecallHours {get;init;}=168;
    public int SummaryCharacters {get;init;}=160;
    public void Validate()
    {
        if(IntervalHours is <1 or >24||ShortTermRounds is <2 or >24||LongTermRounds is <8 or >200||RecallRounds is <1 or >12||DetailedRounds<0||DetailedRounds>RecallRounds||RecallHours is <0 or >2400||SummaryCharacters is <80 or >400)
            throw new InvalidDataException("对话记忆设置超出范围；详细轮数不能超过检索轮数。");
    }
}
public sealed record GamePreferences
{
    public int Version {get;init;}=1;
    public bool Fullscreen {get;init;}
    public bool Vsync {get;init;}=true;
    public int FrameLimit {get;init;}=60;
    public int Msaa {get;init;}
    public bool Shadows {get;init;}=true;
    public double UiScale {get;init;}=1;
    public int MasterVolume {get;init;}=100;
    public int AutosaveMinutes {get;init;}=5;
    public bool PauseOnFocusLoss {get;init;}=true;
    public bool PauseOnRaid {get;init;}=true;
    public bool StartPaused {get;init;}=true;
    public int StartSpeed {get;init;}=1;
    public double CameraSpeed {get;init;}=1;
    public double OrbitSpeed {get;init;}=1;
    public bool ResidentAi {get;init;}
    public bool Storyteller {get;init;}
    public int ResidentIntervalHours {get;init;}=2;
    public DialogueOptions Dialogue {get;init;}=new();
    public void Validate()
    {
        if(ResidentIntervalHours is <1 or >24||Version!=1||FrameLimit is <0 or >240||Msaa is <0 or >3||!double.IsFinite(UiScale)||UiScale is <.8 or >1.3||MasterVolume is <0 or >100||AutosaveMinutes is <0 or >60||StartSpeed is <1 or >3||!double.IsFinite(CameraSpeed)||CameraSpeed is <.25 or >3||!double.IsFinite(OrbitSpeed)||OrbitSpeed is <.25 or >3||Dialogue is null)
            throw new InvalidDataException("游戏设置无效，请恢复默认设置。");
        Dialogue.Validate();
    }
    public static GamePreferences Load(string path)
    {
        if(!File.Exists(path))return new();
        if(new FileInfo(path).Length>16384)throw new InvalidDataException("设置文件过大。");
        var settings=JsonSerializer.Deserialize<GamePreferences>(File.ReadAllText(path))??throw new InvalidDataException("设置文件为空。");
        settings.Validate();return settings;
    }
    public void Save(string path){Validate();SaveFiles.Write(path,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));}
}
