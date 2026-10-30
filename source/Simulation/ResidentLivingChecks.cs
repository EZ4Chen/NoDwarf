using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class ResidentLivingChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("LIVING: "+message);}
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)(seconds*SimulationClock.BaseTicksPerSecond);i++)w.Update(SimulationClock.TickSeconds);}
    public static void Run()
    {
        var w=CombatChecks.Fixture();
        foreach(var r in w.Workers){r.Person.Hunger=2;var load=w.SpawnItem("wood",30,r.Tile);load.Owner=r.Id;}
        w.SpawnItem("meal",3,new(65,60));Advance(w,2.1);
        Check(w.Workers.Count(r=>r.Person.Activity?.Kind=="food_supply")==3&&w.Available("meal")==0,"one meal per hungry resident reserved");
        Check(w.Total("wood")==90&&w.Items.Where(i=>i.Item=="wood"&&i.Owner=="").Sum(i=>i.Count)==90,"full loads safely dropped");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,35);
        Check(w.Workers.All(r=>r.Person.Hunger>30)&&w.Total("meal")==0&&w.Total("wood")==90,"all eat once after save/load without duplication");
        w=CombatChecks.Fixture();var helper=w.Workers[0];var patient=w.Workers[1];
        helper.Person.WorkPriorities["care"]=1;patient.Person.Hunger=1;
        Check(w.InjureResident(patient.Id,"torso","fracture",90).Ok,"patient injury");
        w.SpawnItem("meal",1,new(67,60));Advance(w,2.1);
        Check(helper.Person.Activity?.Kind=="food_supply"&&patient.Person.Incapacitated,"non-doctor starts delivery");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,45);patient=w.Workers[1];
        Check(patient.Person.Hunger>30&&w.Total("meal")==0&&patient.Person.Memories.Any(m=>m.Key.StartsWith("fed:")),"incapacitated patient fed after reload");
        w=CombatChecks.Fixture();patient=w.Workers[1];patient.Person.Hunger=1;w.InjureResident(patient.Id,"torso","fracture",90);w.SpawnItem("meal",1,new(60,60));Advance(w,15);
        Check(w.Total("meal")==1&&patient.Person.Hunger<2,"disabled care must not feed");
        w=CombatChecks.Fixture();var r0=w.Workers[0];r0.Person.Hunger=2;var food=w.SpawnItem("meal",1,new(70,60));Advance(w,2.1);
        Check(w.Available("meal")==0,"food reservation");w.ActResident(new(r0.Id,"cancel"));Check(w.Available("meal")==1,"cancel frees food");
        Advance(w,2.1);w.Items.RemoveAll(i=>i.Item=="meal");Advance(w,1);
        Check(r0.Person.Activity?.Kind!="food_supply"&&w.Total("meal")==0,"missing food cancels without phantom meal");
        Advance(w,3);Check(r0.Person.FoodIssue.Contains("口粮"),"missing food explanation");
        // Urgent hunger interrupts manual activity, not just routine work.
        w=CombatChecks.Fixture();r0=w.Workers[0];var wood=w.SpawnItem("wood",1,new(90,60));
        Check(w.ActResident(new(r0.Id,"pickup",ItemId:wood.Id)).Ok,"manual pickup fixture");
        r0.Person.Hunger=2;w.SpawnItem("meal",1,new(61,60));Advance(w,20);
        Check(r0.Person.Hunger>30,"urgent food interrupts manual pickup");
        w=CombatChecks.Fixture();r0=w.Workers[0];r0.Person.Hunger=50;w.SpawnItem("meal",1,new(61,60));
        Check(w.ActResident(new(r0.Id,"eat")).Ok&&r0.Person.Activity?.Kind=="food_supply","manual eat fetches public food");
        Advance(w,15);Check(r0.Person.Hunger>80&&w.Total("meal")==0,"manual meal consumed once");
        // Ordinary rest claims a stable bed; second resident cannot steal it.
        w=CombatChecks.Fixture();r0=w.Workers[0];var r1=w.Workers[1];var bed=new Cell(61,60);w.Buildings[bed]=new(bed,BuildKind.Bed);
        r0.Person.Rest=20;r1.Person.Rest=20;
        Check(w.ActResident(new(r0.Id,"rest")).Ok&&w.ActResident(new(r1.Id,"rest")).Ok,"rest commands");
        Check(r0.Person.HomeBed==bed&&r1.Person.HomeBed is null,"exclusive bed assignment");
        Advance(w,10);Check(r0.Person.Rest>r1.Person.Rest&&r0.Person.MoodEffects.Any(e=>e.Key=="bedroom"),"bed rest and mood benefits");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Workers[0].Person.HomeBed==bed,"bed ownership persistence");
        w.Buildings.Remove(bed);Advance(w,3);Check(w.Workers[0].Person.HomeBed is null,"removed bed unclaimed");
        // Campfire is an actual destination; fallback remains available without one.
        w=CombatChecks.Fixture();r0=w.Workers[0];r1=w.Workers[1];var fire=new Cell(61,60);w.Buildings[fire]=new(fire,BuildKind.Campfire);w.FireFuel[fire]=200;
        r0.Person.Recreation=10;r1.Person.Recreation=10;
        w.ActResident(new(r0.Id,"relax"));w.ActResident(new(r1.Id,"relax"));
        Check(r0.Person.Activity!.Target!=""&&r1.Person.Activity!.Target==r0.Person.Activity.Target&&r0.Person.Activity.Destination!=r1.Person.Activity.Destination,"shared leisure destination has separate seats");
        Advance(w,10);Check(r0.Person.MoodEffects.Any(e=>e.Key=="leisure")&&r0.Person.Recreation>20,"campfire leisure benefits");
        w.Buildings.Remove(fire);Advance(w,3);Check(r0.Person.Activity?.Kind=="relax","missing leisure facility has fallback");
        // A loaded hauler must release job cargo before taking food, without losing supplies.
        w=CombatChecks.Fixture();r0=w.Workers[0];r0.Person.WorkPriorities["construction"]=1;
        w.SpawnItem("cobblestone",10,r0.Tile);Check(w.QueueBuild(new(70,60),BuildKind.Wall),"cargo fixture");
        for(int i=0;i<600&&!w.Jobs.Any(j=>j.Materials.Any(m=>m.Stage==1));i++)w.Update(1d/30);
        Check(w.Jobs.Any(j=>j.Materials.Any(m=>m.Stage==1)),"cargo in flight");int stone=w.Total("cobblestone");
        r0.Person.Hunger=1;w.SpawnItem("meal",1,r0.Tile);
        for(int i=0;i<900&&r0.Person.Hunger<30;i++)w.Update(1d/30);
        Check(r0.Person.Hunger>30&&w.Total("cobblestone")==stone,$"food interrupt preserves job cargo hunger={r0.Person.Hunger} stone={w.Total("cobblestone")}/{stone} action={r0.Person.Activity?.Kind} issue={r0.Person.FoodIssue} position={r0.Tile}");
        // Stop at the actual feed stage, save, then consume exactly the carried ration.
        w=CombatChecks.Fixture();helper=w.Workers[0];patient=w.Workers[1];w.Workers[2].Person.WorkPriorities["care"]=1;
        helper.Person.WorkPriorities["care"]=1;patient.Person.Hunger=1;w.InjureResident(patient.Id,"torso","fracture",90);w.SpawnItem("meal",2,helper.Tile);
        for(int i=0;i<600&&!w.Workers.Any(r=>r.Person.Activity?.Kind=="feed");i++)w.Update(1d/30);
        Check(w.Workers.Count(r=>r.Person.Activity is {Kind:"food_supply" or "feed"})==1&&w.Workers.Any(r=>r.Person.Activity?.Kind=="feed"),"one carer per patient");
        var loaded=DemoWorld.LoadJson(w.SaveJson());Advance(loaded,15);Check(loaded.Total("meal")==1&&loaded.Workers[1].Person.Hunger>30,"feed-stage resume consumes one ration");
        // Three-high sealed walls make meals genuinely unreachable.
        w=CombatChecks.Fixture();r0=w.Workers[0];r0.Person.Hunger=1;food=w.SpawnItem("meal",1,new(70,60));
        for(int x=68;x<=72;x++)for(int z=58;z<=62;z++)if(x==68||x==72||z==58||z==62)
            for(int y=0;y<3;y++){var c=new Cell(x,z,y);w.Buildings[c]=new(c,BuildKind.Wall);}
        Advance(w,4);Check(r0.Person.FoodIssue.Contains("不可达")&&w.Total("meal")==1,"blocked food explained without consumption");
        // A sheltered bedroom provides a bounded mood benefit, not just faster recovery.
        w=CombatChecks.Fixture();r0=w.Workers[0];RoomEnvironmentChecks.Shell(w,new(58,58));bed=new(61,61);w.Buildings[bed]=new(bed,BuildKind.Bed);
        r0.Person.Rest=20;Check(w.ActResident(new(r0.Id,"rest")).Ok,"indoor sleep");Advance(w,10);
        Check(w.RoomAt(bed).Enclosed&&r0.Person.MoodEffects.Any(e=>e.Key=="bedroom"&&e.Amount>=2),"bedroom quality connected to mood");
        Console.WriteLine("RESIDENT_LIVING_OK: one-meal reservations, full-load drop, urgent interruption, feeding/disabled care, cancellation/missing food, bed ownership, leisure destinations, save/load");
    }
}
