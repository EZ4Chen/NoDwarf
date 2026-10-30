using Godot;
using NoDwarf.Services.Llm;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Startup
{
    private async void RunLlmSettingsReview()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            GetViewport().GuiEmbedSubwindows=true;
            _actions["settings"].EmitSignal(Button.SignalName.Pressed);await Frame();var window=_settingsWindow!;
            Check(window.Fields.Count>=30&&window.Categories.GetTabCount()==7,"Settings categories missing");
            for(int tab=0;tab<7;tab++)
            {
                window.Categories.CurrentTab=tab;await Frame();
                Check(window.Categories.Size.X<=window.Size.X&&window.Categories.Size.Y<window.Size.Y,"Settings overflow");
                if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng("res://.local/settings-review-"+tab+".png");
            }
            window.Size=new(560,380);
            for(int tab=0;tab<7;tab++)
            {
                window.Categories.CurrentTab=tab;await Frame();
                var page=window.Categories.GetCurrentTabControl();
                foreach(var field in window.Fields.Values.Where(f=>f.IsVisibleInTree()))
                    Check(field.GetGlobalRect().End.X<=page.GetGlobalRect().End.X+1,"Small settings page clips field: "+field.AccessibilityName);
                var shell=window.GetChildren().OfType<GameWindowFrame>().Single();
                Check(shell.GetRect().Encloses(shell.Footer.GetGlobalRect()),"Small settings footer overflow");
            }
            window.Categories.CurrentTab=0;await Frame();
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng("res://.local/settings-template-small.png");
            window.Size=new(850,620);await Frame();
            Check(window.Fields.Values.All(f=>f.AccessibilityName!=""),"Missing accessible labels");
            Check(((LineEdit)window.Fields["key"]).Secret,"API key not masked");
            ((LineEdit)window.Fields["url"]).Text="http://external.example";window.Save();Check(window.StatusText.Contains("HTTPS"),"Invalid endpoint saved");
            ((LineEdit)window.Fields["url"]).Text="https://example.invalid/v1";((LineEdit)window.Fields["key"]).Text="offline-fixture";
            ((SpinBox)window.Fields["autosave"]).Value=12;((SpinBox)window.Fields["camera"]).Value=1.5;((SpinBox)window.Fields["short"]).Value=8;window.Save();
            Check(PreferencesRuntime.Current.AutosaveMinutes==12&&PreferencesRuntime.Current.CameraSpeed==1.5&&PreferencesRuntime.Current.Dialogue.ShortTermRounds==8,"Preferences not applied: "+window.StatusText);
            Check(GamePreferences.Load(PreferencesRuntime.SettingsPath).AutosaveMinutes==12,"Preferences not persisted");
            Check(!File.ReadAllText(PreferencesRuntime.LlmPath).Contains("offline-fixture")&&LlmRuntime.Configuration.ApiKey=="offline-fixture","Session-only key persistence failed");
            ((SpinBox)window.Fields["details"]).Value=12;window.Save();Check(window.StatusText.Contains("详细"),"Invalid recall budget accepted");
            window.Close();await Frame();ShowGameSettings();await Frame();window=_settingsWindow!;
            Check(((SpinBox)window.Fields["autosave"]).Value==12&&((LineEdit)window.Fields["key"]).Text=="offline-fixture","Reopen lost settings");
            ((SpinBox)window.Fields["scale"]).Value=1.1;window.Save();Check(window.IsDisplayPreview,"Display change did not require confirmation");
            window.Close();await Frame();Check(Math.Abs(GetTree().Root.ContentScaleFactor-1)<.001&&PreferencesRuntime.Current.UiScale==1,"Closing preview did not restore display");
            ShowGameSettings();await Frame();window=_settingsWindow!;
            var provider=(OptionButton)window.Fields["provider"];provider.Select(1);provider.EmitSignal(OptionButton.SignalName.ItemSelected,1L);
            Check(((LineEdit)window.Fields["key"]).Text=="","Provider switch retained key");
            ((LineEdit)window.Fields["url"]).Text="http://127.0.0.1:1234/v1";((LineEdit)window.Fields["model"]).Text="fixture";window.Client=new WaitingLlmClient();
            var pending=window.TestAsync();await Frame();window.Close();await pending;await Frame();Check(_settingsWindow is null,"Closing did not release settings window");
            PreferencesRuntime.Save(new(),new());
            GD.Print("LLM_SETTINGS_OK: all categories, accessible controls, validation, settings persistence, key masking/session-only/provider reset, cancellation on close; no external network");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private sealed class WaitingLlmClient:ILlmClient
    {
        public async Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken cancellation)
        {await Task.Delay(Timeout.Infinite,cancellation);throw new InvalidOperationException("Expected cancellation");}
    }
}
