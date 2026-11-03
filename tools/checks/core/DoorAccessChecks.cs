using NoDwarf.Simulation;
using System.Text.Json.Nodes;

internal static class DoorAccessChecks
{
    private static readonly Cell Door=new(60,60), Inside=new(64,60), Outside=new(55,60);
    private static void Check(bool ok,string why){if(!ok)throw new Exception("DOOR: "+why);}
    private static void Advance(DemoWorld world,double seconds)
    {for(int i=0;i<seconds*60;i++)world.Update(1d/60);}
    private static DemoWorld Fixture()
    {
        var w=CombatChecks.Fixture();w.Buildings.Clear();
        foreach(var actor in w.Workers){actor.Person.Hunger=90;actor.Person.Rest=90;}
        w.Workers[0].X=Inside.X;w.Workers[0].Z=Inside.Z;
        w.Workers[1].X=Outside.X;w.Workers[1].Z=Outside.Z;
        using(w.BatchBlockChanges())for(int x=60;x<=68;x++)for(int z=56;z<=64;z++)for(int y=0;y<=2;y++)
        {
            var c=new Cell(x,z,y);
            if(y<2&&x>60&&x<68&&z>56&&z<64||x==60&&z==60&&y<2)continue;
            w.Buildings[c]=new(c,BuildKind.Wall);
        }
        w.Buildings[Door]=new(Door,BuildKind.Door);return w;
    }
    public static void Run()
    {
        var w=Fixture();var route=w.FindPath(Outside,Inside)!;long version=w.Navigation.Version;
        Check(route.Contains(Door)&&w.RoomAt(Inside).Enclosed,"open doorway and enclosed room");
        w.DamageBlock(Door,10);double durability=w.RemainingDurability(Door);
        w.Fluids.Add(Door,100,15);int water=w.Fluids.Amount(Door);
        var result=w.SetDoorLocked(Door,true);
        Check(result.Ok&&result.Message.Contains("无可达出口"),"locked sole exit warns about residents");
        Check(!w.Walkable(Door)&&!w.Navigation.CanReach(Outside,Inside)&&!w.Navigation.PathValid(Outside,route,version),"lock invalidates existing and new paths");
        Check(w.RoomAt(Inside).Enclosed&&w.RemainingDurability(Door)==durability,"permissions preserve room and damage");
        Check(w.FluidPassable(Door)&&w.Fluids.Amount(Door)==water,"ordinary door is not watertight and deletes no water");
        w=DemoWorld.LoadJson(w.SaveJson());
        Check(w.DoorLocked(Door)&&!w.Navigation.CanReach(Outside,Inside)&&w.RemainingDurability(Door)==durability,"locked save roundtrip");
        Check(w.SetDoorLocked(Door,false).Ok&&w.Navigation.CanReach(Outside,Inside),"unlock restores navigation");
        Check(!DemoWorld.LoadJson(w.SaveJson()).DoorLocked(Door),"unlocked save roundtrip");
        var invalid=JsonNode.Parse(w.SaveJson())!;
        invalid["Buildings"]!.AsArray().First(b=>b!["Cell"]!["X"]!.GetValue<int>()==68)!["Locked"]=true;
        try{DemoWorld.LoadJson(invalid.ToJsonString());throw new Exception("Invalid non-door lock accepted");}catch(InvalidDataException){}
        w.SetDoorLocked(Door,true);w.DeveloperErase(Door);
        Check(!w.DoorLocked(Door)&&w.Navigation.CanReach(Outside,Inside),"demolition opens path and removes lock");
        w.Buildings[Door]=new(Door,BuildKind.Door);Check(!w.DoorLocked(Door),"rebuilt door starts unlocked");

        w=Fixture();var actor=w.Workers[1];actor.X=60;actor.Z=60;
        Check(!w.SetDoorLocked(Door,true).Ok&&!w.DoorLocked(Door),"occupied door rejects lock");
        actor.X=59.3;actor.TravelFrom=new(59,60);actor.Path.Enqueue(Door);
        Check(!w.SetDoorLocked(Door,true).Ok,"in-flight crossing rejects lock");

        w=Fixture();actor=w.Workers[1];var target=w.Workers[0];actor.X=59.49;target.X=60.51;
        var sword=w.SpawnItem("sword",1,actor.Tile);sword.Owner=actor.Id;sword.Slot="main";
        Check(w.CanMelee(actor,target),"open doorway allows sword contact");w.SetDoorLocked(Door,true);
        Check(!w.CanMelee(actor,target),"locked door blocks weapon contact");

        w=Fixture();
        for(int x=56;x<=58;x++)for(int z=59;z<=61;z++){var c=new Cell(x,z);w.Buildings[c]=new(c,BuildKind.Wall);}
        var stair=new Cell(55,60);w.Buildings[stair]=new(stair,BuildKind.PlankStairs);
        var upper=new Cell(57,60,1);w.Workers[1].X=upper.X;w.Workers[1].Level=upper.Level;
        Check(w.FindPath(upper,Inside) is {Count:>0},"cross-floor route through doorway");
        w.SetDoorLocked(Door,true);Check(!w.Navigation.CanReach(upper,Inside),"locked door blocks cross-floor route");
        w.SetDoorLocked(Door,false);Check(w.Navigation.CanReach(upper,Inside),"cross-floor route restored");

        // Public food reservation is released when its route is interrupted.
        w=Fixture();actor=w.Workers[1];actor.Person.Hunger=30;w.SpawnItem("meal",1,Inside);
        Check(w.ActResident(new(actor.Id,"eat")).Ok&&w.AvailableFood()==0,"food reserved across door");
        Check(w.SetDoorLocked(Door,true).Ok&&actor.Person.Activity is null&&w.AvailableFood()==1,"blocked food activity releases reservation");
        Check(!w.ActResident(new(actor.Id,"eat")).Ok,"new food request cannot bypass lock");
        w=DemoWorld.LoadJson(w.SaveJson());actor=w.Workers[1];
        Check(w.SetDoorLocked(Door,false).Ok&&w.ActResident(new(actor.Id,"eat")).Ok,"saved lock can reopen and resume food");
        Advance(w,20);Check(w.Total("meal")==0&&actor.Person.Hunger>50,"one physical meal consumed after unlock");

        // Assigned beds are re-evaluated; resting cannot keep an unreachable bed reserved.
        w=Fixture();actor=w.Workers[1];var bed=new Cell(66,60);w.Buildings[bed]=new(bed,BuildKind.Bed);
        Check(w.ActResident(new(actor.Id,"rest")).Ok&&actor.Person.HomeBed==bed,"bed selected through doorway");
        w.SetDoorLocked(Door,true);Check(actor.Person.HomeBed is null&&actor.Person.Activity is null,"lock releases unreachable home and rest route");
        w.SetDoorLocked(Door,false);Check(w.ActResident(new(actor.Id,"rest")).Ok&&actor.Person.HomeBed==bed,"bed available again");

        // Carrying a patient must stop on the same side, release the bed, and resume after reopening.
        w=Fixture();actor=w.Workers[1];var patient=w.Workers[2];patient.X=56;patient.Z=60;
        w.Buildings[bed]=new(bed,BuildKind.Bed);w.SetMedicalBed(bed,true);w.InjureResident(patient.Id,"torso","fracture",90);
        Check(w.RescueResident(actor.Id,patient.Id).Ok,"rescue begins through door");
        for(int i=0;i<600&&actor.Person.Activity?.Kind!="rescue_carry";i++)w.Update(1d/60);
        Check(actor.Person.Activity?.Kind=="rescue_carry","patient picked up");
        w=DemoWorld.LoadJson(w.SaveJson());actor=w.Workers[1];patient=w.Workers[2];var drop=actor.Tile;
        Check(w.SetDoorLocked(Door,true).Ok&&actor.Person.Activity is null&&patient.Tile==drop&&patient.Person.MedicalBed is null,"locked rescue drops patient safely");
        Check(!w.RescueResident(actor.Id,patient.Id).Ok,"locked bed cannot be reserved");
        w.SetDoorLocked(Door,false);Check(w.RescueResident(actor.Id,patient.Id).Ok,"rescue can resume");
        Advance(w,25);Check(patient.Person.MedicalBed==bed,"resumed rescue reaches bed");

        w=Fixture();actor=w.Workers[1];actor.Person.WorkPriorities["construction"]=1;
        w.SpawnItem("cobblestone",20,Outside+new Cell(-1,0));var site=new Cell(64,58);
        Check(w.QueueBuild(site,BuildKind.Wall),"construction across doorway");var job=w.Jobs.Single(j=>j.Cell==site);
        for(int i=0;i<1200&&!job.Materials.Any(m=>m.Stage==1);i++)w.Update(1d/60);
        Check(job.Materials.Any(m=>m.Stage==1)&&actor.Job==job,"construction materials in transit");
        int total=w.Total("cobblestone");
        Check(w.SetDoorLocked(Door,true).Ok&&actor.Job is null&&job.Worker<0,"locked work route releases worker");
        Check(!job.Materials.Any(m=>m.Stage==1)&&w.Total("cobblestone")==total,"carried materials dropped exactly once");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,2);
        Check(!w.Buildings.ContainsKey(site)&&w.Total("cobblestone")==total,"blocked saved task neither builds nor loses stock");
        w.SetDoorLocked(Door,false);Advance(w,30);Check(w.Buildings.ContainsKey(site),"construction resumes after unlock");

        // Door state follows the immutable building when a supported group falls.
        w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.Buildings.Clear();w.FallBreakChance=0;
        var support=new Cell(80,80);var elevated=support+new Cell(0,0,1);
        w.Buildings[support]=new(support,BuildKind.Wall);w.Buildings[elevated]=new(elevated,BuildKind.Door);
        Check(w.SetDoorLocked(elevated,true).Ok,"upper door locks");w.Buildings.Remove(support);
        for(int i=0;i<100&&w.Buildings.ContainsKey(elevated);i++)w.AdvanceSupport(4096);
        Check(w.DoorLocked(support)&&!w.DoorLocked(elevated),"fall moves lock with door");
        Console.WriteLine("DOOR_ACCESS_OK: occupancy, routes, rooms, water, damage, food, beds, rescue, saves and demolition");
    }
}
