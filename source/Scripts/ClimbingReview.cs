using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewClimbing()
    {
        try
        {
            FallSafetyChecks.Run();
            ClimbingChecks.Run();
            _world=new DemoWorld(flat:true){FallBreakChance=0};_world.Items.Clear();_world.Stockpiles.Clear();_world.Workers.RemoveRange(1,2);_overview.World=_world;
            _clock.Paused=true;_foundation=null;_cutaway=false;_interiorView=false;
            var low=new Cell(127,127);var high=new Cell(128,127,1);var end=new Cell(129,127);
            _world.DeveloperPlace(high+new Cell(0,0,-1),BuildKind.StoneBricks);
            var worker=_world.Workers[0];worker.X=low.X;worker.Z=low.Z;worker.Level=0;worker.Path.Enqueue(high);worker.Path.Enqueue(end);
            CreatePeople();_focus=WorldPosition(low);_camera.Size=8;PositionCamera();SyncWorld();_terrainView.FlushMeshes();
            for(int i=0;i<10;i++)_world.Update(1d/30);
            AnimatePeople();
            if(_world.WorkerHeight(worker)<=0||_world.WorkerHeight(worker)>=1)throw new InvalidOperationException("Missing mid-climb elevation");
            var capsule=_people[0].FindChildren("*","CollisionShape3D",true,false).OfType<CollisionShape3D>().Select(n=>n.Shape).OfType<CapsuleShape3D>().First();
            if(Math.Abs(capsule.Height-1.4f)>.001)throw new InvalidOperationException("Resident hitbox not scaled");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/climbing-mid.png"));
            for(int i=0;i<150;i++)_world.Update(1d/30);
            if(worker.Tile!=end||worker.Path.Count!=0)throw new InvalidOperationException("Resident failed to climb and descend");
            var jump=new Cell(130,127,2);
            for(int y=0;y<2;y++)_world.Buildings[jump with{Level=y}]=new(jump with{Level=y},BuildKind.StoneBricks);
            worker.X=jump.X;worker.Z=jump.Z;worker.Level=2;worker.Path.Enqueue(end);
            SyncWorld();
            for(int i=0;i<40;i++)_world.Update(1d/30);
            if(_world.WorkerHeight(worker)<=0||_world.WorkerHeight(worker)>=2)throw new InvalidOperationException("Missing two-metre descent interpolation");
            AnimatePeople();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/jump-two-metres.png"));
            for(int i=0;i<80;i++)_world.Update(1d/30);
            if(worker.Tile!=end||worker.Person.Injuries.Count!=0)throw new InvalidOperationException("Safe two-metre landing failed");
            AnimatePeople();GD.Print("CLIMB_UI_OK: 1.4m hitbox, mid-climb height, actual ascent and descent");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
