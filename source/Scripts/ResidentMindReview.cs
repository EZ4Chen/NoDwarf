using Godot;
using NoDwarf.Services.Llm;
using NoDwarf.Simulation;

namespace NoDwarf;
public partial class Main
{
    private async void ReviewResidentMind()
    {
        var original=LlmRuntime.Configuration;
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            _clock.Paused=true;_world.DeveloperResidents(false);_world.DeveloperResidents(true);CreatePeople();
            foreach(var w in _world.Workers){w.Person.Hunger=80;w.Person.Rest=80;w.Person.Thirst=80;w.Person.Recreation=60;}
            LlmRuntime.Configure(new(){ApiKey="offline-fixture"});_residentMindDirector=new(new MindReviewClient());
            _mindPanel.Show();RefreshResidentMindInterface();await Frame();
            Check(_mindToggle.AccessibilityName!=""&&_mindInterval.AccessibilityName!="","Accessible labels missing");
            Check(GetViewport().GetVisibleRect().Encloses(_mindPanel.GetGlobalRect()),"Mind panel outside viewport");
            Check(_mindDetails.Text.Contains("经历"),"Personality details absent");
            _mindInterval.Value=4;Check(_world.ResidentMind.IntervalHours==4,"Interval control not connected");
            _mindToggle.EmitSignal(Godot.Button.SignalName.Pressed);await Frame();
            Check(_world.ResidentMind.Enabled&&_world.ResidentMind.Turn is null,"Toggle while paused sent request");
            _residentMindDirector.Pump(_world,LlmRuntime.Configuration,true);
            for(int i=0;i<120&&_world.ResidentMind.Turn?.Status!="received";i++)await Frame();
            Check(_world.ResidentMind.Turn?.Status=="received"&&_world.ResidentMind.History.Count==0,"Paused receive phase wrong: "+_world.ResidentMind.Error);
            _residentMindDirector.Pump(_world,LlmRuntime.Configuration,true);RefreshResidentMindInterface();await Frame();
            Check(_world.ResidentMind.History.Count==1&&_mindDetails.Text.Contains("当前需求与偏好"),"Decision result absent from UI");
            Check(DemoWorld.LoadJson(_world.SaveJson()).ResidentMind.IntervalHours==4,"Settings lost in save");
            _mindToggle.EmitSignal(Godot.Button.SignalName.Pressed);Check(!_world.ResidentMind.Enabled,"Stop did not disable AI");
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng("res://.local/personality-panel.png");
            GD.Print("PERSONALITY_UI_OK: accessible controls, panel bounds, interval, opt-in, paused receive, actual decision feedback, save and stop; no external requests");
            LlmRuntime.Configure(original);GetTree().Quit();
        }
        catch(Exception e){LlmRuntime.Configure(original);GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private sealed class MindReviewClient:ILlmClient
    {
        public async Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken cancellation)
        {
            await Task.Delay(20,cancellation);
            return new("{\"tool\":\"choose\",\"arguments\":{\"option_id\":\"wait\",\"reason\":\"先继续手头的安排。\",\"memory_ids\":[]}}",1,1,20);
        }
    }
}
