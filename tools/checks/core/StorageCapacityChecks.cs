using NoDwarf.Simulation;
internal static class StorageCapacityChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception("STORAGE: "+why);}
    static void Advance(DemoWorld w,double t){for(int i=0;i<t*60;i++)w.Update(1d/60);}
    public static void Run()
    {
        var seed=new DemoWorld(flat:true);Check(seed.Stockpiles.All(c=>seed.StoredUnits(c)<=DemoWorld.CellStorageCapacity),"initial stocks fit capacity: "+string.Join(";",seed.Stockpiles.Where(c=>seed.StoredUnits(c)>100).Select(c=>$"{c}={seed.StoredUnits(c)}")));
        var w=CombatChecks.Fixture();foreach(var worker in w.Workers)worker.Person.WorkPriorities["hauling"]=1;
        var dest=new Cell(65,60);w.SetStockpile(dest,dest);w.SpawnItem("cobblestone",95,dest);w.SpawnItem("cobblestone",30,new(61,60));
        Advance(w,.6);Check(w.ReservedStorageUnits(dest)<=5,"destination capacity reserved once across workers");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,20);Check(w.StoredUnits(dest)==100&&w.Total("cobblestone")==125,"full cell stops at capacity without losing goods");
        var extra=dest+new Cell(1,0);w.SetStockpile(extra,extra);Advance(w,20);Check(w.StoredUnits(extra)==25,"expansion resumes deliveries");
        w=CombatChecks.Fixture();w.Workers[0].Person.WorkPriorities["hauling"]=1;w.SetStockpile(dest,dest);w.SpawnItem("wood",20,new(61,60));
        for(int i=0;i<900&&!w.Jobs.Any(j=>j.Haul&&j.Materials.Any(m=>m.Stage==1));i++)w.Update(1d/60);
        Check(w.Jobs.Any(j=>j.Haul&&j.Materials.Any(m=>m.Stage==1)),"in-flight fixture");w.SetStockpile(dest,dest,true);Advance(w,2);
        Check(w.Total("wood")==20&&w.ReservedStorageUnits(dest)==0,"removing warehouse releases destination and preserves cargo");
        w=new DemoWorld(flat:true);w.DeveloperResidents(false);var origin=new Cell(30,30);
        foreach(var part in DemoWorld.ExampleBlueprint())w.DeveloperPlace(origin+part.Offset,part.Kind,part.Facing);
        var station=origin+new Cell(4,1);var spots=new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)}.Select(d=>station+d).Where(w.Walkable).ToArray();
        foreach(var c in spots)w.SpawnItem("rot",100,c);
        Check(!w.QueueProductionRecipe("sword"),"full output rejects new batch before reserving inputs");
        Check(w.Available("iron")==12,"no input deduction at full output");
        w.Items.RemoveAll(s=>s.Item=="rot");Check(w.QueueProductionRecipe("sword"),"clear output permits batch");var job=w.Jobs.Single();
        Check(job.OutputCell is not null&&w.ReservedStorageUnits(job.OutputCell.Value)==8,"output space reserved");
        w=DemoWorld.LoadJson(w.SaveJson());w.DeveloperResidents(true);foreach(var a in w.Workers){a.Person.Schedule=ResidentWork.FreeDay();a.Person.WorkPriorities["hauling"]=0;}
        for(int i=0;i<120*60&&!w.Jobs.Any(j=>j.Progress>0);i++)w.Update(1d/60);
        job=w.Jobs.Single(j=>j.Production);Check(job.Progress>0,"actual processing started");double progress=job.Progress;int iron=w.Total("iron");
        foreach(var c in spots)w.SpawnItem("rot",100,c);Advance(w,4);
        Check(job.Progress==progress&&w.Total("iron")==iron&&w.Total("sword")==0,"space lost mid-batch preserves progress and inputs");
        w=DemoWorld.LoadJson(w.SaveJson());w.Items.RemoveAll(s=>s.Item=="rot");Advance(w,30);
        Check(w.Total("sword")==1&&w.Total("iron")==iron-2,"saved waiting batch completes once when clear");
        Check(w.Items.Where(s=>s.Item=="sword").All(s=>spots.Contains(s.Cell)),"output physically adjacent to station");
        Console.WriteLine("STORAGE_CAPACITY_OK: initial stock, reservation, full warehouse, expansion, in-flight removal, full output, waiting save and exact completion");
    }
}
