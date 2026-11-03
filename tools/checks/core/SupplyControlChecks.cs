using NoDwarf.Simulation;
using System.Text.Json.Nodes;
internal static class SupplyControlChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception("SUPPLY: "+why);}
    static void Advance(DemoWorld w,double t){for(int i=0;i<t*60;i++)w.Update(1d/60);}
    static DemoWorld Fixture()
    {var w=CombatChecks.Fixture();foreach(var a in w.Workers){a.Person.Hunger=a.Person.Thirst=a.Person.Rest=90;a.Person.Schedule=ResidentWork.FreeDay();}return w;}
    public static void Run()
    {
        var w=Fixture();var worker=w.Workers[0];worker.Person.WorkPriorities["crafting"]=1;
        var loom=new Cell(64,60);w.Buildings[loom]=new(loom,BuildKind.Loom);w.SpawnItem("cloth",6,new(61,60));
        Check(w.ConfigureSupplyReserve("cloth",4,new[]{"recipe:bandages"}).Ok,"configure reserve");
        Check(w.QueueProductionRecipe("shirt")&&!w.QueueProductionRecipe("shirt"),"one shirt may use surplus but next cannot consume reserve");
        Advance(w,25);Check(w.Total("shirt")==1&&w.Total("cloth")==4,"shirt consumes only surplus");
        Check(!w.QueueProductionRecipe("shirt")&&w.QueueProductionRecipe("bandages"),"allowed bandages can use protected cloth");Advance(w,25);
        Check(w.Total("cloth")==3&&w.Total("bandage")==3,"exception consumes real cloth");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.SupplyReserves.Single().AllowedUses.SetEquals(new[]{"recipe:bandages"}),"reserve persists");
        Check(w.ConfigureSupplyReserve("cloth",0,Array.Empty<string>()).Ok&&w.QueueProductionRecipe("shirt"),"explicit zero removes reserve");

        w=Fixture();worker=w.Workers[0];worker.Person.WorkPriorities["construction"]=1;
        var stone=w.SpawnItem("cobblestone",20,new(61,60));var site=new Cell(70,60);Check(w.QueueBuild(site,BuildKind.Wall),"reserved build fixture");var job=w.Jobs.Single();
        w.SetItemForbidden(stone.Id,true);Check(job.SupplyPaused&&job.Materials.All(m=>m.StackId==0)&&w.Total("cobblestone")==20,"forbid releases untaken reservations while retaining plan");
        Advance(w,3);Check(worker.Job is null&&!w.Buildings.ContainsKey(site),"no repeated assignment of forbidden stock");
        w=DemoWorld.LoadJson(w.SaveJson());stone=w.Items.Single();Check(stone.Forbidden&&w.Jobs.Single().SupplyPaused,"ban and paused plan persist");
        w.SetItemForbidden(stone.Id,false);Advance(w,30);Check(w.Buildings.ContainsKey(site),"restore resumes plan");

        w=Fixture();worker=w.Workers[0];worker.Person.WorkPriorities["construction"]=1;w.SpawnItem("cobblestone",20,new(61,60));w.QueueBuild(site,BuildKind.Wall);
        for(int i=0;i<900&&!w.Jobs.Any(j=>j.Materials.Any(m=>m.Stage==1));i++)w.Update(1d/60);
        Check(w.Jobs.Any(j=>j.Materials.Any(m=>m.Stage==1)),"actual carried material");w.ConfigureSupplyReserve("cobblestone",100,Array.Empty<string>());Advance(w,30);
        Check(w.Buildings.ContainsKey(site),"new reserve never retracts carried or existing reserved materials");

        w=Fixture();worker=w.Workers[0];worker.Person.Hunger=20;var meal=w.SpawnItem("meal",1,new(62,60));
        w.ConfigureSupplyReserve("meal",1,Array.Empty<string>());Check(!w.ActResident(new(worker.Id,"eat")).Ok,"food obeys minimum");
        w.ConfigureSupplyReserve("meal",1,new[]{"food"});Check(w.ActResident(new(worker.Id,"eat")).Ok,"food exception works");
        w.SetItemForbidden(meal.Id,true);Check(worker.Person.Activity is null&&w.Total("meal")==1,"ban cancels food reservation without consuming");
        Check(w.LifeAlerts.Any(a=>a.Id=="supply:meal"&&a.Location==meal.Cell),"restricted food warning locatable");
        w.SetItemForbidden(meal.Id,false);Advance(w,15);Check(worker.Person.Hunger>40&&w.Total("meal")==0,"food recovers when unbanned");

        w=Fixture();worker=w.Workers[1];w.InjureResident("guard","left_arm","cut",30);var bandage=w.SpawnItem("bandage",1,worker.Tile);
        w.SetItemForbidden(bandage.Id,true);Check(!w.ActResident(new(worker.Id,"treat","guard")).Ok,"medical source ban");
        w.SetItemForbidden(bandage.Id,false);w.ConfigureSupplyReserve("bandage",1,new[]{"care"});Check(w.ActResident(new(worker.Id,"treat","guard")).Ok,"care can use medical reserve");Advance(w,20);
        Check(w.Total("bandage")==0&&ResidentHealth.Bleeding(w.Workers[0].Person)==0,"medical material consumed exactly once");
        w=Fixture();worker=w.Workers[0];worker.Person.WorkPriorities["hauling"]=1;var wood=w.SpawnItem("wood",5,new(61,60));var dest=new Cell(65,60);w.SetStockpile(dest,dest);
        w.SetItemForbidden(wood.Id,true);Advance(w,3);Check(!w.Jobs.Any(j=>j.Haul),"ban blocks hauling");
        w.SetItemForbidden(wood.Id,false);w.ConfigureSupplyReserve("wood",5,Array.Empty<string>());Advance(w,15);
        Check(w.Items.Any(s=>s.Item=="wood"&&s.Cell==dest)&&w.Available("wood")==0,"moving reserve preserves protected quantity");
        var zone=w.EditableStorageAt(dest)!;w.ConfigureStorage(zone.Id,3,Array.Empty<string>());w.ConfigureSupplyReserve("wood",0,Array.Empty<string>());
        Check(w.Available("wood")==5,"warehouse refusal is not a usage ban");
        var bad=JsonNode.Parse(w.SaveJson())!;bad["SupplyReserves"]=JsonNode.Parse("[{\"Item\":\"wood\",\"Minimum\":1,\"AllowedUses\":[\"unregistered\"]}]");
        try{DemoWorld.LoadJson(bad.ToJsonString());throw new Exception("invalid purpose accepted");}catch(InvalidDataException){}
        Console.WriteLine("SUPPLY_CONTROL_OK: reserve and recipe exception, committed cargo, stack ban, paused save, food, medicine, hauling and warehouse distinction");
    }
}
