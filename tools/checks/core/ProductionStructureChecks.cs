using NoDwarf.Simulation;

internal static class ProductionStructureChecks
{
    private static void Check(bool ok,string message)
    {if(!ok)throw new InvalidOperationException("PRODUCTION_STRUCTURE: "+message);}

    public static void Run()
    {
        var world=new DemoWorld(flat:true);world.DeveloperResidents(false);
        var first=new Cell(20,20);var second=new Cell(35,20);
        void Workshop(Cell origin)
        {
            foreach(var part in DemoWorld.ExampleBlueprint())
                Check(world.DeveloperPlace(origin+part.Offset,part.Kind,part.Facing),"fixture placement");
        }
        Workshop(first);
        Check(world.QueueProductionRecipe("sword"),"first workshop accepts a batch");
        var original=world.Jobs.Single();var originalCell=original.Cell;
        Workshop(second);
        Check(original.Cell==originalCell&&world.Jobs.At(originalCell).Contains(original),"new workshop cannot relocate existing work");
        var secondAnvil=second+new Cell(4,1);
        var secondBill=world.AddProductionOrder("sword",station:secondAnvil);
        var extraAnvil=first+new Cell(2,2);
        Check(world.DeveloperPlace(extraAnvil,BuildKind.Anvil),"second anvil in same room");
        var extraBill=world.AddProductionOrder("sword",station:extraAnvil);
        var firstFurnace=first+new Cell(1,1);var secondFurnace=second+new Cell(1,1);
        world.SpawnItem("raw_iron",8,world.Camp);
        var smeltA=world.AddProductionOrder("smelt",station:firstFurnace);
        var smeltB=world.AddProductionOrder("smelt",station:secondFurnace);
        world.Update(1.1);
        foreach(var bill in new[]{secondBill,extraBill,smeltA,smeltB})
            Check(world.Jobs.Any(j=>j.ProductionOrderId==bill.Id&&j.Cell==bill.Station),"every eligible station schedules independently");
        Check(world.Jobs.Where(j=>j.Production).GroupBy(j=>j.Cell).All(g=>g.Count()==1),"one active batch per station");
        var completed=DemoWorld.LoadJson(world.SaveJson());completed.DeveloperResidents(true);
        foreach(var worker in completed.Workers)worker.Person.Schedule=ResidentWork.FreeDay();
        for(int tick=0;tick<600*SimulationClock.BaseTicksPerSecond&&completed.ProductionOrders.Any(o=>o.Target>0);tick++)
            completed.Update(SimulationClock.TickSeconds);
        Check(completed.ProductionOrders.All(o=>o.Target==0),"all workshops complete their own bills after loading");
        Check(completed.Total("sword")==world.Total("sword")+3&&completed.Total("iron")==world.Total("iron")-2&&
            completed.Total("coal")==world.Total("coal")-5&&completed.Total("raw_iron")==world.Total("raw_iron")-4,
            "multi-workshop completion conserves recipe materials and outputs");
        var loaded=DemoWorld.LoadJson(world.SaveJson());
        Check(loaded.Jobs.Count==world.Jobs.Count&&loaded.Jobs.All(j=>loaded.Jobs.At(j.Cell).Contains(j)),"save restores station and spatial indexes");
        foreach(var job in loaded.Jobs.ToArray())loaded.CancelJob(job,false);
        Check(loaded.Jobs.Count==0&&world.Jobs.All(j=>loaded.Jobs.At(j.Cell).Count==0),"cancel restored jobs removes all indexes");
        Check(loaded.Available("iron")==12,"cancellation releases reserved iron");
        Check(loaded.DeveloperErase(first+new Cell(2,4)),"open first workshop");
        loaded.Update(1.1);
        Check(!loaded.Jobs.Any(j=>j.Cell==originalCell||j.Cell==extraAnvil||j.Cell==firstFurnace),"invalid room cannot keep scheduling");
        Check(loaded.Jobs.Any(j=>j.Cell==secondAnvil)&&loaded.Jobs.Any(j=>j.Cell==secondFurnace),"other workshop remains operational");

        var jobs=new JobCollection();var a=new DemoJob{Id=1,Cell=first,Refuel=true};var b=new DemoJob{Id=2,Cell=second,Refuel=true};
        jobs.Add(a);jobs.Add(b);long version=jobs.Version;jobs.Move(a,second);
        Check(jobs.Version==version+1&&jobs.At(first).Count==0&&jobs.At(second).Count==2,"moving updates both buckets and version");
        jobs.Remove(a);jobs.Remove(b);
        Check(jobs.Count==0&&jobs.At(second).Count==0,"moved tasks can be removed");
        try{jobs.Move(a,first);throw new Exception("Unknown task accepted");}catch(ArgumentException){}
        Console.WriteLine("PRODUCTION_STRUCTURE_OK: multi-room and multi-station scheduling, fixed stations, save/cancel, invalid rooms, task indexes");
    }
}
