using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace NoDwarf.Simulation;

public sealed record ContentPackStamp(string Id,string Version);
public sealed record ContentManifest(ContentPackStamp[] Packs,string GameplayHash)
{ public int Schema {get;init;}=1; public DefinitionStamp[]? Definitions {get;init;} }
public sealed record DefinitionStamp(string Domain,string Id,string Hash);
public sealed record DefinitionSource(string Pack,string File,string Definition,string Field="");
public sealed record ContentPatch(DefinitionSource Source,string Before,string After);
public sealed record ContentDependency(string Id,string MinVersion,bool Optional=false)
{ public string? MaxVersionExclusive {get;init;} }
public sealed record ContentFile(string Name,string Domain,XDocument Document);
public sealed record ContentPack(string Id,string Version,int Api,IReadOnlyList<ContentDependency> Dependencies,IReadOnlyList<ContentFile> Files);

// One immutable startup catalog. No process-directory guessing or live replacement of definitions.
public sealed partial class ContentCatalog
{
    public IReadOnlyDictionary<string,BlockDefinition> Blocks {get;}
    public IReadOnlyDictionary<string,BuildingDefinition> Buildings {get;}
    public IEnumerable<IContentDefinition> Entries=>Items.Cast<IContentDefinition>().Concat(Blocks.Values).Concat(Buildings.Values);
    public BlockDefinition Block(string id)=>Blocks.GetValueOrDefault(id)??throw new InvalidDataException("未知方块定义："+id);
    public BuildingDefinition Building(string id)=>Buildings.GetValueOrDefault(id)??throw new InvalidDataException("未知建筑定义："+id);
    public IReadOnlyList<ItemDefinition> Items {get;}
    public IReadOnlyList<ProcessingRecipe> Recipes {get;}
    public IReadOnlyList<ProductionMode> Modes {get;}
    public IReadOnlyDictionary<string,ItemDefinition> ItemsById {get;}
    public IReadOnlyDictionary<string,ProcessingRecipe> RecipesById {get;}
    public CreatureCatalog Creatures {get;}
    public IReadOnlyDictionary<string,StoryEventDefinition> StoryEvents {get;}
    public IReadOnlyDictionary<string,StoryEventDefinition> StoryTools {get;}
    public IReadOnlyList<ContentPackStamp> Packs {get;}
    public IReadOnlyDictionary<string,DefinitionSource> Sources {get;}
    public IReadOnlyList<ContentPatch> Patches {get;}
    public string GameplayHash {get;}
    public string Revision {get;}
    public IReadOnlyList<DefinitionStamp> Definitions {get;}
    public ItemDefinition Item(string id)=>ItemsById.GetValueOrDefault(id)??throw new InvalidDataException("未知物品定义："+id);
    public ProcessingRecipe Recipe(string id)=>FindRecipe(id)??throw new InvalidDataException("未知配方定义："+id);
    public ProcessingRecipe? FindRecipe(string id)=>RecipesById.GetValueOrDefault(id);
    public ProductionMode Mode(string id)=>Modes.FirstOrDefault(m=>m.Id==id)??throw new InvalidDataException("未知生产方式："+id);
    public ContentManifest Manifest()=>new(Packs.ToArray(),GameplayHash){Definitions=Definitions.ToArray()};
    private ContentCatalog(XDocument production,XDocument creatures,XDocument events,List<ContentPackStamp> packs,Dictionary<string,DefinitionSource> sources,List<ContentPatch> patches,XDocument? spatial=null)
    {
        var parsed=ProductionDefinitionParser.Parse(production);
        Items=Array.AsReadOnly(parsed.Item1);Recipes=Array.AsReadOnly(parsed.Item2);Modes=Array.AsReadOnly(parsed.Item3);
        ItemsById=new ReadOnlyDictionary<string,ItemDefinition>(Items.ToDictionary(i=>i.Id,StringComparer.Ordinal));RecipesById=new ReadOnlyDictionary<string,ProcessingRecipe>(Recipes.ToDictionary(r=>r.Id,StringComparer.Ordinal));
        spatial??=SpatialDefinitions.Core();
        (Blocks,Buildings)=SpatialDefinitions.Parse(spatial,ItemsById);
        Placements=Array.AsReadOnly(Items.SelectMany(i=>i.Placements).ToArray());
        foreach(var p in Placements)if(Block(p.Block) is not {Shape:BlockShape.Solid} b||!double.IsFinite(b.Durability)||b.Durability<=0)throw new InvalidDataException("放置需要可破坏的实心方块："+p.Block);
        Creatures=CreatureCatalog.Parse(creatures);Packs=packs.AsReadOnly();Sources=new ReadOnlyDictionary<string,DefinitionSource>(sources);Patches=patches.AsReadOnly();
        StoryEvents=StoryEventParser.Parse(events,Items,sources);
        StoryTools=new ReadOnlyDictionary<string,StoryEventDefinition>(StoryEvents.Values.ToDictionary(e=>e.Tool,StringComparer.Ordinal));
        if(System.Text.Json.JsonSerializer.Serialize(StoryEvents.Values.Select(e=>e.ToolSchema())).Length>24000)throw new InvalidDataException("叙事工具目录超过上下文预算，请减少事件或补给物品范围");
        GameplayHash=Hash(Semantic(production.Root!)+Semantic(creatures.Root!)+Semantic(events.Root!)+Semantic(spatial.Root!));
        Revision=Hash(production.ToString(SaveOptions.DisableFormatting)+creatures.ToString(SaveOptions.DisableFormatting)+events.ToString(SaveOptions.DisableFormatting)+spatial.ToString(SaveOptions.DisableFormatting)+string.Join(";",packs.Select(p=>p.Id+"@"+p.Version)));
        Definitions=Array.AsReadOnly((new[]{("production",production),("creatures",creatures),("events",events),("spatial",spatial)}).SelectMany(pair=>pair.Item2.Root!.Elements().SelectMany(section=>section.Elements().Select(node=>new DefinitionStamp(pair.Item1+"/"+section.Name,(string)node.Attribute("id")!,Hash(Semantic(node)))))).OrderBy(d=>d.Domain,StringComparer.Ordinal).ThenBy(d=>d.Id,StringComparer.Ordinal).ToArray());
    }
    internal static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static string Semantic(XElement element)
    {
        if(element.Name=="View")return "";
        string Value(XAttribute a)
        {
            if(a.Name.LocalName is "value" or "count" or "work" or "health" or "defaultCount" or "speed" or "stepUp" or "dropDown" or "maxWater" or "thinkSeconds" or "limit" or "populationLimit" or "updateBudget" or "weight" or "shelfLife" or "nutrition" or "mood" or "diningBonus" or "feedingMood" or "minCount" or "maxCount" or "minDelay" or "maxDelay" or "minDuration" or "maxDuration" or "durability" or "veins" or "size" or "minDepth" or "maxDepth")
                if(double.TryParse(a.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n))return n.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(a.Name.LocalName is "vital" or "ladders" or "storySupply" && bool.TryParse(a.Value,out bool b))return b?"true":"false";
            return a.Value;
        }
        var copy=new XElement(element);
        if(copy.Name=="Species"&&copy.Attribute("controller") is null)copy.SetAttributeValue("controller","wildlife");
        return "<"+copy.Name+string.Concat(copy.Attributes().Where(a=>a.Name.LocalName is not ("name" or "label" or "texture" or "color")&&!(a.Name.LocalName=="description"&&copy.Name.LocalName is "Item" or "Block" or "Building")).OrderBy(a=>a.Name.ToString(),StringComparer.Ordinal).Select(a=>" "+a.Name+"="+System.Text.Json.JsonSerializer.Serialize(Value(a))))+">"+string.Concat(copy.Elements().OrderBy(e=>e.Name.ToString(),StringComparer.Ordinal).ThenBy(e=>(string?)e.Attribute("id")??(string?)e.Attribute("item")??(string?)e.Attribute("ref")??"",StringComparer.Ordinal).Select(Semantic))+"</"+copy.Name+">";
    }
    public static XDocument ReadXml(Stream stream)
    {
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4_000_000});
        return XDocument.Load(reader,LoadOptions.SetLineInfo);
    }
    public static ContentPack Core()
    {
        XDocument Read(string file){using var s=typeof(ContentCatalog).Assembly.GetManifestResourceStream("NoDwarf."+file)??throw new InvalidDataException("缺少内置定义："+file);return ReadXml(s);}
        return new("base","1.0",1,Array.Empty<ContentDependency>(),new[]{new ContentFile("production.xml","production",Read("production.xml")),new ContentFile("creatures.xml","creatures",Read("creatures.xml")),new ContentFile("events.xml","events",Read("events.xml")),new ContentFile("spatial.xml","spatial",Read("spatial.xml"))});
    }
    public static ContentCatalog Load(IEnumerable<ContentPack> extensions,CreatureBehaviorRegistry? behaviors=null)
    {
        var packs=new Dictionary<string,ContentPack>(StringComparer.Ordinal);
        foreach(var pack in new[]{Core()}.Concat(extensions))
        {
            if(pack is null||!ValidId(pack.Id)||!Version.TryParse(pack.Version,out _)||pack.Api!=1||pack.Files is null||pack.Dependencies is null||pack.Files.Count>64||pack.Dependencies.Count>64||pack.Dependencies.Any(d=>d is null||!ValidId(d.Id))||pack.Dependencies.Select(d=>d.Id).Distinct().Count()!=pack.Dependencies.Count)throw new InvalidDataException("内容包清单无效或 API 不兼容");
            if(!packs.TryAdd(pack.Id,pack))throw new InvalidDataException("内容包 ID 重复："+pack.Id);
        }
        if(packs.Count>128)throw new InvalidDataException("内容包数量超过 128");
        var sorted=new List<ContentPack>();var visiting=new HashSet<string>();var done=new HashSet<string>();
        void Visit(string id)
        {
            if(done.Contains(id))return;
            if(!visiting.Add(id))throw new InvalidDataException("内容包依赖循环："+id);
            var p=packs[id];
            foreach(var d in p.Dependencies.OrderBy(d=>d.Id,StringComparer.Ordinal))
            {
                if(!Version.TryParse(d.MinVersion,out var min))throw new InvalidDataException(p.Id+" 依赖版本无效："+d.Id);
                Version? max=null;
                if(d.MaxVersionExclusive is {} maxText&&(!Version.TryParse(maxText,out max)||max<=min))throw new InvalidDataException(p.Id+" 依赖版本超出兼容范围："+d.Id);
                if(!packs.TryGetValue(d.Id,out var dep)){if(d.Optional)continue;throw new InvalidDataException(p.Id+" 缺少依赖 "+d.Id);}
                if(Version.Parse(dep.Version)<min)throw new InvalidDataException(p.Id+" 依赖版本过低："+d.Id+" >= "+min);
                if(max is not null&&Version.Parse(dep.Version)>=max)throw new InvalidDataException(p.Id+" 依赖版本超出兼容范围："+d.Id);
                Visit(d.Id);
            }
            visiting.Remove(id);done.Add(id);sorted.Add(p);
        }
        Visit("base");foreach(var id in packs.Keys)Visit(id);
        sorted=DefinitionDocuments.Prepare(sorted);
        var spatial=new XDocument(new XElement("SpatialDefinitions",new XElement("Blocks"),new XElement("Buildings")));
        var production=new XDocument(new XElement("ProductionDefinitions",new XElement("Items"),new XElement("Recipes"),new XElement("Modes")));
        var creatures=new XDocument(new XElement("Creatures",new XAttribute("version","1"),new XAttribute("populationLimit","256"),new XAttribute("updateBudget","32"),new XElement("Bodies"),new XElement("Movements"),new XElement("Species")));
        var events=new XDocument(new XElement("StoryEvents",new XElement("Events")));
        var sources=new Dictionary<string,DefinitionSource>(StringComparer.Ordinal);var patches=new List<ContentPatch>();
        XDocument Domain(string name)=>name switch {"production"=>production,"creatures"=>creatures,"events"=>events,"spatial"=>spatial,_=>throw new InvalidDataException("未知定义领域："+name)};
        foreach(var pack in sorted)foreach(var file in pack.Files.OrderBy(f=>f.Domain=="patches"?1:0))
        {
            if(file is null||string.IsNullOrWhiteSpace(file.Name)||file.Document is null)throw new InvalidDataException(pack.Id+" 内容文件无效");
            var root=file.Document.Root??throw new InvalidDataException(pack.Id+"/"+file.Name+" XML 为空");
            try
            {
                if(file.Domain=="patches")
                {
                    if(root.Name!="Patches")throw new InvalidDataException("需要 Patches 根节点");
                    Shape(root,[],["Set","Append","Disable"]);
                    foreach(var op in root.Elements())
                    {
                        if(op.Name=="Set")Shape(op,["domain","section","id","field","expect","value"],[]);
                        else if(op.Name=="Append")Shape(op,["domain","section","id"],["Ingredient","Part","Capability","Item"]);
                        else Shape(op,["domain","section","id"],[]);
                        string domain=Required(op,"domain"),section=Required(op,"section"),id=Required(op,"id"),key=domain+"/"+section+"/"+id;
                        var node=Domain(domain).Root?.Element(section)?.Elements().SingleOrDefault(n=>(string?)n.Attribute("id")==id)??throw new InvalidDataException("补丁目标不存在："+key);
                        string field=(string?)op.Attribute("field")??"";string before="",after="";
                        if(op.Name=="Set")
                        {
                            if(field=="id"||field.Contains('/')||field.Contains(':')||field.Length==0)throw new InvalidDataException("Set 只能改当前定义的非 ID 属性");
                            before=(string?)node.Attribute(field)??"";after=Required(op,"value");
                            if(before!=Required(op,"expect"))throw new InvalidDataException("补丁前值冲突："+key+"."+field+" 当前="+before+"，来源 "+sources.GetValueOrDefault(key+"/"+field,sources[key]));
                            if(node.Attribute(field) is null)throw new InvalidDataException("未知字段："+key+"."+field);
                            node.SetAttributeValue(field,after);
                        }
                        else if(op.Name=="Append")
                        {
                            var child=op.Elements().SingleOrDefault()??throw new InvalidDataException("Append 需要一个子项");
                            bool allowed=section=="Bodies"&&child.Name=="Part"||section=="Recipes"&&child.Name=="Ingredient"||section=="Species"&&child.Name=="Capability"||section=="Events"&&child.Name=="Item";
                            if(!allowed)throw new InvalidDataException("不支持的追加类型："+key+"/"+child.Name);
                            var parent=section=="Species"?node.Element("Capabilities"):node;
                            if(parent is null){parent=new XElement("Capabilities");node.Add(parent);}
                            field=child.Name+":"+((string?)child.Attribute("id")??(string?)child.Attribute("item")??(string?)child.Attribute("ref"));after=child.ToString(SaveOptions.DisableFormatting);parent.Add(new XElement(child));
                        }
                        else if(op.Name=="Disable")
                        {
                            if(sources[key].Pack=="base"&&domain is "production" or "events" or "spatial")throw new InvalidDataException("不能禁用核心生产定义："+key);
                            before=node.ToString(SaveOptions.DisableFormatting);node.Remove();
                        }
                        else throw new InvalidDataException("未知补丁操作："+op.Name);
                        var origin=new DefinitionSource(pack.Id,file.Name,id,field);sources[key+"/"+field]=origin;patches.Add(new(origin,before,after));
                    }
                    continue;
                }
                var target=Domain(file.Domain).Root!;
                if(root.Name!=target.Name)throw new InvalidDataException("定义根节点错误："+root.Name);
                Shape(root,file.Domain=="creatures"?(pack.Id=="base"?["version","populationLimit","updateBudget"]:["version"]):[],target.Elements().Select(e=>e.Name.LocalName).ToArray());
                if(file.Domain=="creatures"&&root.Attribute("version") is {} version&&version.Value!="1")throw new InvalidDataException("生物定义版本不兼容");
                if(pack.Id=="base"&&file.Domain=="creatures")foreach(var a in root.Attributes())target.SetAttributeValue(a.Name,a.Value);
                foreach(var section in root.Elements())
                {
                    var dest=target.Element(section.Name)??throw new InvalidDataException("未知定义分组："+section.Name);
                    if(section.HasAttributes||section.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException("定义分组存在未支持的字段或文本："+section.Name);
                    foreach(var node in section.Elements())
                    {
                        string id=Required(node,"id"),key=file.Domain+"/"+section.Name+"/"+id;
                        string expected=section.Name.LocalName switch {"Items"=>"Item","Recipes"=>"Recipe","Modes"=>"Mode","Bodies"=>"Body","Movements"=>"Movement","Species"=>"Species","Events"=>"Event","Blocks"=>"Block","Buildings"=>"Building",_=>""};
                        if(node.Name!=expected)throw new InvalidDataException("未知定义类型："+node.Name);
                        if(pack.Id!="base"&&(!id.StartsWith(pack.Id+":",StringComparison.Ordinal)||!ValidId(id.Replace(':','_'))))throw new InvalidDataException("新增定义必须使用包命名空间："+id);
                        if(!sources.TryAdd(key,new(pack.Id,file.Name,id)))throw new InvalidDataException("定义 ID 重复，需显式补丁："+key+"，原来源="+sources[key]);
                        dest.Add(new XElement(node));
                    }
                }
            }
            catch(Exception ex) when(ex is InvalidDataException or ArgumentException or InvalidOperationException)
            {throw new InvalidDataException(pack.Id+"/"+file.Name+"："+ex.Message,ex);}
        }
        // Validate each resolved definition before publishing; errors retain pack/file/definition/field.
        foreach(var pair in new[]{("production",production),("creatures",creatures)})
        foreach(var section in pair.Item2.Root!.Elements())foreach(var node in section.Elements())
        {
            string key=pair.Item1+"/"+section.Name+"/"+Required(node,"id");
            DefinitionSource Origin(string field)=>sources.GetValueOrDefault(key+"/"+field,sources[key]);
            void Ref(string field,string? value,string targetSection)
            {
                string sourceField=field=="Ingredient.item"?"Ingredient:"+value:field;
                if(!pair.Item2.Root.Element(targetSection)!.Elements().Any(e=>(string?)e.Attribute("id")==value))throw new InvalidDataException(Origin(sourceField)+" 字段 "+field+" 引用不存在："+value);
            }
            string[] allowed=node.Name.LocalName switch
            {
                "Item"=>new[]{"id","name","value","texture","color","category","weight","shelfLife","storySupply","description","parent","categories","tags"},
                "Recipe"=>new[]{"id","name","station","output","count","work"},
                "Mode"=>new[]{"id","label","behavior","defaultCount"},
                "Body"=>new[]{"id"},
                "Movement"=>new[]{"id","speed","stepUp","dropDown","ladders","maxWater"},
                "Species"=>new[]{"id","name","body","movement","behavior","controller","thinkSeconds","limit"},_=>Array.Empty<string>()
            };
            foreach(var attribute in node.Attributes())if(attribute.Name.Namespace!=XNamespace.None||!allowed.Contains(attribute.Name.LocalName))throw new InvalidDataException(Origin(attribute.Name.LocalName)+" 未支持的字段 "+attribute.Name);
            ValidateNested(node,origin:Origin(""));
            foreach(string field in allowed.Where(f=>f is not ("controller" or "category" or "weight" or "shelfLife" or "storySupply" or "description" or "parent" or "categories" or "tags")))
            {
                if(node.Attribute(field)?.Value is not {Length:>0})throw new InvalidDataException(Origin(field)+" 缺少字段 "+field);
                if(field is "value" or "count" or "work" or "defaultCount" or "speed" or "stepUp" or "dropDown" or "maxWater" or "thinkSeconds" or "limit")
                    if(!double.TryParse((string?)node.Attribute(field),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number)||!double.IsFinite(number)||number<0)throw new InvalidDataException(Origin(field)+" 数值无效："+field);
            }
            if(section.Name=="Items"&&node.Attribute("value") is null)throw new InvalidDataException(sources[key]+" 缺少字段 value");
            if(section.Name=="Recipes"){Ref("output",(string?)node.Attribute("output"),"Items");foreach(var ingredient in node.Elements("Ingredient"))Ref("Ingredient.item",(string?)ingredient.Attribute("item"),"Items");}
            if(section.Name=="Species")
            {
                Ref("body",(string?)node.Attribute("body"),"Bodies");Ref("movement",(string?)node.Attribute("movement"),"Movements");
                if(!(behaviors??=CreatureBehaviorRegistry.Standard()).Contains(Required(node,"behavior")))throw new InvalidDataException(sources[key]+" 字段 behavior 未注册："+Required(node,"behavior"));
            }
        }
        foreach(var node in production.Root!.Element("Items")!.Elements())
        {
            string id=Required(node,"id");
            try{ItemUseParser.Read(node,new(id,"","",""){BaseValue=double.Parse(Required(node,"value"),System.Globalization.CultureInfo.InvariantCulture)});}
            catch(Exception ex) when(ex is InvalidDataException or FormatException or OverflowException){throw new InvalidDataException(sources["production/Items/"+id]+"："+ex.Message,ex);}
        }
        try{return new(production,creatures,events,sorted.Select(p=>new ContentPackStamp(p.Id,p.Version)).ToList(),sources,patches,spatial:spatial);}
        catch(Exception e) when(e is InvalidDataException or ArgumentException or NullReferenceException)
        {throw new InvalidDataException("内容目录校验失败（加载包："+string.Join(", ",sorted.Select(p=>p.Id))+"）："+e.Message,e);}
    }
    private static string Required(XElement e,string field)=>e.Attribute(field)?.Value is {Length:>0} v?v:throw new InvalidDataException(e.Name+" 缺少字段 "+field);
    private static bool ValidId(string id)=>id is {Length:>0 and <=100}&&id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-' or '.');
    public static ContentCatalog LoadDirectory(string absoluteRoot)
    {
        if(!Path.IsPathFullyQualified(absoluteRoot))throw new InvalidDataException("内容根目录必须是绝对路径");
        string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(absoluteRoot));
        string FileAt(string folder,string relative)
        {
            string path=Path.GetFullPath(Path.Combine(folder,relative));
            if(!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.Ordinal)||Path.IsPathRooted(relative))throw new InvalidDataException("内容文件越出包目录："+relative);
            for(string? current=path;current is not null&&current!=folder;current=Path.GetDirectoryName(current))
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("内容包内不允许符号链接："+relative);
            return path;
        }
        XDocument Read(string path)
        {try{using var s=File.OpenRead(path);return ReadXml(s);}catch(Exception e) when(e is IOException or XmlException or UnauthorizedAccessException){throw new InvalidDataException(path+"："+e.Message,e);}}
        var config=Read(FileAt(root,"content.xml"));if(config.Root?.Name!="Content")throw new InvalidDataException("需要 Content 清单");
        Shape(config.Root,[],["Pack"]);
        if(config.Root.Elements().Count()>127)throw new InvalidDataException("内容包数量超过限制");
        var packs=new List<ContentPack>();
        foreach(var entry in config.Root.Elements("Pack"))
        {
            Shape(entry,["path"],[]);
            string folder=FileAt(root,Required(entry,"path"));var doc=Read(FileAt(folder,"pack.xml"));var p=doc.Root!;
            if(p.Name!="Pack"||!int.TryParse(Required(p,"api"),out int api))throw new InvalidDataException(folder+"/pack.xml 清单无效");
            Shape(p,["id","version","api"],["Depends","Definitions","Scan"]);
            if(p.Elements("Definitions").Count()>64||p.Elements("Depends").Count()>64)throw new InvalidDataException(folder+"/pack.xml 条目过多");
            foreach(var d in p.Elements("Depends"))
            {Shape(d,["id","minVersion","maxVersionExclusive","optional"],[]);if(d.Attribute("optional") is {} a&&!bool.TryParse(a.Value,out _))throw new InvalidDataException(folder+" optional 必须为布尔值");}
            foreach(var f in p.Elements("Definitions"))Shape(f,["file","domain"],[]);
            var deps=p.Elements("Depends").Select(d=>new ContentDependency(Required(d,"id"),Required(d,"minVersion"),bool.Parse((string?)d.Attribute("optional")??"false")){MaxVersionExclusive=(string?)d.Attribute("maxVersionExclusive")}).ToArray();
            var files=p.Elements("Definitions").Select(f=>new ContentFile(Required(f,"file"),Required(f,"domain"),Read(FileAt(folder,Required(f,"file"))))).ToArray();
            var allFiles=files.ToList();
            foreach(var scan in p.Elements("Scan"))
            {
                Shape(scan,["path"],[]);string scanRoot=FileAt(folder,Required(scan,"path"));
                if(!Directory.Exists(scanRoot))throw new InvalidDataException("定义目录不存在："+scanRoot);
                void Scan(string directory,int depth)
                {
                    if(depth>16)throw new InvalidDataException("定义目录层数过多");
                    foreach(string entryPath in Directory.EnumerateFileSystemEntries(directory).OrderBy(x=>x,StringComparer.Ordinal))
                    {
                        string relative=Path.GetRelativePath(folder,entryPath);string checkedPath=FileAt(folder,relative);
                        if(Directory.Exists(checkedPath)){Scan(checkedPath,depth+1);continue;}
                        if(!string.Equals(Path.GetExtension(checkedPath),".xml",StringComparison.OrdinalIgnoreCase))continue;
                        if(allFiles.Count>=64||allFiles.Any(f=>f.Name==relative))throw new InvalidDataException("定义文件重复或超过 64："+relative);
                        var xml=Read(checkedPath);string domain=xml.Root?.Name.LocalName switch
                        {"Defs"=>"defs","ProductionDefinitions"=>"production","SpatialDefinitions"=>"spatial","Creatures"=>"creatures","StoryEvents"=>"events","Patches"=>"patches",_=>throw new InvalidDataException("未知定义文件："+relative)};
                        allFiles.Add(new(relative,domain,xml));
                    }
                }
                Scan(scanRoot,0);
            }
            packs.Add(new(Required(p,"id"),Required(p,"version"),api,deps,allFiles));
        }
        return Load(packs);
    }
    private static void Shape(XElement e,string[] attributes,string[] children)
    {
        if(e.Attributes().Any(a=>a.Name.Namespace!=XNamespace.None||!attributes.Contains(a.Name.LocalName))||e.Elements().Any(c=>c.Name.Namespace!=XNamespace.None||!children.Contains(c.Name.LocalName))||e.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException(e.Name+" 存在未支持的字段或子项");
    }
    private static void ValidateNested(XElement node,DefinitionSource origin)
    {
        try
        {
            string[] children=node.Name.LocalName switch {"Item"=>["Edible","Place"],"Recipe"=>["Ingredient"],"Body"=>["Part"],"Species"=>["View","Capabilities"],_=>[]};
            if(node.Elements().Any(c=>c.Name.Namespace!=XNamespace.None||!children.Contains(c.Name.LocalName))||node.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException("未知子项或文本");
            foreach(var c in node.Elements())switch(c.Name.LocalName)
            {
                case "Place":Shape(c,["block","count","support"],[]);break;
                case "Edible":Shape(c,["nutrition","mood","diningBonus","feedingMood"],[]);break;
                case "Ingredient":Shape(c,["item","count"],[]);break;
                case "Part":Shape(c,["id","health","vital"],[]);break;
                case "View":Shape(c,["kind","resource","texture","scale"],[]);break;
                case "Capabilities":Shape(c,[],["Capability"]);foreach(var capability in c.Elements())Shape(capability,["id"],[]);break;
            }
            if(node.Elements("View").Count()>1||node.Elements("Capabilities").Count()>1)throw new InvalidDataException("重复的表现／能力分组");
        }
        catch(InvalidDataException e){throw new InvalidDataException(origin+"："+e.Message,e);}
    }
}
// Host-owned publication point for title-screen/session setup. Existing worlds keep
// their catalog reference; a failed reload never changes the last usable catalog.
public sealed class ContentCatalogStore(ContentCatalog initial)
{
    private ContentCatalog current=initial??throw new ArgumentNullException(nameof(initial));
    public ContentCatalog Current=>Volatile.Read(ref current);
    public bool TryLoadDirectory(string absoluteRoot,out string error)
    {
        try{var prepared=ContentCatalog.LoadDirectory(absoluteRoot);Interlocked.Exchange(ref current,prepared);error="";return true;}
        catch(Exception e) when(e is InvalidDataException or IOException or XmlException or ArgumentException or UnauthorizedAccessException){error=e.Message;return false;}
    }
}
public static class ContentRuntime
{
    private static readonly Lazy<ContentCatalog> Catalog=new(()=>Environment.GetEnvironmentVariable("NODWARF_CONTENT_ROOT") is {Length:>0} root?ContentCatalog.LoadDirectory(root):ContentCatalog.Load(Array.Empty<ContentPack>()));
    public static ContentCatalog Current=>Catalog.Value;
}
