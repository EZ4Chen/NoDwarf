using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    public static readonly string[] ResidentActions =
        ["talent","route","equip","unequip","drop","cancel","research_topic","research","rest","relax","pickup","eat","treat","social","schedule","work_priority"];
    private static readonly JsonSerializerOptions ResidentObservationJsonOptions=new()
    {Encoder=JavaScriptEncoder.Create(UnicodeRanges.All)};
    private static readonly JsonSerializerOptions ResidentCommandJsonOptions=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
        MaxDepth=8
    };

    // Observe and execute on the simulation thread. Send only detached JSON to a model.
    // Inventory is paged; history and the entire world are deliberately excluded.
    public string ObserveResident(string resident,int inventoryOffset=0)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==resident);
        if(w is null)return JsonSerializer.Serialize(new{ok=false,message="居民不存在"});
        var p=w.Person;var owned=Inventory(w).OrderBy(i=>i.Id).ToArray();int offset=Math.Clamp(inventoryOffset,0,owned.Length);
        var nearby=NearbyItems(w).OrderBy(i=>Math.Abs(i.Cell.X-w.Tile.X)+Math.Abs(i.Cell.Z-w.Tile.Z)).Take(8).ToArray();
        var others=Workers.Where(o=>o!=w).OrderBy(o=>Math.Abs(o.Tile.X-w.Tile.X)+Math.Abs(o.Tile.Z-w.Tile.Z)).Take(8);
        return JsonSerializer.Serialize(new
        {
            ok=true,contractVersion=1,resident=w.Id,name=w.Name,expectedVersion=p.DecisionVersion,expectedResearch=Research.Version,
            actions=ResidentActions,status=w.Status,activity=p.Activity?.Kind,resumeResearch=p.PausedResearch is not null,
            schedule=p.Schedule,currentHour=Calendar.Hour,currentSchedule=ScheduledMode(w),workPriorities=p.WorkPriorities,
            workTypes=ResidentWork.Types.Select(t=>new{id=t.Id,name=t.Name,available=t.Available}),
            needs=new{food=(int)p.Hunger,rest=(int)p.Rest,recreation=(int)p.Recreation,mood=(int)p.Mood},
            health=new{dead=p.Dead,incapacitated=p.Incapacitated,mobility=Math.Round(p.Mobility,2),manipulation=Math.Round(p.Manipulation,2),
                untreated=p.Injuries.Count(i=>!i.Treated&&i.Kind!="missing")},
            points=p.Points,route=p.AutoSkill,traits=p.Traits,
            skills=ResidentCatalog.Skills.Select(s=>new{id=s.Id,level=p.Level(s.Id),rank=p.Talents.GetValueOrDefault(s.Id),active=s.HasWork}),
            talents=ResidentCatalog.Talents.Where(n=>TalentIssue(w,n)=="").Select(n=>new{id=n.Id,cost=n.Cost,effect=n.Effect}),
            inventory=new{offset,total=owned.Length,nextOffset=offset+12<owned.Length?(int?)(offset+12):null,
                weight=Math.Round(owned.Sum(i=>ResidentGear.Weight(i.Item)*i.Count)+(w.Job?.Materials.Where(m=>m.Stage==1).Sum(m=>ResidentGear.Weight(m.Item)*m.Count)??0),2),capacity=p.Capacity,
                meals=owned.Where(i=>i.Item=="meal").Sum(i=>i.Count),bandages=owned.Where(i=>i.Item=="bandage").Sum(i=>i.Count),
                items=owned.Skip(offset).Take(12).Select(i=>new{itemId=i.Id,item=i.Item,count=i.Count,slot=i.Slot})},
            nearby=nearby.Select(i=>new{itemId=i.Id,item=i.Item,available=i.Count-Reserved(i.Id)}),
            neighbors=others.Select(o=>new{resident=o.Id,name=o.Name,idle=o.Job is null&&o.Path.Count==0&&o.Person.Activity is null,
                dead=o.Person.Dead,incapacitated=o.Person.Incapacitated,untreated=o.Person.Injuries.Count(i=>!i.Treated&&i.Kind!="missing")}),
            research=new{active=Research.Active,topics=ResidentCatalog.Research.Select(r=>new{id=r.Id,progress=Math.Round(Research.Progress.GetValueOrDefault(r.Id),1),cost=r.Cost,
                completed=Research.Completed.Contains(r.Id),unlocked=r.Prerequisites.All(Research.Completed.Contains)})}
        },ResidentObservationJsonOptions);
    }

    // The caller cannot disable AI validation through this JSON entry.
    public ResidentResult ActResidentJson(string json)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>4096)return new(false,"人物命令为空或超过长度上限");
        ResidentCommand? command;
        try
        {
            using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=8});
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)return new(false,"人物命令必须是单个对象");
            var keys=doc.RootElement.EnumerateObject().Select(p=>p.Name).ToArray();
            if(keys.Distinct(StringComparer.Ordinal).Count()!=keys.Length)return new(false,"人物命令包含重复字段");
            command=JsonSerializer.Deserialize<ResidentCommand>(json,ResidentCommandJsonOptions);
        }
        catch(JsonException){return new(false,"人物命令 JSON 格式无效");}
        if(command is null||!ResidentActions.Contains(command.Action))return new(false,"不支持的人物操作");
        return ActResident(command,ai:true);
    }
}
