using Godot;
using NoDwarf.Services.Llm;

namespace NoDwarf;
public partial class Startup
{
    private async void RunLlmSettingsReview()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            Check(_actions.ContainsKey("settings"),"Main menu settings entry missing");
            _actions["settings"].EmitSignal(Button.SignalName.Pressed);await Frame();
            Check(_llmKey.Secret&&_llmKey.AccessibilityName!=""&&_llmModel.AccessibilityName!="","Secret field/accessibility missing");
            _llmUrl.Text="http://external.example";SaveLlmSettings();
            Check(_llmStatus.Text.Contains("HTTPS"),"Invalid endpoint was saved");
            _llmUrl.Text="https://example.invalid/v1";_llmModel.Text="fixture-model";_llmKey.Text="fixture-only-secret";
            _llmRemember.ButtonPressed=false;SaveLlmSettings();
            Check(File.Exists(LlmSettingsPath)&&!File.ReadAllText(LlmSettingsPath).Contains("fixture-only-secret"),"Session key leaked into settings");
            Check(LlmRuntime.Configuration.ApiKey=="fixture-only-secret","Session key lost");
            _actions["llm-back"].EmitSignal(Button.SignalName.Pressed);await Frame();
            _actions["settings"].EmitSignal(Button.SignalName.Pressed);await Frame();
            Check(_llmModel.Text=="fixture-model"&&_llmKey.Text=="fixture-only-secret","Saved settings did not survive menu navigation");
            _llmModel.Text="unsaved";ShowHome();ShowLlmSettings();await Frame();
            Check(_llmModel.Text=="fixture-model","Back applied unsaved changes");
            _llmProvider.Select(1);_llmProvider.EmitSignal(OptionButton.SignalName.ItemSelected,1L);
            Check(_llmKey.Text==""&&!_llmRemember.ButtonPressed,"Switching provider retained credentials");
            var scroll=_llmLimit.GetParent().GetParent().GetParent<ScrollContainer>();
            _llmLimit.GetLineEdit().GrabFocus();await Frame();
            Check(scroll.GetGlobalRect().Intersects(_llmLimit.GetGlobalRect()),"Keyboard cannot reach last setting");
            foreach(var b in _actions.Values)Check(GetViewport().GetVisibleRect().Encloses(b.GetGlobalRect()),"Settings button outside viewport: "+b.Text);
            _llmUrl.Text="http://127.0.0.1:1234/v1";_llmModel.Text="test";_llmClient=new WaitingLlmClient();
            var pending=TestLlmAsync();await Frame();
            Check(_actions["llm-test"].Disabled&&!_actions["llm-cancel"].Disabled,"Request did not enter busy state");
            _actions["llm-cancel"].EmitSignal(Button.SignalName.Pressed);await pending;
            Check(!_actions["llm-test"].Disabled&&_llmStatus.Text.Contains("取消"),"Cancellation did not restore controls");
            pending=TestLlmAsync();ShowHome();await pending;await Frame();
            Check(_actions.ContainsKey("settings")&&_llmTest is null,"Leaving settings did not cancel request");
            File.Delete(LlmSettingsPath);LlmRuntime.Configure(new());
            GD.Print("LLM_SETTINGS_OK: menu navigation, validation, masked key, session-only persistence, draft discard, provider reset, keyboard/layout, cancellation and page exit; no network requests");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private sealed class WaitingLlmClient : ILlmClient
    {
        public async Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken cancellation)
        {await Task.Delay(Timeout.Infinite,cancellation);throw new InvalidOperationException("Expected cancellation");}
    }
}
