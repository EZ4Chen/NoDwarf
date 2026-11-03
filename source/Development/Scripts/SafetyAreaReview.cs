using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewSafetyAreas()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("SAFETY_UI: "+why);}
            IEnumerable<Node> Nodes(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var nested in Nodes(child))yield return nested;}}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Click(BaseButton button)
            {
                for(Node? parent=button.GetParent();parent is not null;parent=parent.GetParent())if(parent is ScrollContainer scroll)scroll.EnsureControlVisible(button);
                await Frame();Check(button.IsVisibleInTree()&&button.AccessibilityName!="","visible accessible button");
                var p=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);
                foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=down},true);
                await Frame();
            }
            Button Action(string text)=>Nodes(_safetyFrame!).OfType<Button>().Single(b=>b.Text==text);
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;
            _consolePanel.Hide();_characterEditor.Hide();_inspector.Hide();
            ShowBuildPage("区域");_buildMenu.Show();await Frame();
            await Click(Nodes(_buildPages["区域"]).OfType<Button>().Single(b=>b.AccessibilityName=="安全作业范围"));
            Check(_safetyPanel is not null,"open from architect entry");
            Check(Nodes(_safetyFrame!).OfType<CheckBox>().Single(b=>b.AccessibilityName=="停止到作业区外取料").Disabled,"no work area cannot stop supplies");
            await Click(Action("添加作业区"));Check(_safetyPanel is null&&_tool=="workarea","enter selection tool");
            var start=_world.Camp+new Cell(2,2);var end=start+new Cell(5,3);
            _heightSelected=true;_layerOffset=start.Level-_world.Camp.Level;_digStart=start;_digEnd=end;
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Minus,PhysicalKeycode=Key.Minus});
            Check(_digEnd==end+new Cell(0,0,-1),"cross-level endpoint");
            var mapPoint=GetViewport().GetVisibleRect().Size*new Vector2(.5f,.35f);
            GetViewport().PushInput(new InputEventMouseMotion{Position=mapPoint,GlobalPosition=mapPoint},true);await Frame();
            _digEnd=end+new Cell(0,0,-1);
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});await Frame();
            Check(_safetyDraft?.WorkAreas.Count==1&&_world.Safety.WorkAreas.Count==0,"selection edits draft only");
            Check(_safetyDraft!.WorkAreas[0].Min.Level==start.Level-1&&_safetyDraft.WorkAreas[0].Max.Level==start.Level,"saved volume covers two levels");
            await Click(Action("添加禁行区"));Check(_safetyPanel is null&&_tool=="forbiddenarea","enter forbidden selection");_digStart=start;_digEnd=end;
            HandleMapGesture(new InputEventKey{Pressed=true,Keycode=Key.Escape});
            Check(_digStart is null&&_safetyPanel is not null&&_safetyDraft.ForbiddenAreas.Count==0,"escape aborts incomplete volume and returns to draft");
            await Click(Action("添加禁行区"));
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true});
            Check(_safetyPanel is not null&&_safetyDraft.WorkAreas.Count==1,"right cancel without anchor preserves previous draft");
            await Click(Action("添加禁行区"));
            CommitDigDrag(end,start);await Frame();
            Check(_safetyDraft.ForbiddenAreas.Count==1&&_world.Safety.ForbiddenAreas.Count==0,"reverse corners normalize in draft");
            await Click(Nodes(_safetyFrame!).OfType<CheckBox>().Single(b=>b.AccessibilityName=="停止到作业区外取料"));
            GetWindow().ContentScaleSize=new(960,640);await Frame();
            Check(GetViewport().GetVisibleRect().Encloses(_safetyFrame!.GetGlobalRect()),"window fits small viewport");
            Check(_safetyFrame.GetGlobalRect().Encloses(Action("应用范围").GetGlobalRect()),"apply button fits window");
            if(DisplayServer.GetName()!="headless")
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/safety-areas.png"));
            }
            await Click(Action("应用范围"));
            Check(_safetyPanel is null&&_world.Safety.StopOutsideSupplies&&_world.Safety.ForbiddenAreas.Count==1,"apply commits all rules");
            var loaded=DemoWorld.LoadJson(_world.SaveJson());Check(loaded.Safety.WorkAreas.Single()==_world.Safety.WorkAreas.Single(),"UI state roundtrip");
            ShowSafetyAreas();await Frame();await Click(Nodes(_safetyFrame!).OfType<CheckBox>().Single(b=>b.AccessibilityName=="启用安全限制"));
            _Input(new InputEventKey{Pressed=true,Keycode=Key.Escape});Check(_world.Safety.Enabled&&_safetyPanel is null,"escape discards modified rules");
            FocusLifeAlert(new("safety-job:1","范围限制",2,Location:start+new Cell(0,0,-1)));await Frame();
            Check(ActiveLevel==start.Level-1&&_safetyFrame!.Status.Text.Contains("范围限制"),"alert focuses level and opens adjustment");
            GD.Print("SAFETY_AREAS_UI_OK: accessible real buttons, cross-level selection, reverse corners, cancel, apply, small layout, save and located warning");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
