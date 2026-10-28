using System.Numerics;
using System.Text.Json;
namespace NoDwarf.Simulation;

// Immutable combat definitions. Item IDs, animation clips and collision shapes are separate concerns.
public sealed record MeleeWeapon(string Id,string Animation,double Windup,double Active,double Recovery,
    double Damage,string Injury,double Cost,double Impact,bool CanParry,double ParryCost,string Trajectory="thrust",double ParryReady=.12,double ParryWindow=.34)
{
    public EquipmentDimensions Geometry {get;init;}=EquipmentPhysicalCatalog.Get(Id);
    public float Radius=>Geometry.Radius;
    public float Reach=>MeleeProfiles.MeasureReach(this);
    public double Duration=>Windup+Active+Recovery;
}
public sealed record MeleeShield(string Id,double RaiseTime,double Cost,double Strength)
{
    public EquipmentDimensions Geometry {get;init;}=EquipmentPhysicalCatalog.Get(Id);
}
public static class MeleeProfiles
{
    public static readonly IReadOnlyDictionary<string,MeleeWeapon> Weapons=new Dictionary<string,MeleeWeapon>
    {
        ["unarmed"]=new("unarmed","",.20,.16,.60,9,"fracture",6,8,false,0),
        ["sword"]=new("sword","Attack",.44,.30,.51,22,"cut",12,18,true,12,"baked-sword"),
        ["dagger"]=new("dagger","",.15,.16,.36,13,"cut",7,9,true,18),
        ["greatsword"]=new("greatsword","",.48,.32,.65,30,"cut",22,30,true,10,"swing"),
        ["spear"]=new("spear","",.32,.25,.55,24,"cut",15,22,true,14),
        ["mace"]=new("mace","",.38,.25,.66,25,"fracture",18,32,true,16,"swing"),
        ["hammer"]=new("hammer","Mine",.72,.36,.42,18,"fracture",14,22,true,16,"baked-pickaxe"),
        ["axe"]=new("axe","Mine",.72,.36,.42,23,"cut",16,23,true,16,"baked-pickaxe"),
        ["battleaxe"]=new("battleaxe","Mine",.72,.36,.42,30,"cut",24,32,true,18,"baked-pickaxe"),
        ["shovel"]=new("shovel","Mine",.72,.36,.42,12,"fracture",17,17,true,20,"baked-pickaxe"),
        ["hoe"]=new("hoe","Mine",.72,.36,.42,12,"cut",17,17,true,20,"baked-pickaxe"),
        ["pickaxe"]=new("pickaxe","Mine",.72,.36,.42,20,"fracture",20,26,true,18,"baked-pickaxe")
    };
    public static readonly IReadOnlyDictionary<string,MeleeShield> Shields=new Dictionary<string,MeleeShield>
    {
        ["shield"]=new("shield",.16,9,28),["buckler"]=new("buckler",.10,7,16),["tower_shield"]=new("tower_shield",.30,12,42)
    };
    public static MeleeWeapon Weapon(string id)=>Weapons.GetValueOrDefault(id)??Weapons["unarmed"];
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MeleeWeapon,ReachValue> Ranges=new();
    private sealed record ReachValue(float Value);
    public static float MeasureReach(MeleeWeapon weapon)=>Ranges.GetValue(weapon,w=>
    {
        float reach=0;
        for(int i=0;i<=90;i++)
        {
            var blade=Blade(w,w.Windup+w.Active*i/90);
            reach=Math.Max(reach,Math.Max(new Vector2(blade.A.X,blade.A.Z).Length(),new Vector2(blade.B.X,blade.B.Z).Length()));
        }
        return new(reach+w.Radius);
    }).Value;
    public static (Vector3 A,Vector3 B) Blade(MeleeWeapon weapon,double elapsed)
    {
        var geometry=weapon.Geometry;
        if(weapon.Trajectory is "baked-sword" or "baked-pickaxe")
        {
            string clip=weapon.Trajectory=="baked-sword"?"sword":"pickaxe";
            return(CombatGripPoses.Sample(clip,elapsed,geometry.Start),CombatGripPoses.Sample(clip,elapsed,geometry.End));
        }
        float p=(float)Math.Clamp((elapsed-weapon.Windup)/weapon.Active,0,1);
        float angle=weapon.Trajectory=="swing"?(p-.5f)*2.1f:0;
        var forward=new Vector3(MathF.Sin(angle),0,MathF.Cos(angle));var right=new Vector3(forward.Z,0,-forward.X);
        var grip=new Vector3(0,.85f,weapon.Trajectory=="swing"?.25f:.25f+.35f*MathF.Sin(p*MathF.PI));
        Vector3 Place(Vector3 v)=>grip+right*v.X+Vector3.UnitY*v.Y+forward*v.Z;
        return(Place(geometry.Start),Place(geometry.End));
    }
}
