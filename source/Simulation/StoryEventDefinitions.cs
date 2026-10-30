using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
namespace NoDwarf.Simulation;

// Definitions select existing local effects. They cannot override safety budgets or invoke arbitrary code.
public sealed record StoryEventDefinition(string Id,string Name,string Tool,string Handler,string Description,
    int MinCount,int MaxCount,double MinDelay,double MaxDelay,double MinDuration,double MaxDuration,IReadOnlyList<string> Items)
{
    public static readonly IReadOnlyList<string> Weathers=Array.AsReadOnly(new[]{"clear","cloudy","rain","fog"});
    private static object Number(double min,double max,string type="number")=>new{type,minimum=min,maximum=max};
    public IReadOnlyDictionary<string,object> Parameters()
    {
        var p=new Dictionary<string,object>();
        if(Handler=="supplies")p["item"]=new Dictionary<string,object>{{"type","string"},{"enum",Items}};
        if(Handler is "raid" or "supplies")p["count"]=Number(MinCount,MaxCount,"integer");
        if(Handler=="weather")
        {
            p["weather"]=new Dictionary<string,object>{{"type","string"},{"enum",Weathers}};
            p["duration_hours"]=Number(MinDuration,MaxDuration);
        }
        p["delay_hours"]=Number(MinDelay,MaxDelay);p["reason"]=new{type="string",minLength=1,maxLength=240};return new ReadOnlyDictionary<string,object>(p);
    }
    public object ToolSchema()
    {var parameters=Parameters();return new{name=Tool,description=Description,parameters=new{type="object",properties=parameters,required=parameters.Keys.ToArray(),additionalProperties=false}};}
    public StoryEvent Create(JsonElement args,string callId,double now)
    {
        var fields=Parameters();
        if(args.ValueKind!=JsonValueKind.Object||args.EnumerateObject().Count()!=fields.Count||args.EnumerateObject().Any(p=>!fields.ContainsKey(p.Name))||args.EnumerateObject().Select(p=>p.Name).Distinct().Count()!=fields.Count)throw new InvalidDataException("事件参数字段不匹配："+Id);
        double Num(string key,double min,double max,bool integer=false)
        {if(!args.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out var n)||!double.IsFinite(n)||n<min||n>max||integer&&n!=Math.Truncate(n))throw new InvalidDataException("事件参数越界："+Id+"."+key);return n;}
        string Str(string key,int max)
        {if(!args.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.String||v.GetString() is not {Length:>0} s||s.Length>max)throw new InvalidDataException("事件文本无效："+Id+"."+key);return s;}
        var e=new StoryEvent{Id=callId,DefinitionId=Id,Kind=Handler,Due=now+Num("delay_hours",MinDelay,MaxDelay)*DemoWorld.StoryHour,Reason=Str("reason",240)};
        if(Handler is "raid" or "supplies")e.Count=(int)Num("count",MinCount,MaxCount,true);
        if(Handler=="supplies")e.Item=Str("item",256);
        if(Handler=="weather"){e.Weather=Str("weather",20);e.Duration=Num("duration_hours",MinDuration,MaxDuration)*DemoWorld.StoryHour;}
        Validate(e);return e;
    }
    public void Validate(StoryEvent e)
    {
        if(e.DefinitionId!=Id||e.Kind!=Handler||!double.IsFinite(e.Duration)||e.Duration<0||
           Handler is "raid" or "supplies"&&(e.Count<MinCount||e.Count>MaxCount)||
           Handler=="supplies"&&(!Items.Contains(e.Item)||e.Weather!=""||e.Duration!=0)||
           Handler=="raid"&&(e.Item!=""||e.Weather!=""||e.Duration!=0)||
           Handler=="weather"&&(e.Count!=0||e.Item!=""||!Weathers.Contains(e.Weather)||e.Duration<MinDuration*DemoWorld.StoryHour||e.Duration>MaxDuration*DemoWorld.StoryHour))throw new InvalidDataException("事件定义或参数不匹配："+Id);
    }
}
internal static class StoryEventParser
{
    internal static IReadOnlyDictionary<string,StoryEventDefinition> Parse(XDocument doc,IReadOnlyList<ItemDefinition> items,IReadOnlyDictionary<string,DefinitionSource> sources)
    {
        var result=new Dictionary<string,StoryEventDefinition>(StringComparer.Ordinal);var tools=new HashSet<string>(StringComparer.Ordinal){"wait","finish","cancel_event"};
        foreach(var e in doc.Root!.Element("Events")!.Elements("Event"))
        {
            string id=(string?)e.Attribute("id")??"";string key="events/Events/"+id;
            InvalidDataException Error(string field)=>new(sources.GetValueOrDefault(key+"/"+field,sources[key])+" 事件字段无效："+field);
            string Text(string field,int max){string? s=(string?)e.Attribute(field);return !string.IsNullOrWhiteSpace(s)&&s.Length<=max?s:throw Error(field);}
            double N(string field,double fallback,double min,double max)
            {if(e.Attribute(field) is null)return fallback;if(!double.TryParse((string?)e.Attribute(field),NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n)||n<min||n>max)throw Error(field);return n;}
            string handler=Text("handler",40),tool=Text("tool",80);
            if(handler is not ("raid" or "supplies" or "weather"))throw Error("handler（未注册）");
            if(!tool.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_')||!tools.Add(tool))throw Error("tool（重复或非法名称）");
            if(e.Attributes().Any(a=>a.Name.LocalName is not ("id" or "name" or "tool" or "handler" or "description" or "minCount" or "maxCount" or "minDelay" or "maxDelay" or "minDuration" or "maxDuration" or "category")))throw Error("未知属性");
            double min=N("minCount",1,1,handler=="raid"?DemoWorld.MaxStoryRaid:100),max=N("maxCount",handler=="raid"?DemoWorld.MaxStoryRaid:100,min,handler=="raid"?DemoWorld.MaxStoryRaid:100);
            if(min!=Math.Truncate(min)||max!=Math.Truncate(max))throw Error("count（需要整数）");
            double delayMin=N("minDelay",handler=="raid"?2:.1,handler=="raid"?2:.1,24),delayMax=N("maxDelay",24,delayMin,24);
            double durationMin=N("minDuration",1,1,12),durationMax=N("maxDuration",12,durationMin,12);
            if(handler!="weather"&&(e.Attribute("minDuration") is not null||e.Attribute("maxDuration") is not null)||handler=="weather"&&(e.Attribute("minCount") is not null||e.Attribute("maxCount") is not null))throw Error("不适用的参数");
            if(e.Elements().Any(x=>x.Name!="Item"||x.Attributes().Any(a=>a.Name!="ref")||x.Elements().Any())||handler!="supplies"&&(e.Elements().Any()||e.Attribute("category") is not null))throw Error("Item/category");
            var references=e.Elements("Item").Select(x=>(string?)x.Attribute("ref")??"").ToArray();
            if(references.Distinct().Count()!=references.Length||references.Any(r=>!items.Any(i=>i.Id==r&&i.StorySupply)))throw Error("Item.ref（缺少物品或未允许叙事补给）");
            string? category=(string?)e.Attribute("category");
            var allowed=handler=="supplies"?items.Where(i=>i.StorySupply&&(category is null||i.Category==category)&&(references.Length==0||references.Contains(i.Id))).Select(i=>i.Id).OrderBy(i=>i,StringComparer.Ordinal).ToArray():Array.Empty<string>();
            if(handler=="supplies"&&allowed.Length==0)throw Error("物品选择器为空");
            result.Add(id,new(id,Text("name",100),tool,handler,Text("description",600),(int)min,(int)max,delayMin,delayMax,durationMin,durationMax,Array.AsReadOnly(allowed)));
        }
        if(result.Count>128)throw new InvalidDataException("叙事事件定义超过128项");
        foreach(string core in new[]{"raid","supplies","weather"})
            if(!result.TryGetValue("base:"+core,out var def)||def.Handler!=core||def.Tool!="schedule_"+core)throw new InvalidDataException("核心事件身份不兼容："+core);
        return new ReadOnlyDictionary<string,StoryEventDefinition>(result);
    }
}
