using Godot;
using NoDwarf.Simulation;
using System.Text.Json;
namespace NoDwarf;
public partial class Startup
{
    internal static string? PreviewSave;
    internal static long PreviewXp;
    private async void RunStartupPreview()
    {
        try
        {
            if(PreviewSave is not null)
            {
                ShowSaves();
                if(!_savePaths.Contains(PreviewSave))throw new InvalidOperationException("Saved colony is missing from title save list");
                await ReadWorld(PreviewSave);return;
            }
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Snap(string name)
            {
                await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var viewport=GetViewport().GetVisibleRect();
                foreach(var button in _actions.Values)
                {
                    Control bounds=button;
                    for(Node? ancestor=button.GetParent();ancestor is not null;ancestor=ancestor.GetParent())
                        if(ancestor is ScrollContainer scroll){bounds=scroll;break;}
                    if(button.IsVisibleInTree()&&!viewport.Encloses(bounds.GetGlobalRect()))throw new InvalidOperationException("Startup button/container outside screen: "+button.Text);
                }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+name+".png"));
            }
            async Task Click(string id)
            {
                await Frame();var point=_actions[id].GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false},true);await Frame();
            }
            await Snap("startup-home");await Click("new");
            if(!_options.ContainsKey("moisture"))throw new InvalidOperationException("New game button must open world settings");
            _seedInput.Text="invalid";_seedInput.EmitSignal(LineEdit.SignalName.TextChanged,_seedInput.Text);if(!_next.Disabled)throw new InvalidOperationException("Invalid seed must block generation");
            _seedInput.Text="23519";_seedInput.EmitSignal(LineEdit.SignalName.TextChanged,_seedInput.Text);_options["moisture"].Select(3);_options["moisture"].EmitSignal(OptionButton.SignalName.ItemSelected,3L);
            await Snap("startup-settings");
            _options["cavernSize"].GrabFocus();await Frame();await Snap("startup-settings-underground");
            var formScroll=_options["cavernSize"].GetParent().GetParent().GetParent<ScrollContainer>();
            if(!formScroll.GetGlobalRect().Encloses(_options["cavernSize"].GetGlobalRect()))throw new InvalidOperationException("Keyboard focus cannot reach underground parameters");
            foreach(var setting in new[]{("minerals",2),("aquifers",2),("caverns",2),("soil",2)})
            {_options[setting.Item1].Select(setting.Item2);_options[setting.Item1].EmitSignal(OptionButton.SignalName.ItemSelected,(long)setting.Item2);}
            await Click("next");
            if(_settings.Seed!=23519||_settings.Moisture!=1||_settings.Minerals!=2||_settings.Aquifers!=2||_settings.CavernLayers!=2||_settings.SoilDepth!=6||_enter.Disabled)throw new InvalidOperationException("Options were not applied to atlas");
            var keyboardStart=_settings;_atlas.GrabFocus();await Frame();
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventKey{Keycode=Key.Right,PhysicalKeycode=Key.Right,Pressed=down},true);
            await Frame();
            if(_settings.SiteX!=Math.Min(keyboardStart.SiteX+1,_settings.Width-1)||_settings.SiteY!=keyboardStart.SiteY||string.IsNullOrEmpty(_atlas.AccessibilityDescription))
                throw new InvalidOperationException("Keyboard atlas selection or accessible description failed");
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventKey{Keycode=Key.Tab,PhysicalKeycode=Key.Tab,Pressed=down},true);
            await Frame();if(_atlas.HasFocus())throw new InvalidOperationException("Tab cannot leave atlas");
            _settings=keyboardStart;UpdateSite();
            GD.Print("STARTUP_ACCESS_OK: keyboard map selection, Tab exit and selected-region description");
            var water=(from x in Enumerable.Range(0,_settings.Width) from y in Enumerable.Range(0,_settings.Height) where !_settings.Tile(x,y).Land select (x,y)).First();
            _atlas.Selected!(water.x,water.y);if(!_enter.Disabled)throw new InvalidOperationException("Water must reject settlement");
            await Click("recommend");await Snap("startup-atlas");
            var manual=(from x in Enumerable.Range(0,_settings.Width) from y in Enumerable.Range(0,_settings.Height) where _settings.Tile(x,y).Land&&(x!=_settings.SiteX||y!=_settings.SiteY) select (x,y)).First();
            _atlas.Selected!(manual.x,manual.y);var chosen=_settings;
            await Click("back");await Click("next");
            if(_settings!=chosen)throw new InvalidOperationException("Returning through unchanged parameters lost manual site");
            await Click("back");_options["temperature"].Select(3);_options["temperature"].EmitSignal(OptionButton.SignalName.ItemSelected,3L);
            var recommended=_settings.WithRecommendedSite();await Click("next");
            if(_settings!=recommended)throw new InvalidOperationException("Changed world parameters must refresh recommendation");
            GD.Print("STARTUP_SELECTION_OK: manual site survives round trip; changed parameters refresh recommendation");
            ShowSaves();await Snap("startup-loads");
            Directory.CreateDirectory(SaveDirectory);var bad=Path.Combine(SaveDirectory,"corrupt.json");File.WriteAllText(bad,"{broken");
            await ReadWorld(bad);if(_busy||!_message.Text.Contains("无法读取"))throw new InvalidOperationException("Corrupt save must keep the menu usable");
            File.Delete(bad);
            var sample=new DemoWorld(chosen.LocalSeed,region:chosen.Site.Region,generation:chosen);
            var state=new Main.SessionState{WorldState=sample.CaptureSnapshot(),Paused=true,Speed=3,Zoom=30,FocusY=sample.Camp.Level};
            string fixture=Path.Combine(SaveDirectory,"fixture.json");SaveFiles.Write(fixture,JsonSerializer.Serialize(state,DemoWorld.SaveOptions));
            var prepared=Main.PrepareSession(fixture);if(prepared.Clock.Speed!=3||prepared.World.Terrain.Generation!=chosen)throw new InvalidOperationException("Title save reader loses session/world settings");
            SaveFiles.Write(fixture,JsonSerializer.Serialize(state,DemoWorld.SaveOptions));
            string second=Path.Combine(SaveDirectory,"another-colony.json");SaveFiles.Write(second,JsonSerializer.Serialize(state,DemoWorld.SaveOptions));SaveFiles.Write(second,JsonSerializer.Serialize(state,DemoWorld.SaveOptions));
            ShowSaves();await Snap("startup-save-list");
            foreach(string backup in new[]{fixture+".bak",second+".bak"})
            {int index=_savePaths.IndexOf(backup);if(index<0||!_saveList!.GetItemText(index).Contains(Path.GetFileNameWithoutExtension(backup[..^4])+" · 上一次备份"))throw new InvalidOperationException("Backup label lost colony identity");}
            GD.Print("STARTUP_BACKUPS_OK: multiple backups identify their own colonies");
            GD.Print("STARTUP_PAGES_OK: real menu clicks, seed validation, parameter retention, ocean rejection, save list and corrupt-save recovery");
            _settings=chosen;ShowAtlas();await EnterWorld();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
