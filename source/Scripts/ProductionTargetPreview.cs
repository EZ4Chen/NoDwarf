using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureProductionTargets()
    {
        try{await VerifyProductionBillsUi();GD.Print("PRODUCTION_BILL_UI_OK: anchored layout, real mouse add/pause/reorder/delete, modes, detail filters, save/load");GetTree().Quit();}
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private async Task VerifyProductionBillsUi()
    {
        void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
        async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
        void Click(Control control){var p=control.GetGlobalRect().GetCenter();GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=down,Position=p,GlobalPosition=p},true);}
        async Task Snap(string file){await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng("res://.local/"+file+".png");}
        _clock.Paused=true;_world=new DemoWorld(flat:true);_overview.World=_world;_terrainRevision=-1;
        _focus=WorldPosition(_world.Camp);_camera.Size=20;PositionCamera();CreatePeople();SyncWorld();
        _world.Buildings[new(5,9)]=new(new(5,9),BuildKind.Loom);_world.SpawnItem("cotton",16,new(2,10));
        _buildMenu.Hide();_selectionPanel.Hide();_worldMenu.Hide();_inspector.Show();LayoutProductionPanel();RefreshUi();await Frame();
        _recipeSearch.Text="棉";_recipeSearch.EmitSignal(LineEdit.SignalName.TextChanged,"棉");await Frame();
        _recipeButtons["cloth"].GrabFocus();await Frame();Check(_world.ProductionOrders.Count==0,"Focus must not add a bill");
        Click(_recipeButtons["cloth"]);await Frame();Check(_world.ProductionOrders.Count==1,"One click must add exactly one bill");
        var first=_world.ProductionOrders[0];Check(first.Mode=="count"&&first.Target==1,"New bill starts as one batch");
        Click(_billRows[first.Id].Pause);await Frame();Check(!first.Enabled,"S must suspend");Click(_billRows[first.Id].Pause);await Frame();Check(first.Enabled,"S must resume");
        var second=_world.AddProductionOrder("shirt","maintain");second.Target=11;var third=_world.AddProductionOrder("bandages","forever");RefreshOrders();await Frame();
        Click(_billRows[third.Id].Up);await Frame();Check(_world.ProductionOrders[1].Id==third.Id,"Up arrow must change queue order");
        _recipeSearch.Text="";FilterRecipes();RefreshOrders();await Frame();
        Check(_inspector.Position.X==0&&Math.Abs(_inspector.GetRect().End.Y-(_inspector.GetParent<Control>().Size.Y-40))<2,"Bill window must touch lower-left above bottom bar");
        Check(!_inspector.FindChildren("*","TabContainer",true,false).Any(),"Invented order/target tabs must be removed");
        await Snap("production-bills");
        OpenBillDetails(first);await Frame();
        var check=_billDetails!.FindChildren("*","CheckBox",true,false).OfType<CheckBox>().Single(b=>b.AccessibilityName=="允许材料：棉絮");Click(check);await Frame();Check(first.AllowedIngredients is not null&&!first.AllowedIngredients.Contains("cotton"),"Material permission must affect bill");
        first.AllowedIngredients=null;first.WorkerId=_world.Workers[0].Id;first.Radius=20;_world.ProductionOrderChanged(first);OpenBillDetails(first);await Frame();
        var previousFocus=_focus;GetViewport().PushInput(new InputEventKey{PhysicalKeycode=Key.W,Keycode=Key.W,Pressed=true},true);await Frame();GetViewport().PushInput(new InputEventKey{PhysicalKeycode=Key.W,Keycode=Key.W,Pressed=false},true);
        Check(_focus==previousFocus,"Bill details must prevent background camera movement");
        Check(_billDetails!.GetGlobalRect().Encloses(_billDetails.FindChildren("*","HSlider",true,false).OfType<HSlider>().Single().GetGlobalRect()),"Details overflow");
        await Snap("production-bill-details");
        _billDetails.Size=new(640,440);await Frame();
        var detailFrame=(GameWindowFrame)_billDetails;
        Check(detailFrame.Size.X<=640&&detailFrame.Size.Y<=440,"Small details must fit requested bounds");
        Check(detailFrame.GetGlobalRect().Encloses(detailFrame.Footer.GetGlobalRect()),"Small details footer overflow");
        Check(detailFrame.FindChildren("*","GridContainer",true,false).OfType<GridContainer>().Single().Columns==1,"Small details should stack sections");
        await Snap("production-template-small");
        GetViewport().PushInput(new InputEventKey{Keycode=Key.Escape,Pressed=true},true);await Frame();
        Check(_billDetails is null&&_inspector.Visible,"Esc must close only the topmost production window");

        _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;RefreshUi();await Frame();
        Check(_world.ProductionOrders[0].WorkerId!=""&&_world.ProductionOrders[0].Radius==20&&_world.ProductionOrders[1].Mode=="forever","Bill constraints/order must survive reload");
        var remove=_world.ProductionOrders.Last();ConfirmBillDelete(remove);await Frame();
        var dialog=GetChildren().OfType<ConfirmationDialog>().Last();dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);await Frame();
        Check(!_world.ProductionOrders.Any(o=>o.Id==remove.Id),"Delete confirmation must remove bill");
    }
}
