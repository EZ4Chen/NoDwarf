namespace NoDwarf.Simulation;

public static class ConstructionChecks
{
    private static void Check(bool ok,string message)
    {if(!ok)throw new InvalidOperationException(message);}

    public static void Run()
    {
        var w=new DemoWorld(flat:true);w.Stockpiles.Clear();
        var cell=w.Camp+new Cell(4,5);
        Check(w.QueueBuild(cell,BuildKind.Wall),"Construction queued");
        var job=w.Jobs.Single(j=>j.Build is not null);
        Check(w.RemainingDurability(cell)==0&&!w.DamageBlock(cell,10),"Unstarted blueprint has no physical durability");
        for(int i=0;i<30*120&&job.BuildFraction<.25;i++)w.Update(1d/30);
        Check(job.BuildFraction>=.25&&job.BuildFraction<1&&!w.Buildings.ContainsKey(cell),"Work exposes partial construction before completion");
        double max=BlockCatalog.Get(BuildKind.Wall).Durability;
        Check(Math.Abs(w.RemainingDurability(cell)-max*job.BuildFraction)<.00001,"Durability grows with real work");
        Check(job.Materials.All(r=>r.Stage==2),"Progress requires delivery");
        double fraction=job.BuildFraction;
        w=DemoWorld.LoadJson(w.SaveJson());job=w.Jobs.Single(j=>j.Build is not null);
        Check(job.BuildFraction==fraction&&Math.Abs(w.RemainingDurability(cell)-max*fraction)<.00001,"Mid-construction save preserves progress and durability");
        double damage=w.RemainingDurability(cell)/2;
        Check(!w.DamageBlock(cell,damage)&&Math.Abs(w.RemainingDurability(cell)-damage)<.00001,"Partial construction takes real damage");
        var cancelled=DemoWorld.LoadJson(w.SaveJson());var cancelledJob=cancelled.Jobs.Single(j=>j.Build is not null);
        int stock=cancelled.Total("cobblestone");cancelled.CancelJob(cancelledJob);
        Check(cancelled.Total("cobblestone")==stock&&cancelled.Available("cobblestone")==stock,"Cancelling delivered construction releases all material reservations");
        Check(cancelled.Workers.All(p=>p.Job!=cancelledJob)&&!cancelled.Buildings.ContainsKey(cell)&&cancelled.RemainingDurability(cell)==0,"Cancellation releases worker and unfinished structure");
        var destroyed=DemoWorld.LoadJson(w.SaveJson());
        Check(destroyed.DamageBlock(cell,max)&&!destroyed.Jobs.Any(j=>j.Cell==cell)&&destroyed.Total("cobblestone")==stock,"Destroying unfinished structure cancels without duplicating stock");
        for(int i=0;i<30*120&&!w.Buildings.ContainsKey(cell);i++)w.Update(1d/30);
        Check(w.Buildings.ContainsKey(cell)&&w.RemainingDurability(cell)==max&&!w.Jobs.Any(j=>j.Cell==cell),"Completion becomes a full-health building exactly once");
        Console.WriteLine("CONSTRUCTION_OK: delivery, progressive durability, mid-build save, damage, cancellation, completion");
    }
}
