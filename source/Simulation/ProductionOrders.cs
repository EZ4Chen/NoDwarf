namespace NoDwarf.Simulation;

public sealed class ProductionOrder
{
    public int Id {get;set;}
    public string Recipe {get;set;}="";
    public string Mode {get;set;}="maintain"; // Old target-only saves migrate without changing behavior.
    public int Target {get;set;}
    public bool Enabled {get;set;}=true;
    public Cell? Station {get;set;}
    public string WorkerId {get;set;}="";
    public HashSet<string>? AllowedIngredients {get;set;}
    public int SourceZone {get;set;} // 0: anywhere, positive: named stockpile.
    public int DestinationZone {get;set;} // -1: leave on ground, 0: best stockpile.
    public int Radius {get;set;} // 0: unlimited; distance is measured from the workbench in 3D.
    public int MinSkill {get;set;}
    public int MaxSkill {get;set;}=20;
    public int MinDurability {get;set;}
    public int MaxDurability {get;set;}=100;
}
public static class ProductionCatalog
{
    public static readonly ProcessingRecipe[] All=ProductionDefinitions.Recipes;
    public static ProcessingRecipe Get(string id)=>All.FirstOrDefault(r=>r.Id==id)??throw new ArgumentException("未知生产配方。");
    public static string RecipeOf(DemoJob j)=>j.Processing?j.Recipe:j.Cook?"cook":j.Smelt?"smelt":j.Forge?"sword":"";
}
public sealed partial class DemoWorld
{
    public List<ProductionOrder> ProductionOrders {get;}=new();
    private double _productionElapsed;
    private int _nextProductionOrder;
    public double ProductionElapsed=>_productionElapsed;
    public ProductionOrder AddProductionOrder(string recipe,string mode="count",Cell? station=null)
    {
        var r=ProductionCatalog.Get(recipe);var m=ProductionDefinitions.Mode(mode);
        if(ProductionOrders.Count>=512)throw new InvalidOperationException("生产清单已满（512）。");
        if(station is Cell c&&(!Buildings.TryGetValue(c,out var b)||b.Kind!=r.Station))throw new ArgumentException("清单设施不匹配");
        var order=new ProductionOrder{Id=++_nextProductionOrder,Recipe=recipe,Mode=mode,Target=m.DefaultCount,Station=station};ProductionOrders.Add(order);Revision++;return order;
    }
    public void RemoveProductionOrder(int id)
    {
        foreach(var job in Jobs.Where(j=>j.ProductionOrderId==id).ToArray())CancelJob(job);
        ProductionOrders.RemoveAll(o=>o.Id==id);Revision++;
    }
    public void MoveProductionOrder(int id,int delta)
    {
        int index=ProductionOrders.FindIndex(o=>o.Id==id);if(index<0)return;
        int next=Math.Clamp(index+delta,0,ProductionOrders.Count-1);var order=ProductionOrders[index];ProductionOrders.RemoveAt(index);ProductionOrders.Insert(next,order);
        // Release pending reservations so the new priority actually takes effect. Running work finishes first.
        foreach(var job in Jobs.Where(j=>j.ProductionOrderId>0&&j.Worker<0).ToArray())CancelJob(job);
        Revision++;
    }
    public void PauseProductionOrder(int id,bool paused)
    {
        var order=ProductionOrders.First(o=>o.Id==id);order.Enabled=!paused;
        if(paused)foreach(var job in Jobs.Where(j=>j.ProductionOrderId==id).ToArray())CancelJob(job,false);
        Revision++;
    }
    public void ProductionOrderChanged(ProductionOrder order)
    {
        ValidateProductionOrder(order);
        foreach(var job in Jobs.Where(j=>j.ProductionOrderId==order.Id).ToArray())CancelJob(job);
        Revision++;
    }
    public bool SetProductionTarget(string recipe,int target,bool enabled=true)
    {
        ProductionCatalog.Get(recipe);if(target is <0 or >10000)throw new ArgumentOutOfRangeException(nameof(target));
        var order=ProductionOrders.FirstOrDefault(o=>o.Recipe==recipe&&ProductionDefinitions.Mode(o.Mode).Behavior=="maintain");
        if(target==0){if(order is not null)RemoveProductionOrder(order.Id);}
        else{order??=AddProductionOrder(recipe,"maintain");order.Target=target;PauseProductionOrder(order.Id,!enabled);}
        Revision++;return true;
    }
    public int ProductionStock(string item)=>Items.Where(s=>s.Owner==""&&s.Item==item).Sum(s=>s.Count)
        -Jobs.Where(j=>!j.Haul).SelectMany(j=>j.Materials).Where(r=>r.Item==item&&r.Stage!=1).Sum(r=>r.Count)
        +Jobs.Where(j=>j.Haul).SelectMany(j=>j.Materials).Where(r=>r.Item==item&&r.Stage==1).Sum(r=>r.Count);
    public int PendingOutput(string item)=>Jobs.Where(j=>j.Production).Sum(j=>{var r=ProductionCatalog.Get(ProductionCatalog.RecipeOf(j));return r.Output==item?r.Count:0;});
    public string ProductionTargetStatus(ProductionOrder order)
    {
        var r=ProductionCatalog.Get(order.Recipe);string mode=ProductionDefinitions.Mode(order.Mode).Behavior;
        string prefix=mode=="forever"?"永远":mode=="count"?$"{order.Target} 次 · 每次 {r.Count} 个":$"{ProductionStock(r.Output)}/{order.Target}";
        if(!order.Enabled)return prefix+" · 已暂停";
        if(mode=="count"&&order.Target==0)return prefix+" · 已完成";
        if(mode=="maintain"&&ProductionStock(r.Output)+PendingOutput(r.Output)>=order.Target)return prefix+" · 已达目标（含在制）";
        if(Jobs.FirstOrDefault(j=>j.ProductionOrderId==order.Id) is {} job)return prefix+" · "+OrderStatus(job);
        if(!ProductionStationReady(r))return prefix+" · 等待"+NameOf(r.Station);
        if(order.WorkerId!=""&&!Workers.Any(w=>w.Id==order.WorkerId&&!w.Person.Dead))return prefix+" · 等待指定工作者";
        if(order.SourceZone>0&&!StorageZones.Any(z=>z.Id==order.SourceZone))return prefix+" · 原料储存区已移除";
        var station=Buildings.Values.FirstOrDefault(b=>b.Kind==r.Station&&(order.Station is null||order.Station==b.Cell));
        if(station is not null)
        {
            var probe=new DemoJob{Cell=station.Cell,Recipe=r.Id,ProductionOrderId=order.Id};
            var missing=r.Inputs.Where(p=>Items.Where(s=>s.Item==p.Key&&s.Owner==""&&BillAllowsSource(probe,s)).Sum(s=>Math.Max(0,s.Count-Reserved(s.Id)))<p.Value).Select(p=>ItemCatalog.Get(p.Key).Name).ToArray();
            if(missing.Length>0)return prefix+" · 缺少符合条件的"+string.Join("、",missing);
        }
        return prefix+" · 等待符合条件的原料与设施";
    }
    private bool ProductionStationReady(ProcessingRecipe r)=>r.Id is "smelt" or "sword"?WorkshopReady:
        Buildings.Values.Any(b=>b.Kind==r.Station&&(r.Station!=BuildKind.Stove||KitchenReady(b.Cell)));
    public bool QueueProductionRecipe(string id)=>id switch {"smelt"=>QueueSmelt(),"sword"=>QueueSword(),"cook"=>QueueCook(),_=>QueueProcessing(id)};
    private ProductionOrder? Bill(DemoJob job)=>job.ProductionOrderId==0?null:ProductionOrders.FirstOrDefault(o=>o.Id==job.ProductionOrderId);
    private bool BillAllowsWorker(DemoJob job,DemoWorker worker)=>job.ProductionOrderId==0||Bill(job) is {} o&&o.Enabled&&(o.WorkerId==""||o.WorkerId==worker.Id)&&worker.Person.Level(JobSkill(job))>=o.MinSkill&&worker.Person.Level(JobSkill(job))<=o.MaxSkill;
    private bool BillAllowsSource(DemoJob job,ItemStack stack)
    {
        if(job.ProductionOrderId==0)return true;
        if(Bill(job) is not {} o)return false;
        double durability=stack.Properties.Durability is double d&&stack.Properties.MaxDurability is double max?100*d/max:100;
        return (o.AllowedIngredients is null||o.AllowedIngredients.Contains(stack.Item))&&
            (o.SourceZone==0||StorageZones.Any(z=>z.Id==o.SourceZone&&z.Cells.Contains(stack.Cell)))&&
            (o.Radius==0||Math.Abs(stack.Cell.X-job.Cell.X)+Math.Abs(stack.Cell.Z-job.Cell.Z)+Math.Abs(stack.Cell.Level-job.Cell.Level)<=o.Radius)&&durability>=o.MinDurability&&durability<=o.MaxDurability;
    }
    private void UpdateProductionOrders(double dt)
    {
        if(ProductionOrders.Count==0)return;
        _productionElapsed+=dt;if(_productionElapsed<1)return;_productionElapsed%=1;
        foreach(var order in ProductionOrders.Where(o=>o.Enabled))
        {
            var r=ProductionCatalog.Get(order.Recipe);string behavior=ProductionDefinitions.Mode(order.Mode).Behavior;
            if(behavior=="count"&&order.Target==0||behavior=="maintain"&&ProductionStock(r.Output)+PendingOutput(r.Output)>=order.Target||Jobs.Any(j=>j.ProductionOrderId==order.Id))continue;
            if(!ProductionStationReady(r))continue;
            foreach(var station in Buildings.Values.Where(b=>b.Kind==r.Station&&(order.Station is null||order.Station==b.Cell)))
            {
                if(r.Id=="smelt"&&station.Cell!=_forgeFurnace||r.Id=="sword"&&station.Cell!=_forgeAnvil||Jobs.Any(j=>j.Production&&j.Cell==station.Cell)||r.Station==BuildKind.Stove&&!KitchenReady(station.Cell))continue;
                var job=new DemoJob{Id=++_nextJob,Cell=station.Cell,Recipe=r.Id,ProductionOrderId=order.Id};
                if(Workers.Count>0&&!Workers.Any(w=>!w.Person.Dead&&BillAllowsWorker(job,w)&&ResidentWork.Rank(w.Person,ResidentWork.JobType(job))!=int.MaxValue))continue;
                if(!Reserve(job,r.Inputs))continue;
                Jobs.Add(job);Revision++;break;
            }
        }
    }
    private void CompleteProductionOrder(DemoJob job)
    {if(Bill(job) is {} order&&ProductionDefinitions.Mode(order.Mode).Behavior=="count")order.Target=Math.Max(0,order.Target-1);}
    private static void ValidateProductionOrder(ProductionOrder order)
    {
        if(order is null||!ProductionCatalog.All.Any(r=>r.Id==order.Recipe)||!ProductionDefinitions.Modes.Any(m=>m.Id==order.Mode)||order.Target is <0 or >10000||order.Target==0&&ProductionDefinitions.Mode(order.Mode).Behavior!="count"||order.SourceZone<0||order.DestinationZone< -1||order.Radius is <0 or >1024||order.MinSkill<0||order.MaxSkill>20||order.MinSkill>order.MaxSkill||order.MinDurability<0||order.MaxDurability>100||order.MinDurability>order.MaxDurability||order.WorkerId is null||order.AllowedIngredients?.Any(k=>!ProductionCatalog.Get(order.Recipe).Inputs.ContainsKey(k))==true||order.Station is Cell c&&!Inside(c))throw new InvalidDataException("生产清单存档无效。");
    }
    public void RestoreProductionOrders(ProductionOrder[] orders,double elapsed)
    {
        if(orders is null||orders.Length>512||!double.IsFinite(elapsed)||elapsed<0||elapsed>=1)throw new InvalidDataException("生产清单存档无效。");
        ProductionOrders.Clear();_nextProductionOrder=orders.Where(o=>o is not null).Select(o=>o.Id).DefaultIfEmpty().Max();
        foreach(var order in orders)
        {
            ValidateProductionOrder(order);if(order.Id==0)order.Id=++_nextProductionOrder;
            if(order.Id<0||ProductionOrders.Any(o=>o.Id==order.Id))throw new InvalidDataException("重复生产清单 ID。");
            ProductionOrders.Add(order);
        }
        foreach(var job in Jobs.Where(j=>j.ProductionOrderId!=0))if(Bill(job) is not {} o||!job.Production||ProductionCatalog.RecipeOf(job)!=o.Recipe||o.Station is Cell c&&c!=job.Cell)throw new InvalidDataException("生产任务清单关联无效。");
        foreach(var job in Jobs.Where(j=>j.Production&&j.ProductionOrderId==0).ToArray())
        {var order=ProductionOrders.FirstOrDefault(o=>o.Recipe==ProductionCatalog.RecipeOf(job)&&!Jobs.Any(j=>j.ProductionOrderId==o.Id))??AddProductionOrder(ProductionCatalog.RecipeOf(job));job.ProductionOrderId=order.Id;}
        _productionElapsed=elapsed;
    }
}
