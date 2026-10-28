using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private void ReviewActorModel()
    {
        try
        {
            var fallback=new ActorModelView();AddChild(fallback);fallback.Initialize(new());
            if(fallback.HasModel)throw new Exception("Empty configuration must preserve sprite fallback");
            fallback.QueueFree();
            var root=new Node3D{Name="Dwarf"};
            var socket=new Node3D{Name="MainHandSocket"};root.AddChild(socket);socket.Owner=root;
            var off=new Node3D{Name="OffHandSocket"};root.AddChild(off);off.Owner=root;
            var mesh=new MeshInstance3D{Name="Body",Mesh=new CapsuleMesh{Height=1.4f,Radius=.25f},Position=Vector3.Up*.7f};root.AddChild(mesh);mesh.Owner=root;
            var player=new AnimationPlayer();root.AddChild(player);player.Owner=root;
            var library=new AnimationLibrary();
            var animation=new Animation{Length=1};int track=animation.AddTrack(Animation.TrackType.Value);
            animation.TrackSetPath(track,"Body:position:x");animation.TrackInsertKey(track,0,0f);animation.TrackInsertKey(track,1,.2f);
            library.AddAnimation("idle",animation);player.AddAnimationLibrary("",library);
            var scene=new PackedScene();scene.Pack(root);ResourceSaver.Save(scene,"res://.local/actor-fixture.tscn");root.Free();
            var weapon=new Node3D();var packed=new PackedScene();packed.Pack(weapon);ResourceSaver.Save(packed,"res://.local/weapon-fixture.tscn");weapon.Free();
            var view=new ActorModelView();AddChild(view);view.Initialize(new(){Scene="res://.local/actor-fixture.tscn",Animations=new(){{"idle","idle"}},EquipmentScenes=new(){{"sword","res://.local/weapon-fixture.tscn"},{"shield","res://.local/weapon-fixture.tscn"}}});
            if(!view.HasModel)throw new Exception("Model load");
            var world=CombatChecks.Fixture();var actor=world.Workers[0];
            view.Present(actor,0,Vector3.Right,.08f);view.Present(actor,.5,Vector3.Right,.08f);
            var body=ActorModelView.Descendants(view).OfType<MeshInstance3D>().Single();
            if(Math.Abs(body.Position.X-.1f)>.01f||Math.Abs(view.Position.Y+.08f)>.001f)throw new Exception("Simulation clock/grounding");
            var pose=body.Position;view.Present(actor,.5,null,.08f);if(body.Position!=pose)throw new Exception("Pause changed pose");
            if(Math.Abs(view.Rotation.Y+Mathf.Pi/2)>.01f)throw new Exception("Facing");
            var item=world.SpawnItem("sword",1,actor.Tile);item.Owner=actor.Id;item.Slot="main";
            var shield=world.SpawnItem("shield",1,actor.Tile);shield.Owner=actor.Id;shield.Slot="off";
            view.UpdateEquipment(world.Inventory(actor));
            var shieldHand=ActorModelView.Descendants(view).OfType<Node3D>().Single(n=>n.Name=="OffHandSocket");
            if(shieldHand.GetChildCount()!=1)throw new Exception("Shield socket");
            var hand=ActorModelView.Descendants(view).OfType<Node3D>().Single(n=>n.Name=="MainHandSocket");
            if(hand.GetChildCount()!=1)throw new Exception("Equipment socket");
            view.UpdateEquipment(world.Inventory(actor));if(hand.GetChildCount()!=1)throw new Exception("Duplicate equipment");
            item.Slot="";view.UpdateEquipment(world.Inventory(actor));if(hand.GetChildCount()!=0)throw new Exception("Unequip");
            actor.Combat.Drafted=true;view.Present(actor,1,null,0);if(view.CurrentState!="combat_idle")throw new Exception("Missing animation fallback");
            GD.Print("ACTOR_MODEL_OK: optional scene, simulation-time pose, pause, facing, grounding, missing clip fallback, equip/unequip sockets");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
