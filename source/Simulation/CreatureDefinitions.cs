using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace NoDwarf.Simulation;

public sealed record CreaturePartDefinition(string Id,double Health,bool Vital);
public sealed record CreatureBodyDefinition(string Id,IReadOnlyList<CreaturePartDefinition> Parts);
public sealed record CreatureMovementDefinition(string Id,double Speed,int StepUp,int DropDown,bool Ladders,double MaxWater);
public sealed record CreatureViewDefinition(string Kind,string Resource,string Texture,double Scale);
public sealed record CreatureSpeciesDefinition(string Id,string Name,string Body,string Movement,string Behavior,
    double ThinkSeconds,int Limit,CreatureViewDefinition View,string Fingerprint)
{
    public string Controller {get;init;}="wildlife";
    public IReadOnlyList<string> Capabilities {get;init;}=Array.Empty<string>();
    public bool Can(string capability)=>Capabilities.Contains(capability);
}

// Configuration selects registered behaviors; it never names arbitrary CLR types or executable code.
public sealed class CreatureCatalog
{
    public IReadOnlyDictionary<string,CreatureBodyDefinition> Bodies {get;}
    public IReadOnlyDictionary<string,CreatureMovementDefinition> Movements {get;}
    public IReadOnlyDictionary<string,CreatureSpeciesDefinition> Species {get;}
    public int PopulationLimit {get;}
    public int UpdateBudget {get;}
    private CreatureCatalog(Dictionary<string,CreatureBodyDefinition> bodies,Dictionary<string,CreatureMovementDefinition> movement,Dictionary<string,CreatureSpeciesDefinition> species,int limit,int budget)
    {Bodies=new ReadOnlyDictionary<string,CreatureBodyDefinition>(bodies);Movements=new ReadOnlyDictionary<string,CreatureMovementDefinition>(movement);Species=new ReadOnlyDictionary<string,CreatureSpeciesDefinition>(species);PopulationLimit=limit;UpdateBudget=budget;}
    public static CreatureCatalog Load(Stream stream)
    {
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,IgnoreWhitespace=true,XmlResolver=null,MaxCharactersInDocument=2_000_000});
        return Parse(XDocument.Load(reader));
    }
    public static CreatureCatalog Parse(XDocument doc)
    {
        string A(XElement e,string key)=>e.Attribute(key)?.Value is {Length:>0} text&&text.Length<=256?text:throw new InvalidDataException($"生物配置缺少或无效：{e.Name}.{key}");
        double N(XElement e,string key,double min,double max)
        {if(!double.TryParse(A(e,key),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<min||n>max)throw new InvalidDataException("生物配置数值越界："+key);return n;}
        int I(XElement e,string key,int min,int max){double n=N(e,key,min,max);if(n!=Math.Truncate(n))throw new InvalidDataException("生物配置要求整数："+key);return (int)n;}
        bool B(XElement e,string key)=>bool.TryParse(A(e,key),out bool b)?b:throw new InvalidDataException("生物配置要求布尔值："+key);
        string Id(XElement e){string id=A(e,"id");if(id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ':' and not '_' and not '-' and not '.'))throw new InvalidDataException("生物 ID 格式错误");return id;}
        string Asset(XElement e,string key)
        {string p=e.Attribute(key)?.Value??"";if(p!=""&&(!p.StartsWith("res://",StringComparison.Ordinal)||p.Contains("..")||p.Contains('\\')||p.Length>512))throw new InvalidDataException("生物资源必须是项目内 res:// 路径");return p;}
        var root=doc.Root;if(root?.Name!="Creatures"||A(root,"version")!="1")throw new InvalidDataException("不支持的生物配置格式");
        var bodies=new Dictionary<string,CreatureBodyDefinition>();var movements=new Dictionary<string,CreatureMovementDefinition>();var species=new Dictionary<string,CreatureSpeciesDefinition>();
        XElement Section(string name)=>root.Element(name)??throw new InvalidDataException("生物配置缺少 "+name);
        foreach(var e in Section("Bodies").Elements("Body"))
        {
            var parts=e.Elements("Part").Select(p=>new CreaturePartDefinition(Id(p),N(p,"health",.01,1_000_000),B(p,"vital"))).ToArray();
            if(parts.Length is <1 or >64||parts.Select(p=>p.Id).Distinct().Count()!=parts.Length||!parts.Any(p=>p.Vital))throw new InvalidDataException("身体需含唯一部位及至少一个要害部位");
            string id=Id(e);if(!bodies.TryAdd(id,new(id,Array.AsReadOnly(parts))))throw new InvalidDataException("重复身体 ID："+id);
        }
        foreach(var e in Section("Movements").Elements("Movement"))
        {
            string id=Id(e);if(!movements.TryAdd(id,new(id,N(e,"speed",.05,20),I(e,"stepUp",0,1),I(e,"dropDown",0,2),B(e,"ladders"),N(e,"maxWater",0,.49))))throw new InvalidDataException("重复移动模板 ID："+id);
        }
        foreach(var e in Section("Species").Elements("Species"))
        {
            string id=Id(e),body=A(e,"body"),movement=A(e,"movement");
            if(!bodies.ContainsKey(body)||!movements.ContainsKey(movement))throw new InvalidDataException("物种引用了未知身体或移动模板："+id);
            var v=e.Element("View")??throw new InvalidDataException("物种缺少 View");string kind=A(v,"kind");
            if(kind is not ("none" or "billboard" or "model"))throw new InvalidDataException("未知表现类型："+kind);
            var view=new CreatureViewDefinition(kind,Asset(v,"resource"),Asset(v,"texture"),N(v,"scale",.01,20));
            if(kind!="none"&&view.Resource=="")throw new InvalidDataException("表现资源路径为空");
            // Fingerprint includes referenced templates. Changed anatomy is rejected, never healed on load.
            string hash=ContentCatalog.Hash(ContentCatalog.Semantic(e)+ContentCatalog.Semantic(Section("Bodies").Elements().Single(b=>Id(b)==body))+ContentCatalog.Semantic(Section("Movements").Elements().Single(m=>Id(m)==movement)));
            string controller=e.Attribute("controller")?.Value??"wildlife";
            if(controller is not ("resident" or "wildlife"))throw new InvalidDataException("未知生物控制器："+controller);
            var capabilities=e.Element("Capabilities")?.Elements("Capability").Select(Id).ToArray()??Array.Empty<string>();
            if(capabilities.Length>64||capabilities.Distinct().Count()!=capabilities.Length)throw new InvalidDataException("生物能力重复或过多");
            if(controller=="resident" && (bodies[body].Parts.Count!=ResidentCatalog.Parts.Length||bodies[body].Parts.Any(p=>!ResidentCatalog.Parts.Any(r=>r.Id==p.Id)||p.Health!=100||p.Vital!=(p.Id is "head" or "torso"))))throw new InvalidDataException("居民健康适配器目前要求十部位、100 生命值及头/躯干要害模板");
            if(!species.TryAdd(id,new(id,A(e,"name"),body,movement,A(e,"behavior"),N(e,"thinkSeconds",.1,3600),I(e,"limit",1,4096),view,hash){Controller=controller,Capabilities=Array.AsReadOnly(capabilities)}))throw new InvalidDataException("重复物种 ID："+id);
        }
        if(bodies.Count>256||movements.Count>256||species.Count>1024)throw new InvalidDataException("生物配置过大");
        return new(bodies,movements,species,I(root,"populationLimit",1,4096),I(root,"updateBudget",1,512));
    }
    public static CreatureCatalog Default=>ContentRuntime.Current.Creatures;
}
