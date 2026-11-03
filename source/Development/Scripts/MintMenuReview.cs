using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CheckMintMenus()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            void Click(Control c){var p=c.GetGlobalRect().GetCenter();GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=down},true);}
            async Task Snap(string name){await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng("res://.local/"+name+".png");}
            _clock.Paused=true;TogglePanel(_buildMenu);ShowBuildPage("结构");await Frame();
            Button Named(string name)=>_buildMenu.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.AccessibilityName==name);
            Check(_mintArchitectDetail.Size.X>=338,"Mint information card width lost");
            Check(_architectSearch.Size.X<180,"Search must stay above categories, not span the window");
            Click(Named("显示或隐藏建筑详情"));await Frame();Check(!_mintArchitectDetail.Visible,"Information toggle failed");
            Click(Named("显示或隐藏建筑详情"));await Frame();Check(_mintArchitectDetail.Visible,"Information card did not reopen");
            GetViewport().PushInput(new InputEventKey{Keycode=Key.F,PhysicalKeycode=Key.F,MetaPressed=true,Pressed=true},true);await Frame();
            Check(_architectSearch.HasFocus(),"Command+F must focus menu search");_architectSearch.ReleaseFocus();
            Check(_buildMenu.GetGlobalRect().Encloses(_toolButtons["StoneBricks"].GetGlobalRect()),"Building grid overflow");
            PinArchitectCategory("家具");Check(_architectCategories.GetChild(1)==_categoryRows["家具"],"Pinned category did not move to top");PinArchitectCategory("家具");
            ShowBuildPage("全部");_architectSortMode=2;_architectSort.Select(2);FilterArchitect();await Frame();
            Check(_architectSorted.GetChildren().OfType<Button>().First(b=>b.Visible)==_toolButtons["Anvil"],"Ported descending durability sort failed");
            Check(!_toolButtons["dig"].Visible,"Global stat sort must exclude commands without that stat");
            await Snap("mint-build-sorted");
            _architectSortMode=0;_architectSort.Select(0);ShowBuildPage("结构");await Frame();
            _toolButtons["StoneBricks"].EmitSignal(Control.SignalName.MouseEntered);
            await Snap("mint-build-grid");
            SetArchitectList(true);await Frame();Check(_buildMenu.GetGlobalRect().Encloses(_toolButtons["Wall"].GetGlobalRect()),"Building list overflow");
            _architectSearch.GrabFocus();_architectSearch.Text="楼梯";_architectSearch.EmitSignal(LineEdit.SignalName.TextChanged,_architectSearch.Text);await Frame();
            Check(_toolButtons["PlankStairs"].IsVisibleInTree()&&!_toolButtons["Furnace"].IsVisibleInTree(),"Search must cross categories");
            var focus=_focus;GetViewport().PushInput(new InputEventKey{PhysicalKeycode=Key.W,Keycode=Key.W,Pressed=true},true);await Frame();GetViewport().PushInput(new InputEventKey{PhysicalKeycode=Key.W,Keycode=Key.W,Pressed=false},true);
            Check(_focus==focus,"Typing in search moved camera");
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventKey{PhysicalKeycode=Key.Escape,Keycode=Key.Escape,Pressed=down},true);
            Check(GetViewport().GuiGetFocusOwner() is not LineEdit&&_buildMenu.Visible,"Escape must leave search before closing menu");
            _architectSearch.Text="不存在的项目";_architectSearch.EmitSignal(LineEdit.SignalName.TextChanged,_architectSearch.Text);await Frame();Check(_architectEmpty.Visible,"No-results message missing");
            _architectSearch.Text="";ShowBuildPage("楼梯");SetArchitectList(false);await Frame();
            _architectAutoClose.ButtonPressed=true;Click(_toolButtons["PlankStairs"]);await Frame();
            Check(!_buildMenu.Visible&&_tool=="PlankStairs"&&GetViewport().GuiGetFocusOwner() is not LineEdit,"Selecting tool must return to map");
            await VerifyProductionBillsUi();
            GD.Print("MINT_MENU_OK: ported attribute sorting and category pinning, category/grid/list bounds, cross-category search, input focus, auto close, recipe ingredient search and target handoff");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
