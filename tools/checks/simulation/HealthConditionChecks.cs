using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class HealthConditionChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("HEALTH_CONDITIONS: "+message);}
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)(seconds*30);i++)w.Update(1d/30);}
    private static ResidentProfile Sick(string id,double progress=150)
    {var p=new ResidentProfile();ResidentHealth.AddDisease(p,id);p.Diseases[0].Progress=progress;return p;}
    public static void Run()
    {
        Check(ResidentCatalog.Parts.Length==10,"body parts unchanged");
        var fever=Sick("fever");var infection=Sick("wound_infection");
        Check(fever.Manipulation<infection.Manipulation&&infection.Pain>fever.Pain&&infection.Mobility<fever.Mobility,"illnesses have distinct symptoms");
        var p=new ResidentProfile{Malnutrition=120,Blood=70};p.Injuries.Add(new(){Part="left_leg",Kind="fracture",Severity=30,PermanentSeverity=5});
        var states=HealthConditions.States(p).ToArray();Check(states.Length==4&&states.Any(c=>c.Definition.Id=="sequela"&&c.Part=="left_leg")&&states.Single(c=>c.Definition.Id=="blood_loss").Severity==30,"unified view without duplicated loss");
        p.Blood=90;Check(HealthConditions.States(p).Single(c=>c.Definition.Id=="blood_loss").Severity==10,"view follows authoritative blood");
        Check(HealthConditions.All.Select(c=>c.Id).Distinct().Count()==HealthConditions.All.Length&&HealthConditions.All.All(c=>c.Stages[0].Start==0&&c.Stages.Zip(c.Stages.Skip(1)).All(pair=>pair.First.Start<pair.Second.Start)&&c.Stages[^1].Start<c.Maximum),"valid configurable stages");
        var low=Sick("fever");var high=Sick("fever");var untreated=Sick("fever");HealthCare.Treat(low,.2);HealthCare.Treat(high,.9);
        ResidentHealth.Step(low,100);ResidentHealth.Step(high,100);ResidentHealth.Step(untreated,100);
        Check(high.Diseases[0].Progress<low.Diseases[0].Progress&&low.Diseases[0].Progress<untreated.Diseases[0].Progress,"quality slows worsening");
        Check(high.Diseases[0].Immunity==untreated.Diseases[0].Immunity,"treatment does not directly boost immunity");
        // Both expiry and immunity crossing inside the same update must split correctly.
        low=Sick("fever");high=Sick("fever");foreach(var sample in new[]{low,high}){HealthCare.Treat(sample,.8);sample.Diseases[0].Treatment.Remaining=10;sample.Diseases[0].Immunity=99;}
        ResidentHealth.Step(low,30);for(int i=0;i<30;i++)ResidentHealth.Step(high,1);
        Check(Math.Abs(low.Diseases[0].Progress-high.Diseases[0].Progress)<1e-8&&low.Diseases[0].Treatment.Remaining==0,"expiry and immunity split equivalently");
        p=new();p.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=40});HealthCare.Treat(p,.8);
        double duration=p.Injuries[0].Treatment.Remaining;ResidentHealth.Step(p,duration+2);
        Check(ResidentHealth.Bleeding(p)==0&&p.Injuries[0].Treated&&HealthCare.Needed(p),"expired dressing doesn't reopen bleeding; renewed care available");
        var weak=new ResidentProfile();var skilled=new ResidentProfile();skilled.Earn("medicine",8000);
        Check(HealthCare.Quality(skilled)>HealthCare.Quality(weak),"doctor skill changes quality");double healthyQuality=HealthCare.Quality(skilled);
        skilled.Injuries.Add(new(){Part="left_hand",Kind="fracture",Severity=80});Check(HealthCare.Quality(skilled)<healthyQuality,"medical ability affects quality");
        p=new();p.Injuries.Add(new(){Part="left_leg",Kind="fracture",Severity=10,PermanentSeverity=10,Treated=true});Check(!HealthCare.Needed(p),"permanent impairment cannot consume dressings forever");
        p=Sick("fever");p.Diseases[0].Immunity=100;Check(!HealthCare.Needed(p),"recovering immune disease doesn't request care");
        var w=CombatChecks.Fixture();var patient=w.Workers[0];var medic=w.Workers[1];w.InfectResident(patient.Id,"fever");w.SpawnItem("bandage",3,medic.Tile);
        medic.Person.WorkPriorities["doctor"]=1;
        Check(w.ActResident(new(medic.Id,"treat",patient.Id)).Ok,"manual disease care fetches public supplies");
        Check(!w.ActResident(new(w.Workers[2].Id,"treat",patient.Id)).Ok,"one carer per patient");
        Advance(w,40);Check(w.Total("bandage")==2&&patient.Person.Diseases[0].Treatment.Remaining>0,"real route and one consumed supply for disease");
        Check(!w.ActResident(new(medic.Id,"treat",patient.Id)).Ok,"active care cannot be spammed");
        double remaining=patient.Person.Diseases[0].Treatment.Remaining,quality=patient.Person.Diseases[0].Treatment.Quality;
        var loaded=DemoWorld.LoadJson(w.SaveJson());Check(loaded.Workers[0].Person.Diseases[0].Treatment.Remaining==remaining&&loaded.Workers[0].Person.Diseases[0].Treatment.Quality==quality,"treatment saved precisely");
        Advance(w,20);Advance(loaded,20);Check(Math.Abs(w.Workers[0].Person.Diseases[0].Progress-loaded.Workers[0].Person.Diseases[0].Progress)<1e-8,"resume same progression");
        patient.Person.Diseases[0].Treatment.Remaining=0;Advance(w,30);Check(w.Total("bandage")==1&&patient.Person.Diseases[0].Treatment.Remaining>0,"automatic doctor renews expired disease care");
        // Active medical supply reservation survives reload and cancellation without consumption.
        w=CombatChecks.Fixture();patient=w.Workers[0];medic=w.Workers[1];w.InfectResident(patient.Id,"fever");w.SpawnItem("bandage",1,new(66,60));
        w.ActResident(new(medic.Id,"treat",patient.Id));loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(loaded.Available("bandage")==0,"disease supply reservation restored");loaded.ActResident(new(medic.Id,"cancel"));Check(loaded.Available("bandage")==1,"cancel releases supply");
        Advance(w,40);Check(w.Total("bandage")==0&&patient.Person.Diseases[0].Treatment.Remaining>0,"delivery completes");
        var invalid=JsonNode.Parse(w.SaveJson())!;invalid["Workers"]![0]!["Person"]!["Diseases"]![0]!["Treatment"]!["Remaining"]=-1;
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid treatment archive rejected");
        Console.WriteLine("HEALTH_CONDITIONS_OK: unified definitions/views, distinct abilities, quality/expiry, immune independence, supply/cancel/renewal, save roundtrip");
    }
}
