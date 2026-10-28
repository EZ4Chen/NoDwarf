namespace NoDwarf.Simulation;
public static class BuildingGravityChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("GRAVITY: "+message);}
    private static void Settle(DemoWorld w){int steps=0;while(w.SupportPending&&steps++<10000)w.AdvanceSupport();Check(!w.SupportPending,"budgeted work finishes");}
    public static void Run()
    {
        var w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        var c=new Cell(60,60,5);w.DeveloperPlace(c,BuildKind.Log);Settle(w);
        Check(w.Buildings.ContainsKey(c with{Level=0})&&!w.Buildings.ContainsKey(c),"floating block lands");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        for(int y=0;y<3;y++)w.DeveloperPlace(new(60,60,y),BuildKind.Wall);
        for(int x=61;x<66;x++)w.DeveloperPlace(new(x,60,2),BuildKind.Planks);
        Settle(w);Check(w.FallenBuildings==0,"connected overhang supported without strength calculation");
        w.DeveloperErase(new(60,60,0));Settle(w);
        Check(w.Buildings.ContainsKey(new(65,60,1))&&w.Buildings.ContainsKey(new(60,60,0)),"whole disconnected group falls together");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);w.DeveloperPlace(new(60,60,0),BuildKind.Anvil);Settle(w);
        Check(w.DeveloperDig(new(60,60,-1)),"foundation can be excavated");Settle(w);
        Check(w.Buildings.ContainsKey(new(60,60,-1)),"excavation triggers gravity");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);w.DeveloperPlace(new(60,60,4),BuildKind.Campfire);w.FireFuel[new(60,60,4)]=123;
        w.DamageBlock(new(60,60,4),1);Settle(w);
        Check(w.FireFuel.GetValueOrDefault(new(60,60,0))==123,"fuel follows fixture");
        Check(w.RemainingDurability(new(60,60,0))==BlockCatalog.Get(BuildKind.Campfire).Durability-1,"damage follows building");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        w.DeveloperPlace(new(60,60,5),BuildKind.Furnace);
        var order=new DemoJob{Cell=new(60,60,5),Smelt=true};w.Jobs.Add(order);Settle(w);
        Check(order.Cell==new Cell(60,60,0),"production task follows moved fixture");
        Check(w.Buildings.ContainsKey(new(60,60,0)),"two-cell fixture lands without self collision");
        w.DeveloperPlace(new(60,60,2),BuildKind.Planks);Settle(w);
        w.DeveloperErase(new(60,60,0));Settle(w);
        Check(w.Buildings.TryGetValue(new(60,60,0),out var upper)&&upper.Kind==BuildKind.Planks,"removing tall fixture wakes block on its top");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        w.DeveloperPlace(new(60,60,4),BuildKind.Floodgate);w.ToggleFloodgate(new(60,60,4));Settle(w);
        Check(w.GateOpen(new(60,60,0)),"open gate state follows falling gate");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.Workers.RemoveRange(1,2);
        var person=w.Workers[0];person.X=60;person.Z=60;person.Level=0;
        w.DeveloperPlace(new(60,60,4),BuildKind.Wall);Settle(w);
        Check(!w.Buildings.ContainsKey(new(60,60,0)),"fall stops before hitting resident");
        person.X=62;for(int retry=0;retry<31;retry++)w.AdvanceSupport();Settle(w);
        Check(w.Buildings.ContainsKey(new(60,60,0)),"fall resumes after resident leaves");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        w.DeveloperPlace(new(60,60,4),BuildKind.Wall);
        w.Workers.Add(new DemoWorker{Name="rider",X=60,Z=60,Level=5,TravelFrom=new Cell(60,60,5)});
        Settle(w);Check(w.Workers[0].Level==1&&w.Workers[0].TravelFrom is null,"resident on platform moves with it");
        var debris=new DemoWorld(flat:true){FallBreakChance=1};debris.DeveloperResidents(false);debris.Items.Clear();
        debris.DeveloperPlace(new(60,60,4),BuildKind.Planks);Settle(debris);
        Check(debris.Buildings.Count==0&&debris.Total("wood")==BlockCatalog.Get(BuildKind.Planks).Cost.Wood,"certain break produces recipe materials once");
        var loadedDebris=DemoWorld.LoadJson(debris.SaveJson());Settle(loadedDebris);
        Check(loadedDebris.Total("wood")==debris.Total("wood")&&!loadedDebris.VisualFalls.Any(),"save/load does not replay debris or animation");
        var mixed=new DemoWorld(flat:true);mixed.DeveloperResidents(false);mixed.Items.Clear();
        for(int x=60;x<160;x++)mixed.DeveloperPlace(new(x,60,4),BuildKind.Planks);
        var replay=DemoWorld.LoadJson(mixed.SaveJson());Settle(mixed);Settle(replay);
        Check(mixed.Buildings.Count>0&&mixed.Buildings.Count<100,"default probability produces survivors and debris");
        Check(mixed.Buildings.Keys.ToHashSet().SetEquals(replay.Buildings.Keys)&&mixed.Total("wood")==replay.Total("wood"),"pre-fall save has deterministic outcome");
        Check(mixed.Total("wood")+mixed.Buildings.Count*BlockCatalog.Get(BuildKind.Planks).Cost.Wood==100*BlockCatalog.Get(BuildKind.Planks).Cost.Wood,"mixed result conserves materials");
        var restored=DemoWorld.LoadJson(w.SaveJson());Settle(restored);Check(restored.Buildings.ContainsKey(new(60,60,0)),"save/load");
        w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        for(int x=50;x<90;x++)for(int z=50;z<90;z++){var p=new Cell(x,z,5);w.Buildings[p]=new(p,BuildKind.Planks);}
        for(int i=0;i<100;i++)w.Workers.Add(new DemoWorker{Name="remote"+i,X=150+i%10,Z=150+i/10,Level=0});
        long before=w.SupportVisits;w.AdvanceSupport(32);Check(w.SupportVisits-before<=32&&w.SupportPending,"large group traversal yields at budget");
        // An edit during the sliced search must restart without losing dirty cells.
        w.Buildings.Remove(new(50,50,5));
        var sw=System.Diagnostics.Stopwatch.StartNew();double worst=0;int calls=0;
        while(w.SupportPending&&calls++<10000){var t=System.Diagnostics.Stopwatch.StartNew();w.AdvanceSupport();worst=Math.Max(worst,t.Elapsed.TotalMilliseconds);}
        Check(!w.SupportPending&&w.Buildings.Count==1599&&w.Buildings.Keys.All(p=>p.Level==0),"batch lands without duplication");
        before=w.SupportVisits;for(int i=0;i<1000;i++)w.AdvanceSupport();Check(w.SupportVisits==before,"idle does no graph work");
        Console.WriteLine($"GRAVITY_OK: landing, overhangs, group fall, excavation, state/save, edit restart, 1599 blocks {sw.Elapsed.TotalMilliseconds:F1} ms total / {worst:F1} ms max call, idle zero visits");
    }
}