public partial class Main
{
    private async Task CheckStartupExitAndSaveIsolation()
    {
        string active=SavePath;var currentWorld=_world;
        if(File.Exists(active)||ReadSavePath!=active)throw new InvalidOperationException("New colony must resolve its own unsaved path");
        RequestLoadSession();if(_world!=currentWorld||_loadDialog?.Visible==true)throw new InvalidOperationException("Unsaved colony F9 must not load another colony");
        if(ReadSavePath!=active)throw new InvalidOperationException("Read must use the active colony slot");
        _clock.Paused=false;GetWindow().EmitSignal(Window.SignalName.CloseRequested);
        if(GetTree().AutoAcceptQuit||_titleDialog?.Visible!=true||!_clock.Paused||_titleDialog.Title!="退出游戏")throw new InvalidOperationException("Window close bypassed save confirmation");
        var dialog=_titleDialog;GetWindow().EmitSignal(Window.SignalName.CloseRequested);
        if(_titleDialog!=dialog)throw new InvalidOperationException("Repeated close replaced pending confirmation");
        dialog.Hide();dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        if(_clock.Paused)throw new InvalidOperationException("Cancel close must resume previously running simulation");
        _clock.Paused=true;RequestTitleScreen();_titleDialog!.Hide();_titleDialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        if(!_clock.Paused)throw new InvalidOperationException("Cancel must retain previously paused simulation");
        string blocked=Path.Combine(Path.GetDirectoryName(active)!,"save-target-is-directory");Directory.CreateDirectory(blocked);
        _activeSavePath=blocked;_clock.Paused=false;
        GetWindow().EmitSignal(Window.SignalName.CloseRequested);_titleDialog!.Hide();_titleDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        if(_saveTask is {} failedSave)await failedSave;
        if(_clock.Paused||GetTree().CurrentScene!=this||_world!=currentWorld||File.Exists(active))throw new InvalidOperationException("Save failure must keep unsaved colony running");
        _activeSavePath=active;Directory.Delete(blocked);_clock.Paused=true;
        GD.Print("STARTUP_EXIT_OK: native close signal, repeated requests, cancel pause restoration, failed save retention and active save isolation");
    }
    private async void CheckStartupArrival()
    {
        try
        {
            if(_world.Terrain.Generation is null||_world.Workers.Count!=3||_developerMode||_creative||!_clock.Paused||_residentButtons.Count!=3)throw new InvalidOperationException("Normal startup must enter a real colony with 3 residents and no developer mode");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/startup-arrival.png"));
            if(Startup.PreviewSave is null)
            {
                await CheckStartupExitAndSaveIsolation();
                _world.Workers[0].Person.Earn("mining",123);Startup.PreviewXp=_world.Workers[0].Person.GrowthXp;
                Startup.PreviewSave=SavePath;RequestTitleScreen();_titleDialog!.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            }
            else
            {
                if(_world.Workers[0].Person.GrowthXp!=Startup.PreviewXp||_world.Terrain.Generation!.Seed!=23519)throw new InvalidOperationException("Title loading changed colony progress");
                _world.Workers[0].Person.Earn("mining",17);long finalXp=_world.Workers[0].Person.GrowthXp;
                GetWindow().EmitSignal(Window.SignalName.CloseRequested);
                _titleDialog!.Hide();_titleDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                if(_saveTask is {} finalSave)await finalSave;
                if(PrepareSession(SavePath).World.Workers[0].Person.GrowthXp!=finalXp)throw new InvalidOperationException("Save-and-quit lost progress");
                GD.Print("STARTUP_UI_OK: title → settings → atlas → colony → save/return → title load → save/quit; progress and world settings restored");
            }
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
