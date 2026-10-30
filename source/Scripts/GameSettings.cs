using Godot;
namespace NoDwarf;
public partial class Startup
{
    private SettingsWindow? _settingsWindow;
    private void ShowGameSettings()
    {
        if(_settingsWindow is not null){_settingsWindow.PopupCenteredClamped(new(850,620),.92f);return;}
        _settingsWindow=new();AddChild(_settingsWindow);_settingsWindow.Closed+=()=>_settingsWindow=null;_settingsWindow.PopupCenteredClamped(new(850,620),.92f);
    }
}
public partial class Main
{
    private SettingsWindow? _settingsWindow;
    private double _autosaveElapsed;
    private int _settingsRevision=-1,_observedRaid;
    private void ShowGameSettings()
    {
        if(_settingsWindow is not null){_settingsWindow.PopupCenteredClamped(new(850,620),.92f);return;}
        bool paused=_clock.Paused;_clock.Paused=true;_worldMenu.Hide();
        _settingsWindow=new(){World=_world};AddChild(_settingsWindow);_settingsWindow.Applied+=ApplyGamePreferences;
        _settingsWindow.Closed+=()=>{_settingsWindow=null;_clock.Paused=paused;};_settingsWindow.PopupCenteredClamped(new(850,620),.92f);
    }
    private void ApplyGamePreferences()
    {
        _settingsRevision=PreferencesRuntime.Revision;PreferencesRuntime.Apply(GetTree().Root);
        _sun.ShadowEnabled=PreferencesRuntime.Current.Shadows;
        _world.DialogueSettings=PreferencesRuntime.Current.Dialogue;
        _autosaveElapsed=0;
    }
    private void PumpGamePreferences(double delta)
    {
        if(_settingsRevision!=PreferencesRuntime.Revision)ApplyGamePreferences();
        var p=PreferencesRuntime.Current;
        if(_world.Enemies.Count>_observedRaid&&p.PauseOnRaid&&!PreferencesRuntime.Testing)_clock.Paused=true;
        _observedRaid=_world.Enemies.Count;
        if(p.AutosaveMinutes>0&&!_clock.Paused&&!PreferencesRuntime.Testing&&_loadTask is not {IsCompleted:false})
        {
            _autosaveElapsed+=delta;
            if(_autosaveElapsed>=p.AutosaveMinutes*60&&_saveTask is not {IsCompleted:false}){_autosaveElapsed=0;_=SaveSessionAsync();}
        }
    }
}
