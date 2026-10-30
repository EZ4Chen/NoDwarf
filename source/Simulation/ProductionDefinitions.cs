using System.Xml;
using System.Xml.Linq;
namespace NoDwarf.Simulation;
public sealed record ProductionMode(string Id,string Label,string Behavior,int DefaultCount);
public static class ProductionDefinitions
{
    public static IReadOnlyList<ItemDefinition> Items=>ContentRuntime.Current.Items;
    public static IReadOnlyList<ProcessingRecipe> Recipes=>ContentRuntime.Current.Recipes;
    public static IReadOnlyList<ProductionMode> Modes=>ContentRuntime.Current.Modes;
    public static (ItemDefinition[],ProcessingRecipe[],ProductionMode[]) Parse(XDocument doc)=>ProductionDefinitionParser.Parse(doc);
    public static ProductionMode Mode(string id)=>Modes.FirstOrDefault(m=>m.Id==id)??throw new ArgumentException("未知制造方式："+id);
}
internal static class ProductionDefinitionParser
{
    public static (ItemDefinition[],ProcessingRecipe[],ProductionMode[]) Parse(XDocument doc)
    {
        string A(XElement e,string a)=>e.Attribute(a)?.Value is {Length:>0} v?v:throw new InvalidDataException("生产 XML 缺少 "+a);
        int N(XElement e,string a)=>int.TryParse(A(e,a),out int n)&&n>0&&n<=10000?n:throw new InvalidDataException("生产 XML 数值无效："+a);
        double Value(XElement e)
        {
            if(!double.TryParse(A(e,"value"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v)||v<0||v>1_000_000)throw new InvalidDataException("物品估值无效："+A(e,"id")+".value");return v;
        }
        var root=doc.Root??throw new InvalidDataException("生产 XML 为空");
        var items=root.Element("Items")!.Elements("Item").Select(e=>ItemUseParser.Read(e,new ItemDefinition(A(e,"id"),A(e,"name"),A(e,"texture"),A(e,"color")){BaseValue=Value(e)})).ToArray();
        var recipes=root.Element("Recipes")!.Elements("Recipe").Select(e=>
        {
            if(!Enum.TryParse<BuildKind>(A(e,"station"),out var station)||!Enum.IsDefined(station))throw new InvalidDataException("未知生产设施");
            var inputs=e.Elements("Ingredient").ToDictionary(i=>A(i,"item"),i=>N(i,"count"));
            return new ProcessingRecipe(A(e,"id"),A(e,"name"),station,new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(inputs),A(e,"output"),N(e,"count")){Work=N(e,"work")};
        }).ToArray();
        var modes=root.Element("Modes")!.Elements("Mode").Select(e=>new ProductionMode(A(e,"id"),A(e,"label"),A(e,"behavior"),N(e,"defaultCount"))).ToArray();
        if(items.Length==0||recipes.Length==0||items.Select(i=>i.Id).Distinct().Count()!=items.Length||recipes.Select(r=>r.Id).Distinct().Count()!=recipes.Length||modes.Select(m=>m.Id).Distinct().Count()!=modes.Length||modes.Length==0)throw new InvalidDataException("生产 XML 定义为空或 ID 重复");
        if(recipes.Any(r=>r.Inputs.Count==0||!items.Any(i=>i.Id==r.Output)||r.Inputs.Keys.Any(k=>!items.Any(i=>i.Id==k)))||modes.Any(m=>m.Behavior is not ("count" or "maintain" or "forever")))throw new InvalidDataException("生产 XML 引用了未知物品或制造行为");
        return (items,recipes,modes);
    }
}
