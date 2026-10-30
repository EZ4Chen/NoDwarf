using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
namespace NoDwarf.Simulation;

public sealed record OreGeneration(int Veins,int Size,int MinDepth,int MaxDepth,IReadOnlyList<string> Hosts);
public sealed record BuildingDefinition(string Id,string Name,string Block,BuildKind Handler,IReadOnlyDictionary<string,int> Cost,DefinitionInfo Info):IContentDefinition;
internal static class SpatialDefinitions
{
    internal static XDocument Core()
    {using var stream=typeof(ContentCatalog).Assembly.GetManifestResourceStream("NoDwarf.spatial.xml")??throw new InvalidDataException("缺少方块定义");return ContentCatalog.ReadXml(stream);}
    internal static (IReadOnlyDictionary<string,BlockDefinition>,IReadOnlyDictionary<string,BuildingDefinition>) Parse(XDocument doc,IReadOnlyDictionary<string,ItemDefinition> items)
    {
        void Shape(XElement e,string[] attrs,string[] children)
        {if(e.Attributes().Any(a=>a.Name.Namespace!=XNamespace.None||!attrs.Concat(DefinitionInfo.Attributes).Contains(a.Name.LocalName))||e.Elements().Any(c=>!children.Contains(c.Name.LocalName))||e.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException("未知空间定义字段或子项："+e);}
        string Text(XElement e,string a)=>e.Attribute(a)?.Value is {Length:>0 and <=256} s?s:throw new InvalidDataException("空间定义缺少字段："+e.Name+"."+a);
        double Number(XElement e,string a,double fallback,double min,double max)
        {if(e.Attribute(a) is null)return fallback;if(!double.TryParse((string?)e.Attribute(a),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<min||n>max)throw new InvalidDataException("空间数值越界："+e.Name+"."+a);return n;}
        int Integer(XElement e,string a,int fallback,int min,int max)
        {double n=Number(e,a,fallback,min,max);if(n!=Math.Truncate(n))throw new InvalidDataException("需要整数："+a);return (int)n;}
        IReadOnlyDictionary<string,int> Resources(XElement e,string child)
        {
            var d=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var r in e.Elements(child)){Shape(r,["item","count"],[]);string id=Text(r,"item");if(!items.ContainsKey(id)||!d.TryAdd(id,Integer(r,"count",1,1,10000)))throw new InvalidDataException("物品引用不存在或重复："+id);}
            return new ReadOnlyDictionary<string,int>(d);
        }
        var blocks=new Dictionary<string,BlockDefinition>(StringComparer.Ordinal);
        foreach(var e in doc.Root!.Element("Blocks")!.Elements())
        {
            Shape(e,["id","name","shape","color","durability","indestructible"],["Drop","Ore"]);
            string id=Text(e,"id"),color=Text(e,"color");
            if(!Enum.TryParse<BlockShape>(Text(e,"shape"),out var shape)||!Enum.IsDefined(shape)||color.Length!=6||!color.All(Uri.IsHexDigit))throw new InvalidDataException("方块形状或颜色无效："+id);
            bool indestructible=false;if(e.Attribute("indestructible") is {} flag&&!bool.TryParse(flag.Value,out indestructible))throw new InvalidDataException("indestructible 需要布尔值");
            OreGeneration? generation=null;
            if(e.Elements("Ore").Count()>1)throw new InvalidDataException("重复矿脉规则："+id);
            if(e.Element("Ore") is {} ore)
            {
                Shape(ore,["veins","size","minDepth","maxDepth","hosts"],[]);
                if(shape!=BlockShape.Solid)throw new InvalidDataException("矿脉需要实心方块");
                int min=Integer(ore,"minDepth",1,1,128),max=Integer(ore,"maxDepth",min,min,128);
                var hosts=Text(ore,"hosts").Split(' ',StringSplitOptions.RemoveEmptyEntries);
                generation=new(Integer(ore,"veins",16,1,1024),Integer(ore,"size",8,1,128),min,max,Array.AsReadOnly(hosts));
            }
            blocks.Add(id,new(id,Text(e,"name"),shape,color){Durability=indestructible?double.PositiveInfinity:Number(e,"durability",100,shape is BlockShape.Solid or BlockShape.Ramp ? .1 : 0,1000000),Info=DefinitionInfo.Read(e),Drops=Resources(e,"Drop"),Generation=generation});
        }
        if(blocks.Values.Sum(b=>(long)(b.Generation?.Veins??0)*(b.Generation?.Size??0))>200000)throw new InvalidDataException("矿脉生成预算超过 200000 格");
        foreach(var block in blocks.Values)if(block.Generation is {} g&&g.Hosts.Any(h=>!blocks.TryGetValue(h,out var host)||host.Shape!=BlockShape.Solid))throw new InvalidDataException("矿脉母岩引用无效："+block.Id);
        var buildings=new Dictionary<string,BuildingDefinition>(StringComparer.Ordinal);
        foreach(var e in doc.Root.Element("Buildings")!.Elements())
        {
            Shape(e,["id","name","block","handler"],["Cost"]);
            string id=Text(e,"id"),block=Text(e,"block");
            if(!Enum.TryParse<BuildKind>(Text(e,"handler"),out var handler)||!Enum.IsDefined(handler)||!blocks.TryGetValue(block,out var voxel))throw new InvalidDataException("建筑处理器或方块引用无效："+id);
            // Shapes are part of existing handlers' geometry contracts.
            var expected=BlockCatalog.IsWall(handler)?BlockShape.Solid:BlockCatalog.IsStairs(handler)?BlockShape.Stairs:BlockShape.Fixture;
            if(voxel.Shape!=expected||!double.IsFinite(voxel.Durability)||voxel.Durability<=0)throw new InvalidDataException("建筑形状／耐久与处理器不兼容："+id);
            var cost=Resources(e,"Cost");if(cost.Count==0)throw new InvalidDataException("建筑缺少成本："+id);
            buildings.Add(id,new(id,Text(e,"name"),block,handler,cost,DefinitionInfo.Read(e)));
        }
        foreach(BuildKind kind in Enum.GetValues<BuildKind>())if(!buildings.TryGetValue(BlockCatalog.Id(kind),out var b)||b.Handler!=kind)throw new InvalidDataException("核心建筑处理器映射无效："+kind);
        // Keep the old cost facade for callers which only accept core BuildKind.
        foreach(var b in buildings.Values.Where(b=>b.Id==BlockCatalog.Id(b.Handler)))blocks[b.Block]=blocks[b.Block] with{Cost=new(b.Cost.GetValueOrDefault("cobblestone"),b.Cost.GetValueOrDefault("wood"),b.Cost.GetValueOrDefault("iron"))};
        return (new ReadOnlyDictionary<string,BlockDefinition>(blocks),new ReadOnlyDictionary<string,BuildingDefinition>(buildings));
    }
}
