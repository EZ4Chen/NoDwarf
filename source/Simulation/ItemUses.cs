using System.Globalization;
using System.Xml.Linq;
namespace NoDwarf.Simulation;

public sealed record EdibleDefinition(double Nutrition,int Mood,int DiningBonus,int FeedingMood);
internal static class ItemUseParser
{
    internal static ItemDefinition Read(XElement e,ItemDefinition item)
    {
        string Context(string field)=>"物品 "+item.Id+" 字段 "+field;
        double Number(XElement node,string field,double fallback,double min,double max)
        {
            if(node.Attribute(field) is null)return fallback;
            if(!double.TryParse((string?)node.Attribute(field),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<min||n>max)throw new InvalidDataException(Context(field)+" 越界");return n;
        }
        int Integer(XElement node,string field,int fallback,int min,int max)
        {double n=Number(node,field,fallback,min,max);if(n!=Math.Truncate(n))throw new InvalidDataException(Context(field)+" 必须是整数");return (int)n;}
        string category=(string?)e.Attribute("category")??"misc";
        if(category.Length is <1 or >100||category.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ':' and not '_' and not '-' and not '.'))throw new InvalidDataException(Context("category")+" 无效");
        bool supply=false;if(e.Attribute("storySupply") is {} flag&&!bool.TryParse(flag.Value,out supply))throw new InvalidDataException(Context("storySupply")+" 需要布尔值");
        if(supply&&item.BaseValue<=0)throw new InvalidDataException(Context("storySupply")+" 需要大于零的 value");
        if(e.Elements().Any(n=>n.Name!="Edible"&&n.Name!="Place")||e.Elements("Edible").Count()>1)throw new InvalidDataException(Context("Edible")+" 用途未注册或重复");
        EdibleDefinition? edible=null;
        if(e.Element("Edible") is {} food)
        {
            if(food.Elements().Any()||food.Attributes().Any(a=>a.Name.LocalName is not ("nutrition" or "mood" or "diningBonus" or "feedingMood"))||food.Attribute("nutrition") is null)throw new InvalidDataException(Context("Edible")+" 属性无效或缺少 nutrition");
            edible=new(Number(food,"nutrition",0,.1,100),Integer(food,"mood",4,-20,20),Integer(food,"diningBonus",2,0,10),Integer(food,"feedingMood",4,-20,20));
        }
        var placements=new List<PlacementDefinition>();
        foreach(var place in e.Elements("Place"))
        {
            if(place.Elements().Any()||place.Attributes().Any(a=>a.Name.Namespace!=XNamespace.None||a.Name.LocalName is not ("block" or "count" or "support"))||place.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw new InvalidDataException(Context("Place")+" 字段无效");
            string block=(string?)place.Attribute("block")??"",support=(string?)place.Attribute("support")??"below";
            if(block.Length is <1 or >100||block.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not ':' and not '_' and not '-' and not '.')||support is not ("below" or "adjacent")||placements.Any(p=>p.Block==block)||placements.Count>=32)throw new InvalidDataException(Context("Place")+" 目标／支撑无效或重复");
            placements.Add(new(item.Id,block,Integer(place,"count",1,1,10000),support));
        }
        return item with{Placements=placements.AsReadOnly(),Info=DefinitionInfo.Read(e),Category=category,Weight=Number(e,"weight",1,.01,1000),ShelfLife=Number(e,"shelfLife",0,0,1e9),StorySupply=supply,Edible=edible};
    }
}
public sealed partial class DemoWorld
{
    // Classification is descriptive; only a registered capability authorizes use. Expiry is checked at commit too.
    public bool CanEat(ItemStack stack)=>stack.Count>0&&stack.Slot==""&&Content.Item(stack.Item).Edible is not null&&
        (stack.Properties.ExpiresAt is null||stack.Properties.ExpiresAt>SimulationTime);
    public int AvailableFood()=>Items.Where(i=>i.Owner==""&&CanEat(i)).Sum(i=>Math.Max(0,i.Count-Reserved(i.Id)));
    private ItemStack? PersonalFood(DemoWorker worker,int itemId=0)=>Inventory(worker).Where(i=>CanEat(i)&&(itemId==0||i.Id==itemId))
        .OrderBy(i=>i.Properties.ExpiresAt??double.MaxValue).ThenBy(i=>i.Id).FirstOrDefault();
    private void EatPersonal(DemoWorker worker,ResidentActivity activity,bool comfort=true)
    {
        var food=PersonalFood(worker,activity.ItemId);if(food is null||worker.Person.Hunger>=95)return;
        var effect=Content.Item(food.Item).Edible!;bool seated=comfort&&DiningSeats(worker).Contains(worker.Tile);
        food.Count--;if(food.Count==0)Items.Remove(food);RefreshWeight(worker);Revision++;
        worker.Person.Hunger=Math.Min(100,worker.Person.Hunger+effect.Nutrition);worker.Person.FoodIssue="";
        if(comfort)worker.Person.Feeling("meal",(seated?"坐在餐桌旁吃了":"吃了")+Content.Item(food.Item).Name,effect.Mood+(seated?effect.DiningBonus:0),SimulationTime+180);
    }
}
