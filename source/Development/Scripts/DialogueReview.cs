using Godot;
using NoDwarf.Services.Llm;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewDialogues()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            GetViewport().GuiEmbedSubwindows=true;_clock.Paused=true;_world.DeveloperResidents(false);_world.DeveloperResidents(true);CreatePeople();FocusCamp();
            ApplyGamePreferences();_dialogueWorld=_world;_world.DialogueSettings=new(){Enabled=true};LlmRuntime.Configure(new(){ApiKey="offline-fixture"});
            _dialogueDirector=new(new SpeechClient());_world.QueueConversation(_world.Workers[0],_world.Workers[1]);
            for(int i=0;i<80&&_world.Dialogues.Records.Count==0;i++){_dialogueDirector.Pump(_world,LlmRuntime.Configuration,true);await Frame();}
            Check(_world.Dialogues.Records.Count==1,"Dialogue never committed: "+_world.Dialogues.Error);
            await Frame();Check(_people.Any(p=>p.GetNodeOrNull<Label3D>("Speech") is not null),"Speech bubble absent");
            ShowDialogueJournal();await Frame();Check(_dialogueText!.Text.Contains("休息之后")&&_dialogueText.Text.Contains("短期记忆"),"Journal missing transcript");
            Check(_dialogueSearch!.AccessibilityName!=""&&_dialoguePerson!.AccessibilityName!="","Journal accessibility missing");
            _dialoguePerson!.Select(3);_dialoguePerson.EmitSignal(OptionButton.SignalName.ItemSelected,3L);Check(_dialogueText.Text.Contains("暂无"),"Uninvolved resident filter leaked dialogue");
            _dialoguePerson.Select(0);_dialoguePerson.EmitSignal(OptionButton.SignalName.ItemSelected,0L);_dialogueSearch.Text="不存在的对话";_dialogueSearch.EmitSignal(LineEdit.SignalName.TextChanged,_dialogueSearch.Text);Check(_dialogueText.Text.Contains("暂无"),"Journal search failed");
            _dialogueSearch.Text="";_dialogueSearch.EmitSignal(LineEdit.SignalName.TextChanged,"");await Frame();Check(_dialogueText.Text.Contains("休息之后"),"Clearing search failed to restore transcript");
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng("res://.local/dialogue-review.png");
            _dialogueWindow!.Hide();ShowGameSettings();await Frame();Check(_settingsWindow!.World==_world,"In-game settings unbound");
            ((CheckBox)_settingsWindow.Fields["dialogue"]).ButtonPressed=false;((CheckBox)_settingsWindow.Fields["shadows"]).ButtonPressed=false;((SpinBox)_settingsWindow.Fields["fps"]).Value=75;_settingsWindow.Save();Check(!_sun.ShadowEnabled&&Engine.MaxFps==75,"Graphics settings not applied");Check(!_world.DialogueSettings.Enabled,"Settings failed to stop dialogue");_settingsWindow.Close();
            GD.Print("DIALOGUE_UI_OK: four utterances, visible speech, journal/filter/search, accessibility, in-game settings opt-out; no external requests");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private sealed class SpeechClient:ILlmClient
    {
        public async Task<LlmReply> CompleteAsync(LlmConfiguration c,IReadOnlyList<LlmMessage> messages,CancellationToken token)
        {await Task.Delay(5,token);return new("{\"text\":\"休息之后，我们再继续研究吧。\"}",1,1,5);}
    }
}
