using System.Text.Json.Nodes;
using System.Xml.Linq;
namespace NoDwarf.Simulation;
public static class ProductionBillChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static DemoWorld Fixture()
    {
        var w=new DemoWorld(flat:true);w.Items.Clear();w.Stockpiles.Clear();w.StorageZones.Clear();w.Workers.RemoveRange(1,2);w.Workers[0].Person.Schedule=ResidentWork.FreeDay();
        w.Buildings[new(5,9)]=new(new(5,9),BuildKind.Loom);w.SpawnItem("cotton",80,new(2,10));return w;
    }
    private static void Advance(DemoWorld w,int seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
    private static void Until(DemoWorld w,Func<bool> done){for(int i=0;i<900*30&&!done();i++)w.Update(1d/30);Check(done(),"Bill work did not complete: "+string.Join(";",w.Workers.Select(p=>p.Status)));}
    public static void Run()
    {
        var count=Fixture();var bill=count.AddProductionOrder("cloth");bill.Target=2;Until(count,()=>bill.Target==0);Advance(count,30);
        Check(count.Total("cloth")==4&&count.Total("cotton")==72,"Fixed batch count must stop and consume real inputs");
        var forever=count.AddProductionOrder("cloth","forever");Until(count,()=>count.Total("cloth")>=8);count.PauseProductionOrder(forever.Id,true);int before=count.Total("cloth");Advance(count,20);
        Check(count.Total("cloth")==before&&!count.Jobs.Any(j=>j.ProductionOrderId==forever.Id),"Suspension must stop/release pending work");count.RemoveProductionOrder(forever.Id);Check(count.Total("cloth")==before,"Delete must keep completed products");
        var order=Fixture();order.DeveloperResidents(false);var low=order.AddProductionOrder("cloth");var high=order.AddProductionOrder("cloth");order.MoveProductionOrder(high.Id,-1);Advance(order,2);
        Check(order.Jobs.Single(j=>j.Production).ProductionOrderId==high.Id,"Reordering must affect actual reservation order");order.PauseProductionOrder(high.Id,true);Advance(order,2);
        Check(order.Jobs.Single(j=>j.Production).ProductionOrderId==low.Id,"Paused top bill must let the next bill run");
        var filter=Fixture();var f=filter.AddProductionOrder("cloth");f.AllowedIngredients=new();Advance(filter,2);Check(!filter.Jobs.Any(j=>j.Production),"Deny-all ingredients must prevent reservations");
        f.AllowedIngredients=null;f.SourceZone=999;Advance(filter,2);Check(!filter.Jobs.Any(j=>j.Production),"Missing source zone must not fall back to anywhere");
        f.SourceZone=0;f.Radius=1;Advance(filter,2);Check(!filter.Jobs.Any(j=>j.Production),"Source radius must constrain material search");
        f.Radius=0;f.MaxDurability=50;Advance(filter,2);Check(!filter.Jobs.Any(j=>j.Production),"Durability filter must affect scheduling");
        f.MaxDurability=100;f.WorkerId="absent-person";Advance(filter,15);Check(filter.Total("cloth")==0&&filter.Jobs.All(j=>j.Worker<0),"Worker restriction must constrain actual assignment");
        f.WorkerId=filter.Workers[0].Id;f.MinSkill=20;Advance(filter,5);Check(filter.Total("cloth")==0,"Minimum skill must constrain assignment");f.MinSkill=0;
        f.DestinationZone=-1;Until(filter,()=>f.Target==0);Check(filter.Items.Any(i=>i.Item=="cloth"&&i.DestinationZone==-1),"Drop-on-floor output policy persisted on physical stock");
        var zones=Fixture();zones.Items.Clear();zones.DeveloperResidents(false);
        var permitted=zones.SpawnItem("cotton",4,new(2,10));var forbidden=zones.SpawnItem("cotton",20,new(5,8));
        zones.SetStockpile(permitted.Cell,permitted.Cell);var source=zones.StorageAt(permitted.Cell)!;
        var sourced=zones.AddProductionOrder("cloth");sourced.SourceZone=source.Id;Advance(zones,2);
        Check(zones.Jobs.Single(j=>j.Production).Materials.All(m=>m.StackId==permitted.Id),"Specific source zone must override nearer materials");
        zones.RemoveProductionOrder(sourced.Id);Check(zones.Available("cotton")==24,"Deleting a queued bill releases its exact reservations");
        var shipping=Fixture();var destination=new Cell(3,12);shipping.SetStockpile(destination,destination);var destinationZone=shipping.StorageAt(destination)!;
        var shipped=shipping.AddProductionOrder("cloth");shipped.DestinationZone=destinationZone.Id;Until(shipping,()=>shipped.Target==0);
        Check(shipping.Items.Any(i=>i.Item=="cloth"&&i.DestinationZone==destinationZone.Id),"Products carry the selected storage destination");
        Until(shipping,()=>shipping.Items.Any(i=>i.Item=="cloth"&&i.Cell==destination));
        Check(shipping.Total("cloth")==2,"Physical delivery must not duplicate products");
        var loaded=DemoWorld.LoadJson(order.SaveJson());Check(loaded.ProductionOrders[0].Id==high.Id&&!loaded.ProductionOrders[0].Enabled&&loaded.Jobs.Single(j=>j.Production).ProductionOrderId==low.Id,"Queue/mode/pause/link save round trip");
        var legacy=JsonNode.Parse(count.SaveJson())!;foreach(var node in legacy["ProductionOrders"]!.AsArray()){node!.AsObject().Remove("Id");node.AsObject().Remove("Mode");node["Target"]=5;}var migrated=DemoWorld.LoadJson(legacy.ToJsonString());Check(migrated.ProductionOrders.All(o=>o.Id>0&&o.Mode=="maintain"),"Legacy target saves migrate");
        var xml=XDocument.Parse("<ProductionDefinitions><Items><Item id='a' name='A' texture='x' color='ffffff'/></Items><Recipes><Recipe id='custom' name='Custom' station='Loom' output='a' count='2' work='7'><Ingredient item='a' count='1'/></Recipe></Recipes><Modes><Mode id='custom_repeat' label='重复' behavior='forever' defaultCount='1'/></Modes></ProductionDefinitions>");
        var defs=ProductionDefinitions.Parse(xml);Check(defs.Item2[0].Work==7&&defs.Item3[0].Behavior=="forever","XML recipes/items/mode definitions parse without C# changes");
        xml.Root!.Element("Recipes")!.Element("Recipe")!.SetAttributeValue("output","missing");try{ProductionDefinitions.Parse(xml);throw new Exception("Unknown XML reference accepted");}catch(InvalidDataException){}
        Console.WriteLine("PRODUCTION_BILLS_OK: count/forever, pause/delete/reorder, materials/source/radius/durability, worker/skill, output policy, save migration, XML validation");
    }
}
