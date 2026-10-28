using Godot;
using NoDwarf.Simulation;
using System.Linq;
namespace NoDwarf;
public partial class SwordShieldIntegrationReview : Node
{
 public override void _Ready()
 {
  try
  {
   foreach(var sex in new[]{"male","female"})
   {
    var view=new ActorModelView();AddChild(view);
    view.Initialize(new(){Scene=$"res://Assets/Characters/SwordShield/Models/{sex}_clothed.tscn",Animations=new(){{"idle","idle"},{"walk","walk"},{"combat_idle","sword_shield_idle"},{"attack","sword_slash"},{"down","down"},{"work","idle"}},EquipmentScenes=new(){{"sword","res://Assets/Characters/SwordShield/Weapons/sword.tscn"},{"shield","res://Assets/Characters/SwordShield/Weapons/shield.tscn"}}});
    if(!view.HasModel)throw new System.Exception("Actual model missing: "+sex);
    var world=CombatChecks.Fixture();var actor=world.Workers[0];
    var sword=world.SpawnItem("sword",1,actor.Tile);sword.Owner=actor.Id;sword.Slot="main";
    var shield=world.SpawnItem("shield",1,actor.Tile);shield.Owner=actor.Id;shield.Slot="off";
    view.UpdateEquipment(world.Inventory(actor));view.UpdateEquipment(world.Inventory(actor));
    foreach(var hand in new[]{"MainHandSocket","OffHandSocket"})
      if(ActorModelView.Descendants(view).OfType<Node3D>().Single(n=>n.Name==hand).GetChildCount()!=1)throw new System.Exception("Equipment duplication/missing "+hand);
    actor.Combat.Drafted=true;view.Present(actor,0,Vector3.Forward,0);view.Present(actor,.3,Vector3.Forward,0);
    if(view.CurrentState!="combat_idle")throw new System.Exception("Combat state mapping");
    var sk=ActorModelView.Descendants(view).OfType<Skeleton3D>().Single();
    var pose=Enumerable.Range(0,sk.GetBoneCount()).Select(sk.GetBonePose).ToArray();
    view.Present(actor,.3,null,0);
    for(int i=0;i<pose.Length;i++)if(!pose[i].IsEqualApprox(sk.GetBonePose(i)))throw new System.Exception("Paused pose changed");
    sword.Slot="";shield.Slot="";view.UpdateEquipment(world.Inventory(actor));
    foreach(var hand in new[]{"MainHandSocket","OffHandSocket"})
      if(ActorModelView.Descendants(view).OfType<Node3D>().Single(n=>n.Name==hand).GetChildCount()!=0)throw new System.Exception("Unequip failed");
    var packed=new PackedScene();var model=GD.Load<PackedScene>($"res://Assets/Characters/SwordShield/Models/{sex}_clothed.tscn").Instantiate();
    if(ActorModelView.Descendants(model).Any(n=>n.GetScript().VariantType!=Variant.Type.Nil))throw new System.Exception("Display scene contains scripts");
    if(ActorModelView.Descendants(model).OfType<AnimationPlayer>().Count()!=1)throw new System.Exception("Must have one player");
    model.Free();view.Free();
   }
   GD.Print("SWORD_SHIELD_INTEGRATION_PASS real models, semantic state, pause, equip/unequip, pure scene");GetTree().Quit();
  }catch(System.Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
 }
}
