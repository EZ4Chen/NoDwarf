using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class ResidentHealthChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("HEALTH: "+message);}
    public static void Run()
    {
        var p=new ResidentProfile();Check(ResidentHealth.AddDisease(p,"fever"),"disease added");
        Check(!ResidentHealth.AddDisease(p,"fever")&&!ResidentHealth.AddDisease(p,"unknown"),"no duplicates or unknown diseases");
        var d=p.Diseases[0];d.Progress=99;ResidentHealth.Step(p,20);
        Check(d.Stage==1&&d.StageProgress>0&&d.Immunity>0,"stage boundary retains excess and immunity");
        d.Progress=150;d.Immunity=100;ResidentHealth.Step(p,500);
        Check(d.Stage==0&&d.Progress<100,"immunity starts gradual reverse stages");
        ResidentHealth.Step(p,1000);Check(p.Diseases.Count==0,"recovered disease removed");
        // Exact immunity crossing gives the same answer regardless of update subdivision.
        p=new();ResidentHealth.AddDisease(p,"fever");p.Diseases[0].Progress=150;p.Diseases[0].Immunity=99;
        var q=new ResidentProfile();ResidentHealth.AddDisease(q,"fever");q.Diseases[0].Progress=150;q.Diseases[0].Immunity=99;
        ResidentHealth.Step(p,30);for(int i=0;i<30;i++)ResidentHealth.Step(q,1);
        Check(Math.Abs(p.Diseases[0].Progress-q.Diseases[0].Progress)<1e-8,"immunity crossing independent of tick subdivision");
        p=new();ResidentHealth.AddDisease(p,"fever");p.Diseases[0].Progress=299;
        ResidentHealth.Step(p,30);Check(p.Diseases[0].Stage==2&&p.Diseases[0].StageProgress==100&&!p.Dead&&p.Incapacitated,"last stage capped and rescuable");
        p=new(){Hunger=0};ResidentHealth.Step(p,4000);Check(p.Malnutrition==100&&p.Consciousness<1,"prolonged hunger accumulates impairment");
        p.Hunger=30;ResidentHealth.Step(p,100);Check(p.Malnutrition==100,"intermediate food holds progression");
        p.Hunger=80;ResidentHealth.Step(p,100);Check(p.Malnutrition==95,"eating doesn't instantly cure malnutrition");
        ResidentHealth.Step(p,2000);Check(p.Malnutrition==0,"sustained food recovers");
        p=new(){Malnutrition=300};Check(p.Incapacitated&&!p.Dead,"severe illness needs care without instant new death mechanic");
        p=new();p.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=40});ResidentHealth.Step(p,100);
        Check(p.Blood<100,"untreated wound bleeds");double blood=p.Blood;p.Injuries[0].Treated=true;ResidentHealth.Step(p,10);
        Check(p.Blood>blood&&p.Injuries[0].Severity<40,"treatment stops bleeding and heals");
        p=new();p.Injuries.Add(new(){Part="left_arm",Kind="burn",Severity=50});ResidentHealth.Step(p,600);
        Check(p.Diseases.Any(d=>d.Id=="wound_infection"),"untreated severe open injury causes infection");
        ResidentHealth.Step(p,10);Check(p.Diseases.Count==1,"infection not duplicated each tick");
        q=new();q.Injuries.Add(new(){Part="left_arm",Kind="burn",Severity=50,Treated=true});ResidentHealth.Step(q,600);
        Check(q.Diseases.Count==0,"timely treatment prevents wound exposure");
        var w=CombatChecks.Fixture();var resident=w.Workers[0];w.InjureResident(resident.Id,"left_leg","fracture",85);
        var injury=resident.Person.Injuries.Single();Check(injury.PermanentSeverity>0,"severe fracture reserves sequela once");
        injury.Treated=true;injury.Fixed=true;ResidentHealth.Step(resident.Person,12000);
        Check(injury.Severity==injury.PermanentSeverity&&resident.Person.Mobility<1,"healing stops at permanent impairment");
        double residual=injury.PermanentSeverity;w.InfectResident(resident.Id,"fever");resident.Person.Diseases[0].Progress=125;resident.Person.Diseases[0].Immunity=85;resident.Person.Malnutrition=50;
        var loaded=DemoWorld.LoadJson(w.SaveJson());var saved=loaded.Workers[0].Person;
        Check(saved.Diseases.Single().Progress==125&&saved.Diseases[0].Immunity==85&&saved.Malnutrition==50&&saved.Injuries.Single().PermanentSeverity==residual,"all progress and sequela persist");
        ResidentHealth.Step(saved,0);Check(saved.Diseases[0].Progress==125,"paused does not advance");
        var legacy=JsonNode.Parse(w.SaveJson())!;legacy["Version"]=18;
        foreach(var r in legacy["Workers"]!.AsArray()) {var person=r!["Person"]!.AsObject();person.Remove("Diseases");person.Remove("Malnutrition");foreach(var i in person["Injuries"]!.AsArray()){i!.AsObject().Remove("PermanentSeverity");i.AsObject().Remove("UntreatedSeconds");i.AsObject().Remove("InfectionSeeded");}}
        loaded=DemoWorld.LoadJson(legacy.ToJsonString());Check(loaded.Workers[0].Person.Diseases.Count==0&&loaded.Workers[0].Person.Malnutrition==0&&loaded.Workers[0].Person.Injuries[0].PermanentSeverity==0,"legacy injuries not retroactively made permanent");
        var invalid=JsonNode.Parse(w.SaveJson())!;invalid["Workers"]![0]!["Person"]!["Diseases"]![0]!["Immunity"]=101;
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid health rejected");
        // Real world cadence, not only isolated state updates.
        w=CombatChecks.Fixture();resident=w.Workers[0];w.InfectResident(resident.Id,"fever");double before=resident.Person.Diseases[0].Progress;
        for(int i=0;i<120;i++)w.Update(1d/30);Check(resident.Person.Diseases[0].Progress>before,"world health update connected");
        Console.WriteLine("RESIDENT_HEALTH_OK: disease stages/immunity/recovery, nutrition, bleeding, infection, sequelae, tick crossing, archive migration and world integration");
    }
}
