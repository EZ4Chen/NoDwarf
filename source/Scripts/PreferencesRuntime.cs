using Godot;
using NoDwarf.Simulation;
using NoDwarf.Services.Llm;
namespace NoDwarf;
public static class PreferencesRuntime
{
    public static GamePreferences Current {get;private set;}=new();
    public static int Revision {get;private set;}
    public static string Issue {get;private set;}="";
    private static bool initialized;
    public static bool Testing=>OS.GetCmdlineUserArgs().Any(a=>a.EndsWith("-test")||a.EndsWith("-review"));
    public static string SettingsPath=>ProjectSettings.GlobalizePath(Testing?"res://.local/preferences-tests/game.json":"user://settings/game.json");
    public static string LlmPath=>ProjectSettings.GlobalizePath(Testing?"res://.local/preferences-tests/llm.json":"user://settings/llm-v1.json");
    public static void Initialize()
    {
        if(initialized)return;initialized=true;
        try{Current=Testing?new():GamePreferences.Load(SettingsPath);}
        catch(Exception e) when(e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException){Issue="无法读取游戏设置，暂用默认值。";}
        if(!LlmRuntime.Initialized)
            try{LlmRuntime.Configure(Testing?new():LlmSettingsStore.Load(LlmPath));}
            catch(LlmException e){Issue=e.Message;}
    }
    public static void Save(GamePreferences settings,LlmConfiguration config)
    {
        settings.Validate();config.Validate();settings.Save(SettingsPath);LlmSettingsStore.Save(LlmPath,config);
        Current=settings;
        var previous=LlmRuntime.Configuration;
        if(previous.Settings!=config.Settings||previous.ApiKey!=config.ApiKey||previous.RememberKey!=config.RememberKey)LlmRuntime.Configure(config);
        Revision++;Issue="";
    }
    public static void Apply(Window root,GamePreferences? preferences=null)
    {
        var p=preferences??Current;
        root.MinSize=new(960,640);
        if(DisplayServer.GetName()!="headless")
        {
            root.Mode=p.Fullscreen?Window.ModeEnum.Fullscreen:Window.ModeEnum.Windowed;
            DisplayServer.WindowSetVsyncMode(p.Vsync?DisplayServer.VSyncMode.Enabled:DisplayServer.VSyncMode.Disabled);
        }
        root.ContentScaleFactor=(float)p.UiScale;
        root.Msaa3D=(Viewport.Msaa)p.Msaa;
        Engine.MaxFps=p.FrameLimit;
        AudioServer.SetBusMute(0,p.MasterVolume==0);
        AudioServer.SetBusVolumeDb(0,p.MasterVolume==0?-80:Mathf.LinearToDb(p.MasterVolume/100f));
    }
}
