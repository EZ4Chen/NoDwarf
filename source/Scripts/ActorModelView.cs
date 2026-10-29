using Godot;
using NoDwarf.Simulation;
using System.Text.Json;
namespace NoDwarf;

// The historical type/node names remain stable for scene callers. This bridge
// only presents 2D paper characters; no rig, model scene or animation resource is loaded.
public partial class ActorModelView : Node3D
{
    public sealed class Definition
    {
        public string Renderer { get; set; } = "sprite4";
    }
    private static Definition? _definition;
    public static Definition LoadDefinition() => _definition ??= JsonSerializer.Deserialize<Definition>(
        Godot.FileAccess.GetFileAsString("res://Assets/Characters/dwarf.json")) ?? new();
    private DirectionalDwarfView? _sprite;
    private string _state = "", _gear = "";
    public bool HasModel => _sprite is not null;
    public string CurrentState => _state;
    public static IEnumerable<Node> Descendants(Node root)
    {
        foreach(var child in root.GetChildren()) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
    public void Initialize()
    {
        if(LoadDefinition().Renderer!="sprite4")throw new InvalidOperationException("Only the current sprite4 character renderer is supported");
        Name="ModelView";
        _sprite=new DirectionalDwarfView{Name="DwarfSprite"};AddChild(_sprite);_sprite.Initialize();
    }
    public void UpdateEquipment(IEnumerable<ItemStack> inventory,string beard="none")
    {
        if(_sprite is null)return;
        var worn=inventory.Where(i=>i.Slot!="").OrderBy(i=>i.Slot).ToArray();
        string key=beard+"|"+string.Join(";",worn.Select(i=>i.Slot+":"+i.Item));if(key==_gear)return;_gear=key;
        _sprite.SetEquipment(worn);
    }
    public void Present(DemoWorker actor,double time,Vector3? facing,float spriteBob)
    {
        if(_sprite is null)return;
        Position=Vector3.Down*spriteBob;
        bool resting=actor.Person.MedicalBed is not null||actor.Person.Activity?.Kind=="rest"&&actor.Path.Count==0;
        string state=resting?"down":actor.Person.Activity?.Kind=="rescue_carry"?"carry":actor.Person.Incapacitated?"down":actor.Path.Count>0?(actor.Job?.Materials.Any(m=>m.Stage==1)==true?"carry":"walk"):actor.Combat.Drafted?"combat_idle":actor.Job is not null?"work":"idle";
        if(actor.Combat.AttackWeapon!="")state="attack";
        else if(actor.Combat.Defense!="")state=actor.Combat.Defense=="block"?"block_hold":"parry";
        if(actor.Combat.AttackWeapon==""&&actor.Combat.ReactionUntil>time&&actor.Combat.LastContact is "block" or "parry")
            state=actor.Combat.LastContact=="block"?"block_hold":"parry";
        if(actor.Person.Incapacitated)state="down";
        _state=state;Rotation=Vector3.Zero;
        _sprite.SetAppearance(actor.Appearance);_sprite.Sample(time,state,facing,actor.Combat.AttackTime);
    }
}
public partial class Main
{
    private static void AddActorModel(Node3D actor)
    {
        var model=new ActorModelView();actor.AddChild(model);model.Initialize();
        actor.GetChild<Sprite3D>(0).Visible=!model.HasModel;
    }
    private void UpdateActorModel(Node3D node,DemoWorker worker,float bob=0)
    {
        var view=node.GetNodeOrNull<ActorModelView>("ModelView");if(view?.HasModel!=true)return;
        Vector3? facing=null;
        if(worker.Combat.AttackWeapon!="")facing=new Vector3((float)worker.Combat.AimX,0,(float)worker.Combat.AimZ);
        else if(worker.Combat.Defense!="")facing=new Vector3((float)worker.Combat.FacingX,0,(float)worker.Combat.FacingZ);
        else if(worker.Path.TryPeek(out var next))facing=new Vector3((float)(next.X-worker.X),0,(float)(next.Z-worker.Z));
        else if(worker.Combat.Target!=""&&_world.Actors.FirstOrDefault(a=>a.Id==worker.Combat.Target) is {} target)
            facing=new Vector3((float)(target.X-worker.X),0,(float)(target.Z-worker.Z));
        view.Rotation=Vector3.Zero;
        view.Present(worker,_world.SimulationTime,facing,bob);
        var carrier=_world.Workers.FirstOrDefault(w=>w.Person.Activity is {Kind:"rescue_carry"} a&&a.Target==worker.Id);
        Cell? resting=worker.Person.MedicalBed??(worker.Person.Activity?.Kind=="rest"&&worker.Path.Count==0?worker.Person.HomeBed:null);
        if(carrier is not null){view.Position+=Vector3.Up*.8f+_camera.GlobalBasis.X*.25f;}
        else if(resting is Cell bed){view.Position+=WorldPosition(bed)-WorldPosition(worker.Tile)+Vector3.Up*.4f;}
        if(view.GetNodeOrNull<DirectionalDwarfView>("DwarfSprite") is {} sprite)
        {
            sprite.Tint(node.GetChild<Sprite3D>(0).Modulate);
            // Labels must follow the same camera-up plane as the paper character.
            var up=_camera.GlobalBasis.Y;
            node.GetChild<Label3D>(1).Position=up*(WorldScale.DwarfHeightMeters+.16f)-Vector3.Up*bob;
            if(node.GetNodeOrNull<Label3D>("Cargo") is {} cargo) cargo.Position=up*(WorldScale.DwarfHeightMeters+.40f)-Vector3.Up*bob;
        }
    }
}
