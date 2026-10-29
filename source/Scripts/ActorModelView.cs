using Godot;
using NoDwarf.Simulation;
using System.Text.Json;
namespace NoDwarf;

// Presentation bridge. Normal gameplay uses sprite4; model3d is retained only
// for explicit legacy asset/trajectory reviews. Node3D/Sprite3D place 2D art in the world.
public partial class ActorModelView : Node3D
{
    public sealed class Definition
    {
        public string Renderer { get; set; } = "model3d";
        public string Scene { get; set; } = "";
        public float Scale { get; set; } = 1;
        public float YawDegrees { get; set; }
        public Dictionary<string,string> Animations { get; set; } = new();
        public Dictionary<string,string> EquipmentScenes { get; set; } = new();
    }
    private static Definition? _definition;
    public static Definition LoadDefinition() => _definition ??= JsonSerializer.Deserialize<Definition>(
        Godot.FileAccess.GetFileAsString("res://Assets/Characters/dwarf.json")) ?? new();
    public static Definition LoadLegacyDefinition() => JsonSerializer.Deserialize<Definition>(
        Godot.FileAccess.GetFileAsString("res://Assets/Characters/legacy-model3d.json"))
        ?? throw new InvalidOperationException("Missing legacy 3D review configuration");
    private Node3D? _model;
    private AnimationPlayer? _player;
    private Definition _settings = new();
    private string _state = "", _gear = "";
    private double _started;
    private readonly List<Node3D> _held = new();
    public bool HasModel => _model is not null;
    public string CurrentState => _state;
    public static IEnumerable<Node> Descendants(Node root)
    {
        foreach(var child in root.GetChildren()) { yield return child; foreach(var nested in Descendants(child)) yield return nested; }
    }
    public void Initialize(Definition? definition=null)
    {
        Name="ModelView"; _settings=definition??LoadDefinition();
        if(_settings.Renderer=="sprite4")
        {var sprite=new DirectionalDwarfView{Name="DwarfSprite"};_model=sprite;AddChild(sprite);sprite.Initialize();return;}
        if(string.IsNullOrWhiteSpace(_settings.Scene))return;
        if(!float.IsFinite(_settings.Scale)||_settings.Scale<=0||!float.IsFinite(_settings.YawDegrees))
        { GD.PushWarning("Invalid dwarf model scale/yaw; using sprite.");return; }
        if(!ResourceLoader.Exists(_settings.Scene)) { GD.PushWarning("Dwarf model missing: "+_settings.Scene);return; }
        var packed=GD.Load<PackedScene>(_settings.Scene);
        if(packed is null)return;
        var instance=packed.Instantiate();
        if(instance is not Node3D model){instance.QueueFree();GD.PushWarning("Dwarf scene must have a Node3D root.");return;}
        _model=model;AddChild(model);
        if(_settings.Scale!=1)GD.PushWarning("Actor scale ignored: combat rig uses metre-calibrated grip poses. Change equipment geometry for weapon sizes.");
        model.RotateY(Mathf.DegToRad(_settings.YawDegrees));
        _player=Descendants(model).Prepend(model).OfType<AnimationPlayer>().FirstOrDefault();
        if(_player is not null){_player.Stop();_player.SetProcess(false);_player.SetPhysicsProcess(false);}
    }
    public void ApplyEquipmentDimensions(string item,EquipmentDimensions geometry)
    {
        geometry.Validate();
        var scale=geometry.Scale;
        foreach(var held in _held.Where(n=>n.Name==item))held.Scale=new(scale.X,scale.Y,scale.Z);
        if(_model?.HasMethod("set_equipment_scale")==true)
        _model.Call("set_equipment_scale",item,new Vector3(scale.X,scale.Y,scale.Z));
    }
    public void UpdateEquipment(IEnumerable<ItemStack> inventory,string beard="none")
    {
        if(!HasModel)return;
        var worn=inventory.Where(i=>i.Slot!="").OrderBy(i=>i.Slot).ToArray();
        string key=beard+"|"+string.Join(";",worn.Select(i=>i.Slot+":"+i.Item));if(key==_gear)return;_gear=key;
        if(_model is DirectionalDwarfView sprite){sprite.SetEquipment(worn);return;}
        foreach(var held in _held){held.GetParent().RemoveChild(held);held.QueueFree();}_held.Clear();
        if(_model!.HasMethod("sync_colony_equipment"))
        {
            _model.Call("sync_colony_equipment",Variant.From(worn.Select(i=>i.Item).Concat(beard=="full"?new[]{"cosmetic_beard"}:Array.Empty<string>()).ToArray()));
            foreach(var item in worn.Where(i=>i.Slot is "main" or "off"))
            {
                var socket=Descendants(_model).OfType<BoneAttachment3D>().FirstOrDefault(n=>n.BoneName==(item.Slot=="main"?"slot_hand_r":"slot_hand_l"));
                if(socket is not null&&EquipmentModels.Create(item.Item) is {} held)
                {socket.AddChild(held);_held.Add(held);}
            }
            foreach(var item in worn)if(EquipmentPhysicalCatalog.Find(item.Item) is {} geometry)
            ApplyEquipmentDimensions(item.Item,geometry);
            return;
        }
        foreach(var item in worn)
        {
            if(!_settings.EquipmentScenes.TryGetValue(item.Item,out var scene)||!ResourceLoader.Exists(scene))continue;
            var socket=Descendants(_model!).OfType<Node3D>().FirstOrDefault(n=>n.Name==(item.Slot=="main"?"MainHandSocket":"OffHandSocket"));
            if(socket is null)continue;
            var instance=GD.Load<PackedScene>(scene)?.Instantiate();
            if(instance is Node3D held){socket.AddChild(held);if(EquipmentPhysicalCatalog.Find(item.Item) is {} geometry){var scale=geometry.Scale;held.Scale=new(scale.X,scale.Y,scale.Z);}_held.Add(held);}else instance?.QueueFree();
        }
    }
    public void Present(DemoWorker actor,double time,Vector3? facing,float spriteBob)
    {
        if(!HasModel)return;
        Position=Vector3.Down*spriteBob; // Model's feet stay grounded; sprite bob is legacy-only.
        if(facing is Vector3 direction && direction.LengthSquared()>.0001f)
            Rotation=new(0,Mathf.Atan2(-direction.X,-direction.Z),0);
        bool resting=actor.Person.MedicalBed is not null||actor.Person.Activity?.Kind=="rest"&&actor.Path.Count==0;
        string state=resting?"down":actor.Person.Activity?.Kind=="rescue_carry"?"carry":actor.Person.Incapacitated?"down":actor.Path.Count>0?(actor.Job?.Materials.Any(m=>m.Stage==1)==true?"carry":"walk"):actor.Combat.Drafted?"combat_idle":actor.Job is not null?"work":"idle";
        if(actor.Combat.AttackWeapon!="")state="attack";
        else if(actor.Combat.Defense!="")state=actor.Combat.Defense=="block"?"block_hold":"parry";
        if(actor.Combat.AttackWeapon==""&&actor.Combat.ReactionUntil>time&&actor.Combat.LastContact is "block" or "parry")
            state=actor.Combat.LastContact=="block"?"block_hold":"parry";
        if(actor.Person.Incapacitated)state="down";
        if(state!=_state){_state=state;_started=time;}
        if(_model is DirectionalDwarfView sprite)
        {Rotation=Vector3.Zero;sprite.SetAppearance(actor.Appearance);sprite.Sample(time,state,facing,actor.Combat.AttackTime);return;}
        // Until a down clip is supplied, lie the rig sideways instead of leaving a defeated actor standing.
        _model!.Rotation=new Vector3(0,Mathf.DegToRad(_settings.YawDegrees),state=="down"&&Clip("down") is null?Mathf.Pi/2:0);
        if(_player is null||!IsVisibleInTree())return;
        string? clip=state=="attack"&&actor.Combat.AttackWeapon!=""&&_player.HasAnimation(MeleeProfiles.Weapon(actor.Combat.AttackWeapon).Animation)
            ?MeleeProfiles.Weapon(actor.Combat.AttackWeapon).Animation:Clip(state)??Clip("idle");if(clip is null)return;
        var animation=_player.GetAnimation(clip);double length=animation.Length;
        double elapsed=state=="attack"?actor.Combat.AttackTime:state is "block_hold" or "parry"?.2:Math.Max(0,time-_started);
        bool loop=state is "walk" or "idle" or "combat_idle" or "work" or "carry";
        double position=loop&&length>0?elapsed%length:Math.Min(elapsed,length);
        // Seek using simulation time: pause and speed changes cannot desynchronize the pose.
        _player.AssignedAnimation=clip;_player.Seek(position,true);
    }
    private string? Clip(string state)=>_settings.Animations.TryGetValue(state,out var clip)&&_player?.HasAnimation(clip)==true?clip:null;
    private double AnimationDuration(string state)=>Clip(state) is string clip?_player!.GetAnimation(clip).Length:0;
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
