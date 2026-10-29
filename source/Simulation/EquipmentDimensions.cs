using System.Numerics;
using System.Text.Json;
namespace NoDwarf.Simulation;

// Metres in the equipment's local grip axes. Reference bounds calibrate an authored mesh;
// Size is the only editable physical size. Contact coordinates are in reference-mesh space.
public sealed record EquipmentDimensions
{
    public float[] ReferenceMin {get;init;}=[];
    public float[] ReferenceMax {get;init;}=[];
    public float[] Size {get;init;}=[];
    public float[] ContactStart {get;init;}=[];
    public float[] ContactEnd {get;init;}=[];
    public float ContactRadius {get;init;}
    public double? ExpiresAt {get;init;}
    public string MaterialId {get;init;}="";
    public static Vector3 V(float[] v)=>new(v[0],v[1],v[2]);
    public Vector3 Scale=>V(Size)/(V(ReferenceMax)-V(ReferenceMin));
    public Vector3 Min=>V(ReferenceMin)*Scale;
    public Vector3 Max=>V(ReferenceMax)*Scale;
    public Vector3 Start=>V(ContactStart)*Scale;
    public Vector3 End=>V(ContactEnd)*Scale;
    public float Radius
    {
        get
        {
            var axis=Vector3.Abs(V(ContactEnd)-V(ContactStart));var scale=Scale;
            return ContactRadius*(axis.Z>=axis.X&&axis.Z>=axis.Y?Math.Max(scale.X,scale.Y):axis.Y>=axis.X?Math.Max(scale.X,scale.Z):Math.Max(scale.Y,scale.Z));
        }
    }
    public float Length=>Size[2];
    public void Validate()
    {
        foreach(var array in new[]{ReferenceMin,ReferenceMax,Size,ContactStart,ContactEnd})
            if(array is null||array.Length!=3||array.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("装备尺寸向量无效");
        for(int i=0;i<3;i++)if(Size[i]<=0||Size[i]>10||ReferenceMax[i]<=ReferenceMin[i])throw new InvalidDataException("装备尺寸范围无效");
        for(int i=0;i<3;i++)if(ContactStart[i]<ReferenceMin[i]-.001f||ContactStart[i]>ReferenceMax[i]+.001f||ContactEnd[i]<ReferenceMin[i]-.001f||ContactEnd[i]>ReferenceMax[i]+.001f)throw new InvalidDataException("接触点超出装备参考尺寸");
        if(!float.IsFinite(ContactRadius)||ContactRadius<0||ContactRadius>.5f)throw new InvalidDataException("装备接触半径无效");
    }
}
public static class EquipmentPhysicalCatalog
{
    private static readonly IReadOnlyDictionary<string,EquipmentDimensions> Definitions=Load();
    private static IReadOnlyDictionary<string,EquipmentDimensions> Load()
    {
        using var stream=typeof(EquipmentPhysicalCatalog).Assembly.GetManifestResourceStream("NoDwarf.equipment-geometry.json")!;
        var data=JsonSerializer.Deserialize<Dictionary<string,EquipmentDimensions>>(stream)!;
        foreach(var entry in data.Values)entry.Validate();return data;
    }
    public static EquipmentDimensions Get(string id)=>Definitions.TryGetValue(id,out var dimensions)?dimensions:throw new ArgumentException("No equipment geometry: "+id);
    public static EquipmentDimensions? Find(string id)=>Definitions.GetValueOrDefault(id);
}

// Optional per-stack attributes. Null durability means the wear system is not enabled.
// Immutable values can be carried through reservations/splits without sharing mutable state.
public sealed record ItemProperties
{
    public double? ExpiresAt {get;init;}
    public string MaterialId {get;init;}="";
    public string QualityId {get;init;}="normal";
    public double? Durability {get;init;}
    public double? MaxDurability {get;init;}
    public void Validate()
    {
        static bool Id(string? s)=>s is not null&&s.Length<=64&&s.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-' or ':' or '.');
        if(ExpiresAt is double expiry&&(!double.IsFinite(expiry)||expiry<0)||!Id(MaterialId)||!Id(QualityId)||QualityId.Length==0||Durability.HasValue!=MaxDurability.HasValue||
            Durability is double d&&(!double.IsFinite(d)||d<0)||MaxDurability is double m&&(!double.IsFinite(m)||m<=0||m>1e9)||Durability>MaxDurability)
            throw new InvalidDataException("物品材质、品质或耐久字段无效");
    }
}
public sealed class GripPose
{
    public double Time {get;set;}
    public float[] Origin {get;set;}=[];
    public float[] X {get;set;}=[];
    public float[] Y {get;set;}=[];
    public float[] Z {get;set;}=[];
    public Vector3 Transform(Vector3 point)=>EquipmentDimensions.V(Origin)+EquipmentDimensions.V(X)*point.X+EquipmentDimensions.V(Y)*point.Y+EquipmentDimensions.V(Z)*point.Z;
    public Vector3 Inverse(Vector3 point)
    {
        var p=point-EquipmentDimensions.V(Origin);
        return new(Vector3.Dot(p,EquipmentDimensions.V(X)),Vector3.Dot(p,EquipmentDimensions.V(Y)),Vector3.Dot(p,EquipmentDimensions.V(Z)));
    }
}
public static class CombatGripPoses
{
    private static readonly IReadOnlyDictionary<string,GripPose[]> Poses=Load();
    private static IReadOnlyDictionary<string,GripPose[]> Load()
    {
        using var stream=typeof(CombatGripPoses).Assembly.GetManifestResourceStream("NoDwarf.grip-poses.json")!;
        return JsonSerializer.Deserialize<Dictionary<string,GripPose[]>>(stream)!;
    }
    public static GripPose Guard(bool left)=>Poses[left?"guard_left":"guard_right"][0];
    public static Vector3 Sample(string name,double elapsed,Vector3 point)
    {
        var samples=Poses[name];double frame=Math.Clamp(elapsed/samples[^1].Time,0,1)*(samples.Length-1);
        int i=Math.Min((int)frame,samples.Length-2);return Vector3.Lerp(samples[i].Transform(point),samples[i+1].Transform(point),(float)(frame-i));
    }
}
