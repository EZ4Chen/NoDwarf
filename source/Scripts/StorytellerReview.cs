using Godot;
using NoDwarf.Services.Llm;
using NoDwarf.Simulation;

namespace NoDwarf;
public partial class Main
{
    private async void ReviewStoryteller()
    {
        var original=LlmRuntime.Configuration;
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            _clock.Paused=true;_world.DeveloperResidents(true);CreatePeople();
            LlmRuntime.Configure(new(){ApiKey="offline-fixture"});
            _storyDirector=new(new ReviewStoryClient());
            _storyPanel!.Show();RefreshStoryInterface();await Frame();
            Check(_storyValue.Text.Contains("总价值")&&_storyToggle.AccessibilityName!="","Value panel or accessibility missing");
            Check(GetViewport().GetVisibleRect().Encloses(_storyPanel.GetGlobalRect()),"Story panel outside viewport");
            _storyToggle.EmitSignal(Godot.Button.SignalName.Pressed);await Frame();
            Check(_world.Story.Enabled&&_world.Story.Turn is null,"Enabling while paused issued a request");
            _storyDirector.Pump(_world,LlmRuntime.Configuration,true);
            for(int i=0;i<120&&_world.Story.Turn?.Status!="received";i++)await Frame();
            Check(_world.Story.Turn?.Status=="received"&&_world.Story.Pending.Count==0,"Paused network result changed world");
            var snapshot=DemoWorld.LoadJson(_world.SaveJson());
            Check(snapshot.Story.Turn!.AcceptedJson.Contains("schedule_supplies"),"Received JSON missing from save");
            _storyDirector.Pump(_world,LlmRuntime.Configuration,true);RefreshStoryInterface();await Frame();
            Check(_world.Story.Pending.Count==1&&_storyEvents.GetChildCount()==1,$"Queued event missing from panel: pending={_world.Story.Pending.Count}, rows={_storyEvents.GetChildCount()}, error={_world.Story.Error}, receipt={_world.Story.Turn?.Receipts.LastOrDefault()?.Result}");
            var cancel=_storyEvents.FindChildren("*","Button",true,false).OfType<Button>().Single();
            cancel.EmitSignal(Godot.Button.SignalName.Pressed);await Frame();Check(_world.Story.Pending.Count==0,"UI cancellation failed");
            _storyToggle.EmitSignal(Godot.Button.SignalName.Pressed);Check(!_world.Story.Enabled,"Stop button failed");
            GD.Print("STORYTELLER_UI_OK: wealth/status panel, opt-in, paused async JSON, save snapshot, queued event, cancellation and stop; no external network");
            LlmRuntime.Configure(original);GetTree().Quit();
        }
        catch(Exception e){LlmRuntime.Configure(original);GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private sealed class ReviewStoryClient:ILlmClient
    {
        public async Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken cancellation)
        {
            await Task.Delay(20,cancellation);
            return new("{\"tool\":\"schedule_supplies\",\"arguments\":{\"item\":\"wood\",\"count\":2,\"delay_hours\":1,\"reason\":\"发现木料\"}}",1,1,20);
        }
    }
}
