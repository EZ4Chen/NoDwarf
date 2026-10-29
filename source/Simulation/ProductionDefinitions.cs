using System.Xml;
using System.Xml.Linq;
namespace NoDwarf.Simulation;
public sealed record ProductionMode(string Id,string Label,string Behavior,int DefaultCount);
public static class ProductionDefinitions
{
    public static readonly ItemDefinition[] Items;
    public static readonly ProcessingRecipe[] Recipes;
    public static readonly ProductionMode[] Modes;
    static ProductionDefinitions()
    {
        // Loose XML is editable without rebuilding; embedded XML is the export fallback.
        string? path=new[]{Path.Combine(Environment.CurrentDirectory,"Assets/Definitions/production.xml"),Path.Combine(Environment.CurrentDirectory,"source/Assets/Definitions/production.xml"),Path.Combine(AppContext.BaseDirectory,"Assets/Definitions/production.xml")}.FirstOrDefault(File.Exists);
        using var stream=path is not null?File.OpenRead(path):typeof(ProductionDefinitions).Assembly.GetManifestResourceStream("NoDwarf.production.xml")??throw new InvalidDataException("缺少 production.xml");
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
        (Items,Recipes,Modes)=Parse(XDocument.Load(reader));
    }
    public static (ItemDefinition[],ProcessingRecipe[],ProductionMode[]) Parse(XDocument doc)
    {
        string A(XElement e,string a)=>e.Attribute(a)?.Value is {Length:>0} v?v:throw new InvalidDataException("生产 XML 缺少 "+a);
        int N(XElement e,string a)=>int.TryParse(A(e,a),out int n)&&n>0&&n<=10000?n:throw new InvalidDataException("生产 XML 数值无效："+a);
        var root=doc.Root??throw new InvalidDataException("生产 XML 为空");
        var items=root.Element("Items")!.Elements("Item").Select(e=>new ItemDefinition(A(e,"id"),A(e,"name"),A(e,"texture"),A(e,"color"))).ToArray();
        var recipes=root.Element("Recipes")!.Elements("Recipe").Select(e=>
        {
            if(!Enum.TryParse<BuildKind>(A(e,"station"),out var station)||!Enum.IsDefined(station))throw new InvalidDataException("未知生产设施");
            var inputs=e.Elements("Ingredient").ToDictionary(i=>A(i,"item"),i=>N(i,"count"));
            return new ProcessingRecipe(A(e,"id"),A(e,"name"),station,inputs,A(e,"output"),N(e,"count")){Work=N(e,"work")};
        }).ToArray();
        var modes=root.Element("Modes")!.Elements("Mode").Select(e=>new ProductionMode(A(e,"id"),A(e,"label"),A(e,"behavior"),N(e,"defaultCount"))).ToArray();
        if(items.Length==0||recipes.Length==0||items.Select(i=>i.Id).Distinct().Count()!=items.Length||recipes.Select(r=>r.Id).Distinct().Count()!=recipes.Length||modes.Select(m=>m.Id).Distinct().Count()!=modes.Length||modes.Length==0)throw new InvalidDataException("生产 XML 定义为空或 ID 重复");
        if(recipes.Any(r=>r.Inputs.Count==0||!items.Any(i=>i.Id==r.Output)||r.Inputs.Keys.Any(k=>!items.Any(i=>i.Id==k)))||modes.Any(m=>m.Behavior is not ("count" or "maintain" or "forever")))throw new InvalidDataException("生产 XML 引用了未知物品或制造行为");
        return (items,recipes,modes);
    }
    public static ProductionMode Mode(string id)=>Modes.FirstOrDefault(m=>m.Id==id)??throw new ArgumentException("未知制造方式："+id);
}
