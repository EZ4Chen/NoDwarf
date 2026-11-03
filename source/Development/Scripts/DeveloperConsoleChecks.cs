using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private void CheckDeveloperConsole()
    {
        _world=new DemoWorld(flat:true);_overview.World=_world;BindWorldLog();CreatePeople();
        RunCommand("residents off");
        if(_world.Workers.Count!=3)throw new InvalidOperationException("Developer guard failed");
        RunCommand("dev on");RunCommand("residents off");
        int stock=_world.Stone;
        RunCommand("dig 20 20 -3");RunCommand("place nodwarf:anvil 20 20 -3");
        if(_world.Workers.Count!=0||!_world.Buildings.ContainsKey(new Cell(20,20,-3))||_world.Stone!=stock||_world.Jobs.Count!=0)
            throw new InvalidOperationException("Creative commands must work without residents or material spend");
        long tick=_clock.Tick;RunCommand("step 2");
        if(_clock.Tick!=tick+2||!_clock.Paused)throw new InvalidOperationException("Single-step clock incorrect");
        RunCommand("fog off");RunCommand("level -3");
        if(!CurrentView.RevealAll||CurrentView.Top!=-2)throw new InvalidOperationException("Reveal and level commands");
        int count=_world.Buildings.Count;RunCommand("place bad-id 20 21 -3");RunCommand("dig bad 20 -3");
        if(_world.Buildings.Count!=count)throw new InvalidOperationException("Malformed commands changed world");
        ToggleConsole();_commandInput.Text="wasd";_commandInput.GrabFocus();
        var focus=_focus;MoveCamera(1);
        if(_focus!=focus)throw new InvalidOperationException("Console moved camera");
        DeveloperInput(new InputEventKey{Pressed=true,Keycode=Key.Escape});
        if(_consolePanel.Visible||GetViewport().GuiGetFocusOwner()==_commandInput)throw new InvalidOperationException("Closing console retained input focus");
        RunCommand("dev off");
        if(_world.Workers.Count!=0)throw new InvalidOperationException("Disabling dev must not add residents implicitly");
        RunCommand("dev on");RunCommand("residents on");RunCommand("dev off");
        if(_world.Workers.Count!=3||CurrentView.RevealAll)throw new InvalidOperationException("Leaving dev mode must restore residents and fog");
        if(!File.Exists(_sessionLog.FilePath)||!_sessionLog.Snapshot().Any(r=>r.Category=="命令"))throw new InvalidOperationException("Session log missing");
        GD.Print("DEVELOPER_UI_OK: guarded commands, no-worker edits, single step, invalid input, console focus, restore and disk logs");
    }
}
