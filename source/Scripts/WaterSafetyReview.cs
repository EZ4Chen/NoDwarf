using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CheckWaterSafetyUi()
    {
        try
        {
            void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            _clock.Paused=true;_world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _foundation=null;_lifeNoticeKey="";_terrainRevision=-1;
            var c=new Cell(40,40,-3);_world.Terrain.Excavate(c+new Cell(1,0));_world.Fluids.Add(c+new Cell(1,0),900);
            _world.QueueDig(c,announce:false);var job=_world.Jobs.Single();
            _world.RefreshLifeAlerts();SyncWorld();RefreshUi();await Frame();
            var notice=_lifeNotices.GetChildren().OfType<Button>().First(b=>b.Text.Contains("采掘"));
            Check(notice.AccessibilityName!="","Water notice accessible");notice.EmitSignal(BaseButton.SignalName.Pressed);await Frame();
            Check(ActiveLevel==c.Level,"Warning locates underground work level");
            var dialog=GetChildren().OfType<ConfirmationDialog>().Last(d=>d.Visible);
            Check(dialog.DialogText.Contains("困住居民")&&dialog.GetOkButton().AccessibilityName!="","Continue describes risk and has accessible name");
            dialog.Hide();dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);await Frame();Check(job.WaterPaused,"Cancel dialog preserves pause");
            ShowWaterDigWarning(job.Id);dialog=GetChildren().OfType<ConfirmationDialog>().Last(d=>d.Visible);
            dialog.Hide();dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);await Frame();Check(!job.WaterPaused,"Confirm resumes exactly selected excavation");
            var loaded=DemoWorld.LoadJson(_world.SaveJson());Check(loaded.Jobs.Single().WaterPaused,"UI confirmation does not survive load");
            _world=loaded;_overview.World=_world;ShowWaterDigWarning(job.Id);dialog=GetChildren().OfType<ConfirmationDialog>().Last(d=>d.Visible);
            dialog.Hide();dialog.EmitSignal(AcceptDialog.SignalName.CustomAction,"cancel-dig");await Frame();Check(_world.Jobs.Count==0&&_world.NaturalSolid(c),"Cancel excavation preserves terrain");
            GD.Print("WATER_SAFETY_UI_OK: accessible notice, underground focus, consequence prompt, keep paused, confirm, save recheck and cancel excavation");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
