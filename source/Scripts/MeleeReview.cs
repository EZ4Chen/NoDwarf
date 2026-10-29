using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewMelee()
    {
        try
        {
            MeleeChecks.Run();
            _world=CombatChecks.Fixture();_overview.World=_world;_clock.Paused=true;_creative=false;_foundation=null;_cutaway=false;_interiorView=false;
            var a=_world.Workers[0];a.X=60;a.Z=60;
            var b=new DemoWorker{Id="melee-review",Name="盾牌守卫",X=60,Z=61,Combat=new(){Drafted=true,FacingZ=-1,Defense="block",DefenseTime=.2}};_world.Enemies.Add(b);
            foreach(var actor in new[]{a,b})foreach(var name in new[]{"shirt","trousers","boots","sword","shield"})
            {var item=_world.SpawnItem(name,1,actor.Tile);item.Owner=actor.Id;item.Slot=ResidentGear.Get(name)!.Slot;}
            _focus=WorldPosition(new(60,60));_camera.Size=5;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();AnimatePeople();
            if(!_world.BeginMelee(a,b))throw new Exception("Start visual strike");
            for(int i=0;i<16;i++)_world.AdvanceMelee(1d/30);
            AnimatePeople();
            var view=_people[0].GetNode<ActorModelView>("ModelView");
            if(view.GetNodeOrNull<DirectionalDwarfView>("DwarfSprite") is {} sprite)
            {
                if(view.CurrentState!="attack"||!sprite.Wears("sword"))throw new Exception("Sprite attack state/equipment diverged from simulation");
                var hand=sprite.GetNode<Node3D>("Paper/main");var pose=hand.Transform;
                view.Present(a,_world.SimulationTime,null,0);
                if(hand.Transform!=pose)throw new Exception("Paused sprite attack changed pose");
            }
            else
            {
                var player=ActorModelView.Descendants(view).OfType<AnimationPlayer>().Single();
                if(Math.Abs(player.CurrentAnimationPosition-a.Combat.AttackTime)>.001)throw new Exception("Attack animation diverged from simulation");
            }
            for(int i=0;i<4;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/melee-windup.png"));
            for(int i=0;i<8;i++)_world.AdvanceMelee(1d/30);
            if(a.Combat.LastContact!="block"||b.Person.Injuries.Count!=0)throw new Exception("Rendered setup shield contact");
            AnimatePeople();SelectResident(0);_combatPanel.Show();RefreshUi();
            for(int i=0;i<3;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/melee-shield.png"));
            GD.Print("MELEE_UI_OK: active character renderer follows simulation state and pause; shield contact and defense controls");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
