using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureProductionTargets()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            var origin=_world.Camp;_world.DeveloperPlace(origin+new Cell(0,0),BuildKind.Workbench);_world.DeveloperPlace(origin+new Cell(2,0),BuildKind.Loom);_world.DeveloperPlace(origin+new Cell(4,0),BuildKind.Anvil);
            _world.SetProductionTarget("cloth",12);_world.SetProductionTarget("shirt",3);_world.SetProductionTarget("shield",2);
            _foundation=null;_heightSelected=false;_layerOffset=0;_focus=WorldPosition(origin+new Cell(2,1));_camera.Size=14;CreatePeople();PositionCamera();SetViewMode(0);
            _buildMenu.Hide();_selectionPanel.Hide();_worldMenu.Hide();_inspector.Show();RefreshUi();SyncWorld();
            int shirt=Array.FindIndex(ProductionCatalog.All,r=>r.Id=="shirt");_targetRecipe.Select(shirt);_targetCount.Value=5;_applyProductionTarget.EmitSignal(Godot.Button.SignalName.Pressed);
            if(_world.ProductionOrders.Single(o=>o.Recipe=="shirt").Target!=5)throw new InvalidOperationException("Target UI did not apply");
            _productionTargetRows["shirt"].Pause.EmitSignal(Godot.Button.SignalName.Pressed);if(_world.ProductionOrders.Single(o=>o.Recipe=="shirt").Enabled)throw new InvalidOperationException("Pause target UI failed");
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;RefreshUi();SyncWorld();
            if(_productionTargetRows["shirt"].Pause.Text!="恢复补产")throw new InvalidOperationException("Target UI did not restore pause");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            _targetCount.GetLineEdit().GrabFocus();_targetCount.GetLineEdit().Text="7";
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventKey{Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=down},true);
            if(_targetCount.Value!=7)throw new InvalidOperationException("Enter did not commit target number");
            _applyProductionTarget.GrabFocus();
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventKey{Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=down},true);
            if(_world.ProductionOrders.Single(o=>o.Recipe=="shirt").Target!=7)throw new InvalidOperationException("Keyboard did not activate production target button");
            _applyProductionTarget.ReleaseFocus();_targetCount.Value=5;_applyProductionTarget.EmitSignal(Godot.Button.SignalName.Pressed);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(_terrainView.PendingMeshCount>0)
            {
                if(timer.Elapsed.TotalSeconds>60)throw new TimeoutException("Production preview terrain did not finish loading");
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            var scroll=_targetRows.GetParent().GetParent<ScrollContainer>();scroll.ScrollVertical=(int)_targetRecipe.Position.Y-70;
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(!_inspector.GetGlobalRect().Encloses(_targetRecipe.GetGlobalRect()))throw new InvalidOperationException("Production target selector overflow");
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/production-targets.png"))!=Error.Ok)throw new IOException("Target screenshot failed");
            _targetCount.Value=0;_applyProductionTarget.EmitSignal(Godot.Button.SignalName.Pressed);
            if(_world.ProductionOrders.Single(o=>o.Recipe=="shirt").Target!=1)throw new InvalidOperationException("Zero must clamp to a valid target, not silently delete it");
            var remove=_productionTargetRows["shirt"].Row.GetChildren().OfType<HBoxContainer>().Single().GetChildren().OfType<Button>().Single(b=>b.Text=="移除目标");
            remove.EmitSignal(Godot.Button.SignalName.Pressed);if(_world.ProductionOrders.Any(o=>o.Recipe=="shirt"))throw new InvalidOperationException("Explicit remove target failed");
            if(_targetRecipe.AccessibilityName!="生产物品"||_targetCount.GetLineEdit().AccessibilityName!="目标库存"||_applyProductionTarget.FocusMode!=Control.FocusModeEnum.All)
                throw new InvalidOperationException("Production form accessible labels or keyboard focus missing");
            GD.Print("PRODUCTION_TARGET_UI_OK: workbench, recipe selector, target application, pause, save/load, deletion and panel bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
