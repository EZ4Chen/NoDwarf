using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewComplexCombat()
    {
        try
        {
            CombatRevisionChecks.Run();
            _world=CombatRevisionChecks.Storeys();_overview.World=_world;_clock.Paused=true;_creative=false;_foundation=null;_cutaway=false;_interiorView=false;_heightSelected=false;
            _world.SetRallyPoint(new(56,60));
            _world.Enemies.Add(new(){Id="upper-enemy",Name="楼上敌人",X=65,Z=60,Level=3,Combat=new(){Drafted=true}});
            _world.MilitaryAttack(_world.Workers[0].Id,"upper-enemy");
            _world.MilitaryMove(_world.Workers[1].Id,new(64,60,3));_world.MilitaryMove(_world.Workers[2].Id,new(64,60,3));
            _focus=WorldPosition(new(61,60,1));_camera.Size=17;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();AnimatePeople();RefreshUi();
            if(_commandMarkers.Count!=4)throw new Exception("Expected flag, enemy and two destinations");
            var upper=_commandMarkers["move:"+_world.Workers[1].Id];
            if(Math.Abs(upper.Position.Y-3.065f)>.01)throw new Exception("Goal marker is on wrong storey");
            _cutaway=true;_heightSelected=true;_layerOffset=0;UpdateCommandMarkers();
            if(upper.Visible||!_commandMarkers["rally"].Visible)throw new Exception("Height-cut marker visibility");
            _cutaway=false;_heightSelected=false;UpdateCommandMarkers();
            var old=_commandMarkers["rally"];UpdateCommandMarkers();if(old!=_commandMarkers["rally"])throw new Exception("Unchanged marker recreated");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/combat-orders.png"));
            _world.MilitaryStop(_world.Workers[1].Id);UpdateCommandMarkers();if(_commandMarkers.ContainsKey("move:"+_world.Workers[1].Id))throw new Exception("Canceled marker retained");
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;CreatePeople();UpdateCommandMarkers();if(_commandMarkers.Count!=3)throw new Exception("Loaded marker reconstruction");
            GD.Print("COMMAND_MARKERS_OK: distinct floor goals, rally flag, attack target, cutaway visibility, cancellation, node reuse and save/load");
            if(OS.GetCmdlineUserArgs().Contains("--markers-only")){GetTree().Quit();return;}
            _world=new DemoWorld(region:"rainforest");_overview.World=_world;
            foreach(var a in _world.Workers){var sword=_world.SpawnItem("sword",1,a.Tile);sword.Owner=a.Id;sword.Slot="main";_world.DraftResident(a.Id,true);}
            var positions=new List<Cell>();
            for(int x=_world.Camp.X+6;x<_world.Camp.X+15;x++)for(int z=_world.Camp.Z;z<_world.Camp.Z+14;z++)
            {var c=_world.Surface(x,z);if(_world.Walkable(c)&&_world.Navigation.CanReach(c,_world.Workers[0].Tile))positions.Add(c);}
            if(positions.Count<3)throw new Exception("Forest combat spawn fixture");
            for(int i=0;i<3;i++){var c=positions[i*2];_world.Enemies.Add(new(){Id="forest-enemy-"+i,Name="森林敌人"+i,X=c.X,Z=c.Z,Level=c.Level,Combat=new(){Drafted=true}});_world.MilitaryAttack(_world.Workers[i].Id,"forest-enemy-"+i);}
            string snapshot=_world.SaveJson();
            foreach(int speed in new[]{1,3})
            {
                _world=DemoWorld.LoadJson(snapshot);_overview.World=_world;_focus=WorldPosition(_world.Camp);_camera.Size=90;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();
                _clock.SetSpeed(speed);_clock.Paused=false;
                var warm=System.Diagnostics.Stopwatch.StartNew();while(warm.Elapsed.TotalSeconds<2)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                var watch=System.Diagnostics.Stopwatch.StartNew();long ticks=_clock.Tick;int frames=0;double longest=0,last=0;
                while(watch.Elapsed.TotalSeconds<5){await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);double now=watch.Elapsed.TotalSeconds;longest=Math.Max(longest,now-last);last=now;frames++;}
                _clock.Paused=true;double seconds=watch.Elapsed.TotalSeconds;
                GD.Print($"FOREST_COMBAT_METRICS: {speed}x, {frames/seconds:F1} FPS, {(_clock.Tick-ticks)/seconds:F1} TPS, longest frame {longest*1000:F1} ms, trees={_world.Trees.Count}, injured={_world.Actors.Count(a=>a.Person.Injuries.Count>0)}");
                if(!_world.Actors.Any(a=>a.Person.Injuries.Count>0))throw new Exception("Forest fight never reached melee");
            }
            GD.Print("COMPLEX_COMBAT_UI_OK");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
