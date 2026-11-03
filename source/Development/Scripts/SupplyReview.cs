using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewSupplies()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("SUPPLIES_UI: "+why);}
            IEnumerable<Node> Nodes(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var n in Nodes(child))yield return n;}}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Click(BaseButton button)
            {
                for(Node? p=button.GetParent();p is not null;p=p.GetParent())if(p is ScrollContainer scroll)scroll.EnsureControlVisible(button);
                await Frame();Check(button.IsVisibleInTree()&&button.AccessibilityName!="","accessible visible button");var pos=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion{Position=pos,GlobalPosition=pos},true);
                foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=pos,GlobalPosition=pos,ButtonIndex=MouseButton.Left,Pressed=down},true);
                await Frame();
            }
            _clock.Paused=true;_world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_world.Items.Clear();_world.Stockpiles.Clear();_overview.World=_world;
            _foundation=null;_developerMode=false;_creative=false;_inspector.Hide();_buildMenu.Hide();_consolePanel.Hide();_characterEditor.Hide();
            var cell=_world.Camp+new Cell(2,2);var stack=_world.SpawnItem("cloth",4,cell);_world.SetStockpile(cell,cell);
            SelectTool("select");SyncWorld();Act(cell);await Frame();
            await Click(_forbidItemButton);Check(stack.Forbidden&&_forbidItemButton.Text=="恢复使用"&&_selection.Text.Contains("禁用"),"single stack ban and explanation");
            await Click(_forbidItemButton);Check(!stack.Forbidden,"single stack restore");
            await Click(_supplyRuleButton);Check(_supplyFrame is not null,"open selected material rules");
            var number=Nodes(_supplyFrame!).OfType<SpinBox>().Single();number.Value=3;
            var exception=Nodes(_supplyFrame!).OfType<CheckBox>().Single(b=>b.Text.StartsWith("裁绷带"));await Click(exception);
            await Click(Nodes(_supplyFrame!).OfType<Button>().Single(b=>b.Text=="应用规则"));
            Check(_world.SupplyReserves.Single().Minimum==3&&_world.SupplyReserves.Single().AllowedUses.SetEquals(new[]{"recipe:bandages"}),"apply material purpose exception");
            GetWindow().ContentScaleSize=new(640,480);await Frame();
            Check(GetViewport().GetVisibleRect().Encloses(_supplyFrame!.GetGlobalRect()),"supply window small layout");
            Check(_supplyFrame.GetGlobalRect().Encloses(Nodes(_supplyFrame).OfType<Button>().Single(b=>b.Text=="应用规则").GetGlobalRect()),"apply remains visible");
            if(DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/supply-rules.png"));}
            number.Value=0;_Input(new InputEventKey{Pressed=true,Keycode=Key.Escape});Check(_supplyFrame is null&&_world.SupplyReserves.Single().Minimum==3,"escape discards unapplied override");
            ShowSupplyRules("cloth");await Frame();Nodes(_supplyFrame!).OfType<SpinBox>().Single().Value=0;
            await Click(Nodes(_supplyFrame!).OfType<Button>().Single(b=>b.Text=="应用规则"));Check(_world.SupplyReserves.Count==0,"zero explicitly restores general use");CloseSupplyRules();
            var zone=_world.EditableStorageAt(cell)!;ShowStorage(zone);await Frame();
            Check(_storagePanel is GameWindowFrame frame&&GetViewport().GetVisibleRect().Encloses(frame.GetGlobalRect()),"storage uses shared window and fits");
            Check(Nodes(_storagePanel!).OfType<Label>().Any(l=>l.Text.Contains("已存 4 / 100")),"capacity and reservation displayed");
            await Click(Nodes(_storagePanel!).OfType<CheckBox>().Single(b=>b.AccessibilityName=="布允许入库"));
            await Click(Nodes(_storagePanel!).OfType<Button>().Single(b=>b.Text=="应用设置"));Check(!zone.Allowed.Contains("cloth")&&_world.Available("cloth")==4,"storage filter does not ban usage");
            ShowStorage(zone);await Frame();_Input(new InputEventKey{Pressed=true,Keycode=Key.Escape});Check(_storagePanel is null&&_selectedCell==cell,"storage escape preserves underlying selection");
            var loaded=DemoWorld.LoadJson(_world.SaveJson());Check(loaded.Total("cloth")==4&&!loaded.StorageZones.Single().Allowed.Contains("cloth"),"UI current save roundtrip");
            GD.Print("SUPPLIES_UI_OK: real ban/restore clicks, material reserve and purpose, explicit override, capacity, acceptance filter, small windows, Esc and save");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
