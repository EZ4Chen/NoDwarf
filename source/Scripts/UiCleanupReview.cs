using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CheckUiCleanup()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Snap(string name){await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng("res://.local/"+name+".png");}
            _world=CombatChecks.Fixture();_overview.World=_world;_terrainRevision=-1;_clock.Paused=true;CreatePeople();SyncWorld();RefreshUi();await Frame();
            _residentDetails.Hide();SelectResident(0);RefreshUi();Check(_residentDetails.Visible,"Person tab must return after object selection");OpenPersonPage(2);await Frame();
            var menu=_residentBody.FindChildren("*","MenuButton",true,false).OfType<MenuButton>().First();
            menu.GetPopup().EmitSignal(PopupMenu.SignalName.AboutToPopup);
            Check(menu.GetPopup().IsItemDisabled(1)&&menu.GetPopup().IsItemDisabled(2),"Healthy resident must not offer fracture fixation or prosthesis");
            Check(menu.GetPopup().GetItemTooltip(1)!="","Disabled care action must explain why");
            TogglePanel(_combatPanel);RefreshUi();await Frame();
            Check(!DossierOpen&&_combatPanel.Visible&&!_inspector.Visible,"Defense must replace resident dossier");
            _developerMode=false;RefreshUi();Check(!_raidTools.Visible,"Test raids leaked into normal UI");
            _world.Enemies.Clear();RefreshUi();Check(_locateEnemy.Disabled,"Empty enemy locator must be disabled");
            _developerMode=true;RefreshUi();Check(_raidTools.Visible,"Developer raid tools missing");
            TogglePanel(_inspector);RefreshUi();await Frame();Check(!_combatPanel.Visible&&_inspector.Visible,"Production must replace defense");
            var production=_productionTabs;
            await Snap("ui-cleanup-orders");
            production.CurrentTab=1;await Frame();
            SelectRecipe("sword");
            await Snap("ui-cleanup-recipes");
            production.CurrentTab=2;await Snap("ui-cleanup-targets");
            Check(_seed.MinValue==int.MinValue&&_seed.MaxValue==int.MaxValue,"Map seed range differs from startup");
            FocusLifeAlert(new("water","饮水不足",2));await Frame();Check(!_worldMenu.Visible&&_inspector.Visible&&_actionFeedback.Visible,"Water alert routed to unrelated menu");
            TogglePanel(_buildMenu);SelectTool("dig");await Frame();Check(_architectHint.Text.Contains("左键"),"Dig instructions hidden");
            _buildMenu.Hide();await Frame();Check(_hint.Visible,"Contextual tool hint hidden");
            SelectTool("select");await Frame();Check(!_hint.Visible,"Idle tool hint must not occupy space");
            var all=GetTree().Root.FindChildren("*","Button",true,false).OfType<Button>().Select(b=>b.Text).ToArray();
            foreach(var stale in new[]{"详细资料","收起","应用并保存","恢复普通工作"})Check(!all.Contains(stale),"Stale/duplicate action: "+stale);
            GD.Print("UI_CLEANUP_OK: panel exclusivity, developer tools, contextual care, alert routing, tool hints, seed range and production layouts");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
