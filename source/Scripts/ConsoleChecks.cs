using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void CheckUnifiedConsole()
    {
        try
        {
            void Check(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);}
            void KeyPress(Key key,bool shift=false,bool echo=false)
            {GetViewport().PushInput(new InputEventKey{Pressed=true,PhysicalKeycode=key,Keycode=key,ShiftPressed=shift,Echo=echo},true);}
            bool LuaOutput(string text)=>_sessionLog.Snapshot().Any(r=>r.Category=="Lua"&&r.Message==text);
            _developerMode=false;_clock.Paused=true;CheckDeveloperConsole();
            KeyPress(Key.Quoteleft);await Frame();
            Check(_consolePanel.Visible&&GetViewport().GuiGetFocusOwner()==_commandInput,"Tilde did not open/focus console");
            KeyPress(Key.Quoteleft,echo:true);Check(_consolePanel.Visible&&_commandInput.Text=="wasd","Held tilde changed state/text");
            _commandInput.Text="status";KeyPress(Key.Enter);await Frame();
            Check(_commandInput.Text==""&&_commandHistory.Last()=="status","Enter did not execute console input");
            KeyPress(Key.Up);Check(_commandInput.Text=="status","History up failed");KeyPress(Key.Down);
            var focus=_focus;var paused=_clock.Paused;KeyPress(Key.W);KeyPress(Key.Space);await Frame();
            Check(_focus==focus&&_clock.Paused==paused,"Typing leaked into game shortcuts");_commandInput.Clear();
            var click=_buildTab.GetGlobalRect().GetCenter();bool menu=_buildMenu.Visible;
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=click,GlobalPosition=click,ButtonIndex=MouseButton.Left,Pressed=down},true);
            await Frame();Check(_buildMenu.Visible==menu,"Console allowed clicks on underlying game buttons");_commandInput.GrabFocus();
            KeyPress(Key.Quoteleft,shift:true);await Frame();Check(!_consolePanel.Visible&&!_consoleShield.Visible,"Shift-tilde did not close");
            KeyPress(Key.Quoteleft);await Frame();
            GD.Print("CONSOLE_NATIVE_PRINT_PROBE");GD.PushWarning("CONSOLE_EXPECTED_WARNING_PROBE");GD.PushError("CONSOLE_EXPECTED_ERROR_PROBE");
            Console.WriteLine("CONSOLE_MANAGED_PRINT_PROBE");Console.Error.WriteLine("CONSOLE_EXPECTED_MANAGED_ERROR_PROBE");
            await Task.Run(()=>GD.PushWarning("CONSOLE_THREAD_WARNING_PROBE"));
            _diagnostics.Drain();
            foreach(var (level,marker) in new[]{("INFO","CONSOLE_NATIVE_PRINT_PROBE"),("WARN","CONSOLE_EXPECTED_WARNING_PROBE"),
                ("ERROR","CONSOLE_EXPECTED_ERROR_PROBE"),("INFO","CONSOLE_MANAGED_PRINT_PROBE"),("ERROR","CONSOLE_EXPECTED_MANAGED_ERROR_PROBE"),("WARN","CONSOLE_THREAD_WARNING_PROBE")})
                Check(_sessionLog.Snapshot().Any(r=>r.Level==level&&r.Message.Contains(marker)),"Missing captured log: "+marker);
            RunCommand("filter error");Check(_consoleOutput.Text.Contains("CONSOLE_EXPECTED_ERROR_PROBE")&&!_consoleOutput.Text.Contains("CONSOLE_NATIVE_PRINT_PROBE"),"Error filter failed");RunCommand("filter all");
            RunCommand("lua = 2+3");Check(LuaOutput("5"),"Lua expression failed");
            RunCommand("lua n=7");RunCommand("lua print(n)");Check(LuaOutput("7"),"Lua variables did not persist");
            RunCommand("lua print(os==nil and io==nil and require==nil and load==nil and luanet==nil)");Check(LuaOutput("true"),"Lua exposed unsupported libraries");
            int before=_world.Buildings.Count;RunCommand("lua game.command(\"place nodwarf:anvil 21 20 -3\")");
            Check(_world.Buildings.Count==before,"Lua bypassed developer guard");
            RunCommand("dev on");RunCommand("lua game.command(\"dig 21 20 -3\"); game.command(\"place nodwarf:anvil 21 20 -3\")");
            Check(_world.Buildings.ContainsKey(new Cell(21,20,-3)),"Lua game command did not modify world");
            _selectedCell=new Cell(21,20,-3);RunCommand("lua local c=game.selected(); print(c.x+c.z+c.y)");Check(LuaOutput("38"),"Lua selection snapshot failed");
            RunCommand("lua for i=1,17 do game.command(\"status\") end");
            Check(_lua is null&&_sessionLog.Snapshot().Any(r=>r.Message.Contains("最多调用 16")),"Lua command limit failed");
            var timer=System.Diagnostics.Stopwatch.StartNew();RunCommand("lua while true do end");
            Check(timer.Elapsed.TotalSeconds<2&&_lua is null,"Infinite Lua loop was not stopped");
            RunCommand("lua = 8*8");Check(LuaOutput("64"),"Lua did not recover after interruption");
            RunCommand("lua function (");Check(_lua is null,"Lua syntax failure did not recover");
            RunCommand("lua n=99");RunCommand("lua reset");Check(_lua is null,"Lua reset failed");
            RunCommand("stats on");Check(_diagnosticsPanel.Visible,"Stats command failed");RunCommand("stats off");
            RunCommand("water selected 250");RunCommand("drain selected");
            Check(_world.Fluids.Amount(new Cell(21,20,-2))==0,"Selected-cell water/drain commands failed");
            RunCommand("clear");Check(!_consoleOutput.Text.Contains("CONSOLE_EXPECTED_ERROR_PROBE"),"Clear did not clear console view");
            Check(File.ReadAllText(_sessionLog.FilePath).Contains("CONSOLE_EXPECTED_ERROR_PROBE"),"Clear erased disk log");
            RunCommand("lua sentinel=99");Check(_lua is not null,"Lua state was not created before world switch");
            _world=new DemoWorld(flat:true);BindWorldLog();Check(_lua is null,"World switch retained Lua state");
            _overview.World=_world;CreatePeople();SyncWorld();
            foreach(var name in new[]{"single","two","underground"})
            {
                _world=new DemoWorld(flat:true);_overview.World=_world;BindWorldLog();CreatePeople();
                RunCommand("demo "+name);Check(_world.Jobs.Count>0,"Removed demo button has no working command: "+name);
            }
            IEnumerable<Node> Descendants(Node node)
            {foreach(var child in node.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
            Check(!Descendants(this).OfType<Button>().Any(b=>b.Text.Contains("示例")||b.Text=="启用居民生产"||b.TooltipText is "开发工具 [F1]" or "控制台 [F2]" or "性能统计"),"Debug buttons remain in game UI");
            _foundation=null;_cutaway=false;_heightSelected=false;_layerOffset=0;_camera.Size=38;FocusCamp();SyncWorld();
            RunCommand("help");RunCommand("lua print(\"Lua 已就绪\", 6*7)");RefreshDeveloperUi();await Frame();
            Check(_consolePanel.GetGlobalRect().Encloses(_commandInput.GetGlobalRect()),"Console input extends beyond panel");
            if(OS.GetCmdlineUserArgs().Contains("--console-preview-test"))
            {
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/unified-console.png"));
            }
            GD.Print("CONSOLE_OK: tilde, focus, history, input isolation, engine/managed/thread logs, filters, Lua bridge/limits/recovery and persistence");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
