using System.Text.Json;
namespace NoDwarf.Simulation;

public sealed record ModuleArchive(string Id,int Version,JsonElement Data);
public sealed record WorldModule(string Id,int Version,Func<DemoWorld,JsonElement> Save,Action<DemoWorld,JsonElement> Restore);
// Modules use only their current format during development.
public sealed class WorldModuleRegistry
{
    private readonly SortedDictionary<string,WorldModule> modules=new(StringComparer.Ordinal);
    public WorldModuleRegistry Register(WorldModule module)
    {
        if(string.IsNullOrWhiteSpace(module.Id)||module.Version<1||!modules.TryAdd(module.Id,module))throw new InvalidOperationException("重复或无效存档模块："+module.Id);
        return this;
    }
    internal ModuleArchive[] Save(DemoWorld world)=>modules.Values.Select(m=>new ModuleArchive(m.Id,m.Version,m.Save(world).Clone())).ToArray();
    internal void Restore(DemoWorld world,ModuleArchive[] archives)
    {
        if(archives is null||archives.Length>128||archives.Any(a=>a is null)||archives.Select(a=>a.Id).Distinct().Count()!=archives.Length)throw new InvalidDataException("模块存档重复或无效");
        var prepared=new List<(WorldModule Module,JsonElement Data)>();
        foreach(var archive in archives)
        {
            if(!modules.TryGetValue(archive.Id,out var module))throw new InvalidDataException("缺少存档模块："+archive.Id);
            if(archive.Version!=module.Version||archive.Data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)throw new InvalidDataException("模块版本不兼容："+archive.Id);
            var data=archive.Data.Clone();
            prepared.Add((module,data));
        }
        foreach(string required in new[]{"base:events","base:tasks"})if(!archives.Any(a=>a.Id==required))throw new InvalidDataException("缺少核心模块："+required);
        foreach(var (module,data) in prepared)
        {
            try{module.Restore(world,data);}catch(Exception ex) when(ex is JsonException or InvalidDataException or ArgumentException){throw new InvalidDataException("模块恢复失败："+module.Id+"："+ex.Message,ex);}
        }
    }
    public static WorldModuleRegistry Standard()=>new WorldModuleRegistry()
        .Register(new("base:events",1,w=>JsonSerializer.SerializeToElement(w.DomainEvents.Save(),DemoWorld.SaveOptions),(w,json)=>w.DomainEvents.Restore(json.Deserialize<DomainEventArchive>(DemoWorld.SaveOptions)!)))
        .Register(new("base:tasks",1,w=>w.SaveTaskModule(),(w,json)=>w.RestoreTaskModule(json)));
}
public sealed partial class DemoWorld
{
    public ContentCatalog Content {get;}
    public WorldModuleRegistry Modules {get;private set;}=WorldModuleRegistry.Standard();
    internal JsonElement SaveTaskModule()=>JsonSerializer.SerializeToElement(_residentTasks,SaveOptions);
    internal void RestoreTaskModule(JsonElement data)
    {
        var archive=data.Deserialize<ResidentTaskArchive>(SaveOptions)??throw new InvalidDataException("任务存档为空");
        if(archive.NextId<0||archive.Recent is null||archive.Recent.Count>128||archive.Recent.Any(r=>r is null)||archive.Recent.Select(r=>r.Id).Distinct().Count()!=archive.Recent.Count)throw new InvalidDataException("任务回执无效");
        foreach(var r in archive.Recent)
            if(r.Id is null||!r.Id.StartsWith("task:")||!long.TryParse(r.Id.AsSpan(5),out long serial)||serial<=0||serial>archive.NextId||r.Outcome is not ("completed" or "cancelled")||r.Reason is null||r.Reason.Length>500||r.Task is null||r.Actor is null||r.Target is null||!double.IsFinite(r.Time)||r.Time<0)throw new InvalidDataException("任务回执字段无效");
        _residentTasks=archive;
    }
    private static void ValidateContentArchive(WorldSnapshot snapshot,ContentCatalog catalog)
    {
        if(snapshot.Version!=WorldSnapshot.CurrentVersion)
            throw new InvalidDataException("不支持的存档版本。开发阶段只支持当前格式，请新建游戏；原存档未改写。");
        if(snapshot.Jobs is null||snapshot.Jobs.Length>10000||snapshot.Jobs.Any(j=>j is null))throw new InvalidDataException("任务存档无效");
        var manifest=snapshot.Content;
        if(manifest?.Packs is null||manifest.Schema!=1||manifest.Packs.Length is <1 or >128||manifest.Packs[0]?.Id!="base"||
           manifest.Packs.Any(p=>p is null||string.IsNullOrWhiteSpace(p.Id)||!Version.TryParse(p.Version,out _))||
           manifest.Packs.Select(p=>p.Id).Distinct().Count()!=manifest.Packs.Length||
           manifest.GameplayHash is null||manifest.GameplayHash.Length!=64||!manifest.GameplayHash.All(Uri.IsHexDigit))throw new InvalidDataException("内容包清单无效");
        foreach(var pack in manifest.Packs)if(!catalog.Packs.Any(p=>p.Id==pack.Id))throw new InvalidDataException("缺少存档所需内容包："+pack.Id+"；原存档未改写");
        if(manifest.GameplayHash!=catalog.GameplayHash)throw new InvalidDataException("存档玩法配置与当前配置不一致。开发阶段不提供迁移，请新建游戏；原存档未改写。");
        if(manifest.Definitions is null||manifest.Definitions.Length is <1 or >20000||
           manifest.Definitions.Any(d=>d is null||string.IsNullOrEmpty(d.Domain)||string.IsNullOrEmpty(d.Id)||d.Hash is null||d.Hash.Length!=64||!d.Hash.All(Uri.IsHexDigit))||
           manifest.Definitions.Select(d=>(d.Domain,d.Id)).Distinct().Count()!=manifest.Definitions.Length||
           !manifest.Definitions.OrderBy(d=>d.Domain,StringComparer.Ordinal).ThenBy(d=>d.Id,StringComparer.Ordinal).SequenceEqual(catalog.Definitions))
            throw new InvalidDataException("定义修订清单与内容目录不一致");
    }
}
