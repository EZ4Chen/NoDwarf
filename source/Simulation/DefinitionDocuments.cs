using System.Collections.ObjectModel;
using System.Xml.Linq;
namespace NoDwarf.Simulation;

public interface IContentDefinition { string Id {get;} string Name {get;} DefinitionInfo Info {get;} }
public sealed record DefinitionInfo(string Description,string? Parent,IReadOnlyList<string> Categories,IReadOnlyList<string> Tags)
{
    public static readonly DefinitionInfo Empty=new("",null,Array.Empty<string>(),Array.Empty<string>());
    internal static readonly string[] Attributes=["description","parent","categories","tags"];
    internal static DefinitionInfo Read(XElement e)
    {
        IReadOnlyList<string> Words(string key)
        {
            var words=((string?)e.Attribute(key)??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(words.Length>32||words.Distinct().Count()!=words.Length||words.Any(w=>w.Length>100||w.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ':' and not '_' and not '-' and not '.')))throw new InvalidDataException("定义分类／标签无效："+(string?)e.Attribute("id"));
            return Array.AsReadOnly(words);
        }
        string description=(string?)e.Attribute("description")??"";
        if(description.Length>2000)throw new InvalidDataException("定义说明过长");
        return new(description,(string?)e.Attribute("parent"),Words("categories"),Words("tags"));
    }
}
// XML is authoring data. Resolve templates once, before typed parsing and reference validation.
internal static class DefinitionDocuments
{
    private static (string Domain,string Root,string Section) Type(string type)=>type switch
    {"Item"=>("production","ProductionDefinitions","Items"),"Block"=>("spatial","SpatialDefinitions","Blocks"),"Building"=>("spatial","SpatialDefinitions","Buildings"),_=>throw new InvalidDataException("未知公共定义类型："+type)};
    internal static List<ContentPack> Prepare(IEnumerable<ContentPack> packs)
    {
        var prepared=new List<ContentPack>();
        foreach(var p in packs)
        {
            var files=new List<ContentFile>();
            foreach(var f in p.Files)
            {
                if(f is null||string.IsNullOrWhiteSpace(f.Name)||f.Document?.Root is null)throw new InvalidDataException(p.Id+"：内容文件无效");
                if(f.Domain!="defs"){files.Add(f with{Document=new XDocument(f.Document)});continue;}
                var root=f.Document.Root;
                if(root?.Name!="Defs"||root.HasAttributes||root.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException(p.Id+"/"+f.Name+"：需要 Defs 根节点");
                foreach(var group in root.Elements().GroupBy(e=>Type(e.Name.LocalName)))
                    files.Add(new(f.Name,group.Key.Domain,new XDocument(new XElement(group.Key.Root,group.GroupBy(n=>Type(n.Name.LocalName).Section).Select(g=>new XElement(g.Key,g.Select(n=>new XElement(n))))))));
            }
            prepared.Add(p with{Files=files});
        }
        var nodes=new Dictionary<(string Type,string Id),(XElement Node,string Source)>();
        foreach(var p in prepared)foreach(var f in p.Files.Where(f=>f.Domain is "production" or "spatial"))
        foreach(var e in f.Document.Root!.Elements().SelectMany(s=>s.Elements()).Where(e=>e.Name.LocalName is "Item" or "Block" or "Building"))
        {
            string id=(string?)e.Attribute("id")??"";string source=p.Id+"/"+f.Name+"/"+id;
            string[] fields=e.Name.LocalName switch
            {
                "Item"=>["id","name","value","texture","color","category","weight","shelfLife","storySupply","abstract"],
                "Block"=>["id","name","shape","color","durability","indestructible","abstract"],
                _=>["id","name","block","handler","abstract"]
            };
            string[] children=e.Name.LocalName switch {"Item"=>["Edible","Place"],"Block"=>["Drop","Ore"],_=>["Cost"]};
            if(e.Attributes().Any(a=>a.Name.Namespace!=XNamespace.None||!fields.Concat(DefinitionInfo.Attributes).Contains(a.Name.LocalName))||e.Elements().Any(c=>c.Name.Namespace!=XNamespace.None||!children.Contains(c.Name.LocalName)))throw new InvalidDataException(source+"：未知字段或子项");

            if(id.Length==0||p.Id!="base"&&(!id.StartsWith(p.Id+":",StringComparison.Ordinal)||id.Length>100||id.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ':' and not '_' and not '-' and not '.')))throw new InvalidDataException(source+"：新增定义必须使用包命名空间");
            if(!nodes.TryAdd((e.Name.LocalName,id),(e,source)))throw new InvalidDataException(source+"：定义 ID 重复");
        }
        var resolved=new Dictionary<(string,string),XElement>();var visiting=new HashSet<(string,string)>();
        XElement Resolve((string,string) key)
        {
            if(resolved.TryGetValue(key,out var prior))return prior;
            if(!nodes.TryGetValue(key,out var value))throw new InvalidDataException("模板引用不存在："+key);
            if(!visiting.Add(key)||visiting.Count>32)throw new InvalidDataException(value.Source+"：模板继承循环或层数超过 32");
            var e=new XElement(value.Node);
            if(e.Attribute("abstract") is {} abstractFlag&&!bool.TryParse(abstractFlag.Value,out _))throw new InvalidDataException(value.Source+"：abstract 必须为布尔值");
            if(e.Attribute("parent") is {} parent)
            {
                var basis=Resolve((key.Item1,parent.Value));
                foreach(var a in basis.Attributes().Where(a=>a.Name.LocalName is not ("id" or "parent" or "abstract")))if(e.Attribute(a.Name) is null)e.SetAttributeValue(a.Name,a.Value);
                // Each child group replaces the inherited group of the same name; omitted groups inherit.
                foreach(var group in basis.Elements().GroupBy(c=>c.Name))if(!e.Elements(group.Key).Any())e.Add(group.Select(c=>new XElement(c)));
            }
            visiting.Remove(key);return resolved[key]=e;
        }
        foreach(var key in nodes.Keys)Resolve(key);
        foreach(var (key,value) in nodes)
        {
            var e=resolved[key];bool template=bool.TryParse((string?)e.Attribute("abstract"),out var a)&&a;
            e.Attribute("abstract")?.Remove();
            if(template)value.Node.Remove();else value.Node.ReplaceWith(e);
        }
        return prepared;
    }
}
