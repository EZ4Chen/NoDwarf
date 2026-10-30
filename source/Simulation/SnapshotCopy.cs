using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoDwarf.Simulation;

// Copies the serialized DTO graph without a JSON round trip. Compiled once per type;
// ignored runtime bindings are never traversed. Archive DTOs must be acyclic.
public static class SnapshotCopy
{
    public static T Copy<T>(T value)=>value is null?value:Copier<T>.Run(value);
    private static class Copier<T> { public static readonly Func<T,T> Run=Build<T>(); }
    public static void Prepare<T>()=>Prepare(typeof(T),new HashSet<Type>());
    private static void Prepare(Type type,HashSet<Type> seen)
    {
        if(!seen.Add(type))return;
        typeof(SnapshotCopy).GetMethod(nameof(PrepareType),BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null,null);
        if(type.IsArray){Prepare(type.GetElementType()!,seen);return;}
        if(type.IsGenericType)foreach(var t in type.GetGenericArguments())Prepare(t,seen);
        if(type.Namespace==typeof(WorldSnapshot).Namespace)
            foreach(var p in Properties(type))Prepare(p.PropertyType,seen);
    }
    private static void PrepareType<T>()=>GC.KeepAlive(Copier<T>.Run);
    private static IEnumerable<PropertyInfo> Properties(Type type)=>type.GetProperties().Where(p=>p.GetMethod is {IsStatic:false}&&p.GetIndexParameters().Length==0&&p.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition!=JsonIgnoreCondition.Always);
    private static Func<T,T> Build<T>()
    {
        var type=typeof(T);
        if(type==typeof(JsonElement))return (Func<T,T>)(object)(Func<JsonElement,JsonElement>)(v=>v.ValueKind==JsonValueKind.Undefined?v:v.Clone());
        if(type==typeof(string)||type.IsValueType)
        {
            // Value DTOs may contain strings, but never mutable reference fields.
            bool Safe(Type t)=>t==typeof(string)||t.IsPrimitive||t.IsEnum||t.IsValueType&&t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).All(f=>Safe(f.FieldType));
            if(!Safe(type))throw new InvalidOperationException("Snapshot value needs an explicit copier: "+type);
            return v=>v;
        }
        string? method=type.IsArray?nameof(ArrayCopy):null;
        Type[] args=type.IsArray?[type.GetElementType()!]:type.GenericTypeArguments;
        if(type.IsGenericType)
        {
            var def=type.GetGenericTypeDefinition();
            method=def==typeof(List<>)?nameof(ListCopy):def==typeof(HashSet<>)?nameof(SetCopy):
                def==typeof(Dictionary<,>)?nameof(DictionaryCopy):def==typeof(ReadOnlyDictionary<,>)?nameof(ReadOnlyCopy):
                def==typeof(IReadOnlyDictionary<,>)?nameof(DictionaryInterfaceCopy):
                def==typeof(IReadOnlyList<>)?nameof(ListInterfaceCopy):null;
        }
        if(method is not null)return typeof(SnapshotCopy).GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(args).CreateDelegate<Func<T,T>>();
        if(type.Namespace!=typeof(WorldSnapshot).Namespace)throw new InvalidOperationException("Unsupported snapshot DTO: "+type);
        if(Properties(type).Any(p=>p.SetMethod is null&&!p.PropertyType.IsValueType&&p.PropertyType!=typeof(string)))
            throw new InvalidOperationException("Snapshot DTO needs a writable serialized property: "+type);
        var input=Expression.Parameter(type,"source");var result=Expression.Variable(type,"copy");
        Expression create=type.GetConstructor(Type.EmptyTypes) is {} ctor?Expression.New(ctor):Expression.Convert(Expression.Call(input,typeof(object).GetMethod("MemberwiseClone",BindingFlags.NonPublic|BindingFlags.Instance)!),type);
        var body=new List<Expression>{Expression.Assign(result,create)};
        foreach(var p in Properties(type).Where(p=>p.SetMethod is not null))
            body.Add(Expression.Assign(Expression.Property(result,p),Expression.Call(typeof(SnapshotCopy),nameof(Copy),[p.PropertyType],Expression.Property(input,p))));
        body.Add(result);
        return Expression.Lambda<Func<T,T>>(Expression.Block([result],body),input).Compile();
    }
    private static T[] ArrayCopy<T>(T[] values)=>Array.ConvertAll(values,Copy);
    private static List<T> ListCopy<T>(List<T> values)=>values.ConvertAll(Copy);
    private static HashSet<T> SetCopy<T>(HashSet<T> values)=>new(values.Select(Copy),values.Comparer);
    private static Dictionary<K,V> DictionaryCopy<K,V>(Dictionary<K,V> values) where K:notnull
    {var copy=new Dictionary<K,V>(values.Count,values.Comparer);foreach(var (key,value) in values)copy.Add(Copy(key),Copy(value));return copy;}
    private static ReadOnlyDictionary<K,V> ReadOnlyCopy<K,V>(ReadOnlyDictionary<K,V> values) where K:notnull=>new(values.ToDictionary(p=>Copy(p.Key),p=>Copy(p.Value)));
    private static IReadOnlyDictionary<K,V> DictionaryInterfaceCopy<K,V>(IReadOnlyDictionary<K,V> values) where K:notnull=>values is Dictionary<K,V> d?DictionaryCopy(d):values.ToDictionary(p=>Copy(p.Key),p=>Copy(p.Value));
    private static IReadOnlyList<T> ListInterfaceCopy<T>(IReadOnlyList<T> values)=>values.Select(Copy).ToArray();
}
