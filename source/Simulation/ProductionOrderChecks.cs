namespace NoDwarf.Simulation;
public static class ProductionOrderChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Until(DemoWorld w,Func<bool> condition,int seconds=1800)
    {for(int i=0;i<seconds*30&&!condition();i++)w.Update(1d/30);Check(condition(),"Production timed out: "+string.Join(";",w.Workers.Select(p=>p.Status)));}
    public static void Run()
    {
        var w=new DemoWorld(flat:true);w.Items.Clear();w.Stockpiles.Clear();w.Workers.RemoveRange(1,2);var person=w.Workers[0];person.Person.Schedule=ResidentWork.FreeDay();
        var station=new Cell(5,9);w.Buildings[station]=new(station,BuildKind.Loom);w.SpawnItem("cotton",16,new(2,10));
        w.SetProductionTarget("cloth",4);Until(w,()=>w.Total("cloth")==4);for(int i=0;i<300;i++)w.Update(1d/30);
        Check(w.Total("cotton")==8&&!w.Jobs.Any(j=>j.Recipe=="cloth"),"Target stops without excess batches");
        int consumed=2;foreach(var cloth in w.Items.Where(i=>i.Item=="cloth").ToArray()){int take=Math.Min(consumed,cloth.Count);cloth.Count-=take;consumed-=take;if(cloth.Count==0)w.Items.Remove(cloth);if(consumed==0)break;}w.SetProductionTarget("cloth",4,false);for(int i=0;i<90;i++)w.Update(1d/30);
        Check(!w.Jobs.Any(j=>j.Recipe=="cloth"),"Paused target does not schedule");w.SetProductionTarget("cloth",4);Until(w,()=>w.Total("cloth")==4);
        Check(w.Total("cotton")==4,"Consumption triggers replenishment");
        w.SetProductionTarget("shirt",1);Until(w,()=>w.Total("shirt")==1&&w.Total("cotton")==0&&w.Total("cloth")==4);Check(w.Total("cotton")==0&&w.Total("cloth")<=4,"Chained targets consume real ingredients");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.ProductionOrders.Count==2,"Targets survive save/load");
        w.SetProductionTarget("cloth",20);for(int i=0;i<300;i++)w.Update(1d/30);Check(!w.Jobs.Any(j=>j.Recipe=="cloth")&&w.ProductionTargetStatus(w.ProductionOrders[0]).Contains("缺少"),"Missing ingredients do not create empty jobs");
        var bench=new Cell(6,9);w.Buildings[bench]=new(bench,BuildKind.Workbench);w.SpawnItem("wood",8,new(2,10));w.SetProductionTarget("shield",1);
        Until(w,()=>w.Total("shield")==1);Check(w.Total("wood")==4,"Carpentry consumes wood");
        w.SpawnItem("deep_rubble",2,new(2,10));Check(w.QueueProcessing("rubble"),"Rubble order accepted");Until(w,()=>w.Total("cobblestone")==2);Check(w.Total("deep_rubble")==0,"Rubble processing chain");
        w.Buildings[new(7,9)]=new(new(7,9),BuildKind.Anvil);w.SpawnItem("iron",3,new(2,10));
        w.SetProductionTarget("pickaxe",1);w.SetProductionTarget("hammer",1);Until(w,()=>w.Total("pickaxe")==1&&w.Total("hammer")==1);
        Check(w.Total("iron")==0&&w.Total("wood")==2,"Tools consume iron and wood");
        var pending=new DemoWorld(flat:true);pending.DeveloperResidents(false);pending.Items.Clear();pending.Stockpiles.Clear();pending.Buildings[station]=new(station,BuildKind.Loom);pending.SpawnItem("cloth",4,new(3,9));
        pending.SetProductionTarget("shirt",1);pending.Update(1);pending.Update(1);Check(pending.Jobs.Count(j=>j.Recipe=="shirt")==1&&pending.PendingOutput("shirt")==1,"In-flight output counts toward target");
        var job=pending.Jobs.Single(j=>j.Recipe=="shirt");pending.CancelJob(job);pending.Update(1);Check(pending.Jobs.Count(j=>j.Recipe=="shirt")==1,"Canceled batches can replenish without duplicate reservations");
        pending.SetProductionTarget("shirt",0);pending.CancelJob(pending.Jobs.Single(j=>j.Recipe=="shirt"));pending.Update(1);Check(pending.Jobs.Count==0,"Removed target stays removed");
        var transit=new DemoWorld(flat:true);transit.Items.Clear();transit.Jobs.Clear();transit.SpawnItem("cloth",5,new(3,9));transit.Jobs.Add(new DemoJob{Id=987,Cell=new(5,9),Haul=true,Materials=new(){new(){Item="cloth",Count=2,Stage=1}}});
        Check(transit.ProductionStock("cloth")==7,"Hauling cargo counts as stock");
        var bad=System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!;bad["ProductionOrders"]![0]!["Target"]=-1;
        try{DemoWorld.LoadJson(bad.ToJsonString());throw new Exception("Bad production target accepted");}catch(InvalidDataException){}
        Console.WriteLine("PRODUCTION_TARGET_OK: replenishment, pause/remove, pipeline output, ingredients, crafting, hauling stock, persistence and validation");
    }
}
