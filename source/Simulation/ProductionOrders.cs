namespace NoDwarf.Simulation;

public sealed class ProductionOrder
{
    public string Recipe {get;set;}="";
    public int Target {get;set;}
    public bool Enabled {get;set;}=true;
}
public static class ProductionCatalog
{
    public static readonly ProcessingRecipe[] All=ProcessingCatalog.All.Concat(new[]{
        new ProcessingRecipe("cook","烹饪：2 食材 + 1 木料 → 2 口粮",BuildKind.Stove,new(){{"raw_food",2},{"wood",1}},"meal",2),
        new ProcessingRecipe("smelt","熔炼：2 粗铁 + 1 煤 → 2 铁锭",BuildKind.Furnace,new(){{"raw_iron",2},{"coal",1}},"iron",2),
        new ProcessingRecipe("sword","锻造：2 铁锭 + 1 煤 → 铁剑",BuildKind.Anvil,new(){{"iron",2},{"coal",1}},"sword",1)}).ToArray();
    public static ProcessingRecipe Get(string id)=>All.FirstOrDefault(r=>r.Id==id)??throw new ArgumentException("未知生产配方。");
    public static string RecipeOf(DemoJob j)=>j.Processing?j.Recipe:j.Cook?"cook":j.Smelt?"smelt":j.Forge?"sword":"";
}
public sealed partial class DemoWorld
{
    public List<ProductionOrder> ProductionOrders {get;}=new();
    private double _productionElapsed;
    public double ProductionElapsed=>_productionElapsed;
    public bool SetProductionTarget(string recipe,int target,bool enabled=true)
    {
        ProductionCatalog.Get(recipe);if(target is <0 or >10000)throw new ArgumentOutOfRangeException(nameof(target));
        var order=ProductionOrders.FirstOrDefault(o=>o.Recipe==recipe);
        if(target==0){if(order is not null)ProductionOrders.Remove(order);}
        else if(order is null)ProductionOrders.Add(new(){Recipe=recipe,Target=target,Enabled=enabled});
        else {order.Target=target;order.Enabled=enabled;}
        Revision++;return true;
    }
    // Loose stock plus items in hauling transit. Equipped/personal goods and committed ingredients do not count.
    public int ProductionStock(string item)=>Items.Where(s=>s.Owner==""&&s.Item==item).Sum(s=>s.Count)
        -Jobs.Where(j=>!j.Haul).SelectMany(j=>j.Materials).Where(r=>r.Item==item&&r.Stage!=1).Sum(r=>r.Count)
        +Jobs.Where(j=>j.Haul).SelectMany(j=>j.Materials).Where(r=>r.Item==item&&r.Stage==1).Sum(r=>r.Count);
    public int PendingOutput(string item)=>Jobs.Where(j=>j.Production).Sum(j=>{var r=ProductionCatalog.Get(ProductionCatalog.RecipeOf(j));return r.Output==item?r.Count:0;});
    public string ProductionTargetStatus(ProductionOrder order)
    {
        var r=ProductionCatalog.Get(order.Recipe);int stock=ProductionStock(r.Output),pending=PendingOutput(r.Output);
        string prefix=$"可用 {stock} + 在制 {pending} / 目标 {order.Target}";
        if(!order.Enabled)return prefix+" · 已暂停补产";
        if(stock+pending>=order.Target)return prefix+(pending>0?" · 已排足批次，等待完工":" · 已达目标");
        if(Jobs.Any(j=>j.Production&&ProductionCatalog.RecipeOf(j)==r.Id))return prefix+" · 等待当前批次";
        if(!ProductionStationReady(r))return prefix+" · 等待"+NameOf(r.Station);
        var missing=r.Inputs.Where(p=>Available(p.Key)<p.Value).Select(p=>ItemCatalog.Get(p.Key).Name);
        return prefix+(missing.Any()?" · 缺少"+string.Join("、",missing):" · 待排产");
    }
    private bool ProductionStationReady(ProcessingRecipe r)=>r.Id is "smelt" or "sword"?WorkshopReady:
        Buildings.Values.Any(b=>b.Kind==r.Station&&(r.Station!=BuildKind.Stove||KitchenReady(b.Cell)));
    public bool QueueProductionRecipe(string id)=>id switch {"smelt"=>QueueSmelt(),"sword"=>QueueSword(),"cook"=>QueueCook(),_=>QueueProcessing(id)};
    private void UpdateProductionOrders(double dt)
    {
        if(ProductionOrders.Count==0)return;
        _productionElapsed+=dt;if(_productionElapsed<1)return;_productionElapsed%=1;
        foreach(var order in ProductionOrders.Where(o=>o.Enabled))
        {
            var r=ProductionCatalog.Get(order.Recipe);
            if(ProductionStock(r.Output)+PendingOutput(r.Output)>=order.Target||Jobs.Any(j=>j.Production&&ProductionCatalog.RecipeOf(j)==r.Id))continue;
            if(!ProductionStationReady(r)||r.Inputs.Any(p=>Available(p.Key)<p.Value))continue;
            QueueProductionRecipe(r.Id);
        }
    }
    public void RestoreProductionOrders(ProductionOrder[] orders,double elapsed)
    {
        if(orders is null||orders.Length>ProductionCatalog.All.Length||!double.IsFinite(elapsed)||elapsed<0||elapsed>=1)throw new InvalidDataException("生产目标存档无效。");
        ProductionOrders.Clear();
        foreach(var order in orders)
        {
            if(order is null||!ProductionCatalog.All.Any(r=>r.Id==order.Recipe)||order.Target is <1 or >10000||ProductionOrders.Any(o=>o.Recipe==order.Recipe))throw new InvalidDataException("重复或无效生产目标。");
            ProductionOrders.Add(order);
        }
        _productionElapsed=elapsed;
    }
}
