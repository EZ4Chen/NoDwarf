using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewCombat()
    {
        try
        {
            CombatChecks.Run();
            _world=CombatChecks.Fixture();_overview.World=_world;_clock.Paused=true;_creative=false;_foundation=null;_cutaway=false;_interiorView=false;
            foreach(var w in _world.Workers){var sword=_world.SpawnItem("sword",1,w.Tile);sword.Owner=w.Id;sword.Slot="main";_world.DraftResident(w.Id,true);}
            for(int i=0;i<3;i++)_world.Enemies.Add(new(){Id=$"review-enemy-{i}",Name=$"袭击者 {i+1}",X=65,Z=59+i*2,Combat=new(){Drafted=true}});
            _focus=WorldPosition(new(61,60));_camera.Size=18;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();
            SelectResident(0);_draftButton.EmitSignal(Godot.Button.SignalName.Pressed);if(_world.Workers[0].Combat.Drafted)throw new Exception("undraft UI");
            _draftButton.EmitSignal(Godot.Button.SignalName.Pressed);if(!_world.Workers[0].Combat.Drafted)throw new Exception("draft UI");
            _world.SetRallyPoint(new(56,60));_alertButton.EmitSignal(Godot.Button.SignalName.Pressed);if(!_world.CivilianAlert)throw new Exception("alert UI");_alertButton.EmitSignal(Godot.Button.SignalName.Pressed);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            AnimatePeople();var firstEnemy=_enemyVisuals[_world.Enemies[0].Id];
            RightClick(_camera.UnprojectPosition(firstEnemy.Position+Vector3.Up*.7f));
            if(_world.Workers[0].Combat.Target!=_world.Enemies[0].Id)throw new Exception("right-click enemy targeting");
            for(int i=0;i<3;i++)_world.MilitaryAttack(_world.Workers[i].Id,_world.Enemies[i].Id);
            _combatPanel.Show();_selectionPanel.Hide();
            _clock.Paused=false;
            var warmup=System.Diagnostics.Stopwatch.StartNew();while(warmup.Elapsed.TotalSeconds<2)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            long startTick=_clock.Tick;var watch=System.Diagnostics.Stopwatch.StartNew();int frames=0;
            while(watch.Elapsed.TotalSeconds<5){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);frames++;}
            _clock.Paused=true;AnimatePeople();RefreshUi();
            double elapsed=watch.Elapsed.TotalSeconds;
            GD.Print($"COMBAT_RENDER_METRICS: {frames/elapsed:F1} FPS, {(_clock.Tick-startTick)/elapsed:F1} TPS, 3 residents + 3 enemies, 1x, flat fixture, after 2s warm-up; not forest benchmark");
            if(_enemyVisuals.Count!=3||!_world.Actors.Any(w=>w.Person.Injuries.Count>0))throw new Exception("rendered combat did not progress");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/combat-0.5.png"));
            foreach(var w in _world.Workers)_world.DraftResident(w.Id,false);
            _world.SpawnItem("bandage",1,new(58,60));
            var patient=_world.Workers[0];patient.Person.Blood=100;patient.Person.Injuries.Clear();patient.Person.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=20});
            SelectResident(0);OpenPersonPage(2);
            var care=_residentBody.FindChildren("*","Button",true,false).OfType<Godot.Button>().First(b=>b.Text=="安排护理");
            if(care.Disabled)throw new Exception("public-supply care UI incorrectly disabled");care.EmitSignal(Godot.Button.SignalName.Pressed);
            if(!_world.Workers.Any(w=>w.Person.Activity?.Kind=="care_supply"))throw new Exception("care UI did not request public supplies");
            GD.Print("COMBAT_UI_OK: draft/undraft, alert buttons, three rendered enemies, combat feedback and live simulation");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
