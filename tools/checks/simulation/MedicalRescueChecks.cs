using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class MedicalRescueChecks
{
    static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("RESCUE: "+why);}
    static void Advance(DemoWorld w,double t){for(int i=0;i<t*30;i++)w.Update(1d/30);}
    static DemoWorld Fixture()
    {
        var w=CombatChecks.Fixture();w.InjureResident("guard","torso","fracture",90);
        var bed=new Cell(74,60);w.Buildings[bed]=new(bed,BuildKind.Bed);Check(w.SetMedicalBed(bed,true).Ok,"designate bed");return w;
    }
    static void UntilCarry(DemoWorld w)
    {for(int i=0;i<600&&w.Workers[1].Person.Activity?.Kind!="rescue_carry";i++)w.Update(1d/30);Check(w.Workers[1].Person.Activity?.Kind=="rescue_carry","pickup transitions to carrying");}
    public static void Run()
    {
        var w=Fixture();var patient=w.Workers[0];
        w.InjureResident(patient.Id,"left_hand","missing",100);
        Check(ResidentHealth.Bleeding(patient.Person)>0&&HealthCare.Needed(patient.Person),"fresh amputation bleeds");
        Check(!w.RescueResident("medic","guard").Ok,"hemostasis before transport");
        HealthCare.Treat(patient.Person,.7);
        Check(ResidentHealth.Bleeding(patient.Person)==0&&patient.Person.Injuries.Single(i=>i.Kind=="missing").Severity==100,"bandaging stops blood without regrowing limb");
        var old=new ResidentProfile();old.Injuries.Add(new(){Part="left_hand",Kind="missing",Severity=100});
        Check(ResidentHealth.Bleeding(old)==0&&!HealthCare.Needed(old),"old absent limbs remain stable");
        w=CombatChecks.Fixture();w.InjureResident("guard","left_hand","missing",100);w.SpawnItem("bandage",1,new(55,60));
        Check(w.ActResident(new("medic","treat","guard")).Ok,"fresh amputation uses actual care job");Advance(w,20);
        Check(ResidentHealth.Bleeding(w.Workers[0].Person)==0&&w.Total("bandage")==0,"hemostasis consumes one physical bandage");
        w=Fixture();Check(w.RescueResident("medic","guard").Ok,"reserve rescue");
        Check(!w.RescueResident("civilian","guard").Ok,"exclusive patient reservation");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Workers[1].Person.Activity?.Kind=="rescue_pickup","pickup archive");
        UntilCarry(w);Advance(w,1);Check(w.Workers[0].Tile==w.Workers[1].Tile&&w.Workers[0].Person.MedicalBed is null,"travels alongside carrier, no teleport to bed");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Workers.Count==3&&w.Workers[1].Person.Activity?.Kind=="rescue_carry","carry archive");
        Advance(w,45);Check(w.Workers[0].Person.MedicalBed==new Cell(74,60)&&w.Workers[1].Person.Activity is null,"arrival and bed occupancy");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Workers[0].Person.MedicalBed is not null,"occupied bed persists");
        w.InjureResident("civilian","torso","fracture",90);Check(!w.RescueResident("medic","civilian").Ok,"occupied bed unavailable");
        Check(w.SetMedicalBed(new(74,60),false).Ok&&w.Workers[0].Person.MedicalBed is null,"release occupied bed");
        w=Fixture();w.RescueResident("medic","guard");UntilCarry(w);Advance(w,1);var pos=w.Workers[1].Tile;
        w.SetMedicalBed(new(74,60),false);Check(w.Workers[1].Person.Activity is null&&w.Workers[0].Tile==pos,"cancel drops at carrier and releases reservation");
        w=Fixture();w.RescueResident("medic","guard");UntilCarry(w);w.Buildings.Remove(new(74,60));Advance(w,1);Check(w.Workers[1].Person.Activity is null,"destroyed bed cancels safely");
        w=Fixture();w.RescueResident("medic","guard");UntilCarry(w);w.InjureResident("medic","torso","fracture",90);Advance(w,1);Check(w.Workers[1].Person.Activity is null,"incapacitated carrier releases patient");
        w=Fixture();w.InjureResident("civilian","torso","fracture",90);w.Workers.Add(new(){Id="helper2",Name="搬运者",X=56,Z=60});
        Check(w.RescueResident("medic","guard").Ok&&!w.RescueResident("helper2","civilian").Ok,"one bed cannot be reserved by two patients");
        w=Fixture();w.RescueResident("medic","guard");UntilCarry(w);pos=w.Workers[1].Tile;w.DraftResident("medic",true);
        Check(w.Workers[0].Tile==pos&&w.Workers[1].Person.Activity is null,"draft interrupts carry safely");
        w=Fixture();w.RescueResident("medic","guard");UntilCarry(w);
        var occupied=w.Workers[1].Tile;
        foreach(var delta in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
            for(int y=0;y<3;y++){var block=occupied+delta;block=new(block.X,block.Z,y);w.Buildings[block]=new(block,BuildKind.Wall);}
        Advance(w,3);Check(w.Workers[1].Person.Activity is null&&w.Workers[0].Person.MedicalBed is null,"blocked route drops patient rather than claiming arrival");
        // Upper-floor bed reached through stairs, including archive during travel.
        w=Fixture();w.SetMedicalBed(new(74,60),false);w.Buildings.Remove(new(74,60));
        for(int x=64;x<=69;x++)for(int z=59;z<=61;z++){var c=new Cell(x,z);w.Buildings[c]=new(c,BuildKind.Wall);}
        var stair=new Cell(63,60);w.Buildings[stair]=new(stair,BuildKind.PlankStairs);
        var upstairs=new Cell(68,60,1);w.Buildings[upstairs]=new(upstairs,BuildKind.Bed);w.SetMedicalBed(upstairs,true);
        Check(w.RescueResident("medic","guard").Ok,"cross-floor route accepted");UntilCarry(w);w=DemoWorld.LoadJson(w.SaveJson());Advance(w,60);
        Check(w.Workers[0].Level==1&&w.Workers[0].Person.MedicalBed==upstairs,"cross-floor delivery");
        // Doctor chooses distant dangerous bleeding over a nearby small cut.
        w=CombatChecks.Fixture();var minor=w.Workers[0];var critical=w.Workers[2];minor.X=58;critical.X=70;
        w.InjureResident(minor.Id,"left_arm","cut",10);w.InjureResident(critical.Id,"left_arm","missing",100);critical.Person.Blood=25;
        var medic=w.Workers[1];medic.Person.WorkPriorities["doctor"]=1;var bandage=w.SpawnItem("bandage",2,medic.Tile);bandage.Owner=medic.Id;
        Advance(w,.2);Check(medic.Person.Activity?.Target==critical.Id,"triage beats distance");
        // Automatic care uses the same rescue path and bed reservation.
        w=Fixture();w.Workers[1].Person.WorkPriorities["care"]=1;Advance(w,45);Check(w.Workers[0].Person.MedicalBed is not null,"automatic rescue");
        var invalid=JsonNode.Parse(w.SaveJson())!;invalid["Workers"]![2]!["Person"]!["MedicalBed"]=JsonNode.Parse("{\"X\":74,\"Z\":60,\"Level\":0}");
        // Invalid occupancy location is safely released on restore.
        var loaded=DemoWorld.LoadJson(invalid.ToJsonString());Check(loaded.Workers[2].Person.MedicalBed is null,"stale occupant released");
        Console.WriteLine("MEDICAL_RESCUE_OK: triage, amputation, reservations, transport, cross-floor, interruption, automatic care and archives");
    }
}
