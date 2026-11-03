using NoDwarf.Simulation;
using System.Text.Json.Nodes;

internal static class SafetyAreaChecks
{
    private static void Check(bool ok,string why){if(!ok)throw new Exception("SAFETY: "+why);}
    private static SafetyVolume Box(int x1,int x2,int y1=0,int y2=0)=>new(new(x1,59,y1),new(x2,61,y2));
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<seconds*60;i++)w.Update(1d/60);}
    private static DemoWorld Corridor()
    {
        var w=CombatChecks.Fixture();w.Buildings.Clear();
        using(w.BatchBlockChanges())for(int x=54;x<=76;x++)for(int z=59;z<=61;z++)for(int y=0;y<=2;y++)
        {
            if(x>54&&x<76&&z==60&&y<2)continue;
            var c=new Cell(x,z,y);w.Buildings[c]=new(c,BuildKind.Wall);
        }
        w.Workers[0].X=70;w.Workers[0].Z=60;
        w.Workers[1].X=56;w.Workers[1].Z=60;
        w.Workers[2].X=57;w.Workers[2].Z=60;
        foreach(var a in w.Workers){a.Person.Hunger=90;a.Person.Rest=90;a.Person.Thirst=90;}
        return w;
    }
    public static void Run()
    {
        var w=Corridor();var from=new Cell(56,60);var goal=new Cell(70,60);
        var rules=new SafetyRules{WorkAreas=new[]{Box(55,58),Box(68,75)},ForbiddenAreas=new[]{Box(64,64)}};
        var actor=w.Workers[1];foreach(var step in w.FindPath(from,goal)!)actor.Path.Enqueue(step);actor.CheckedNavigationVersion=w.Navigation.Version;
        w.ConfigureSafety(rules);
        Check(actor.Path.Count==0,"existing route invalidated when first restricted graph is created");
        Check(w.SafetyWorkAllowed(from)&&w.SafetyWorkAllowed(goal)&&w.ResidentNavigation.FindPath(from,goal) is null,"valid endpoints cannot bypass forbidden middle");
        Check(w.Navigation.FindPath(from,goal) is not null,"restriction is permission, not a wall for enemies");
        Check(w.LifeAlerts.Any(a=>a.Id=="safety:"+actor.Id&&a.Text.Contains("口粮")&&a.Location==actor.Tile),"supply warning is locatable");
        w.ConfigureSafety(rules with{ForbiddenAreas=Array.Empty<SafetyVolume>()});
        Check(w.ResidentNavigation.FindPath(from,goal) is {Count:>0},"work area constrains targets, not transit between two areas");
        w.ConfigureSafety(rules);w.DraftResident(actor.Id,true);
        Check(!w.MilitaryMove(actor.Id,goal).Ok&&!w.MilitaryMove(actor.Id,new(64,60)).Ok,"drafted commands obey forbidden paths and targets");
        var restored=DemoWorld.LoadJson(w.SaveJson());
        Check(restored.Safety.ForbiddenAreas.Count==1&&restored.ResidentNavigation.FindPath(from,goal) is null,"safety save roundtrip");
        var invalid=JsonNode.Parse(w.SaveJson())!;invalid["Safety"]!["ForbiddenAreas"]![0]!["Max"]!["X"]=99999;
        try{DemoWorld.LoadJson(invalid.ToJsonString());throw new Exception("invalid volume accepted");}catch(InvalidDataException){}

        // A real workshop bill waits for outside inputs, then resumes without duplicate batches.
        w=new DemoWorld(flat:true);w.DeveloperResidents(false);var workshop=new Cell(20,20);
        foreach(var part in DemoWorld.ExampleBlueprint())Check(w.DeveloperPlace(workshop+part.Offset,part.Kind,part.Facing),"workshop fixture");
        Check(w.QueueProductionRecipe("sword"),"one sword batch queued");
        int iron=w.Total("iron"),coal=w.Total("coal"),swords=w.Total("sword");
        w.ConfigureSafety(new(){WorkAreas=new[]{new SafetyVolume(new(19,19,-1),new(27,26,4))},StopOutsideSupplies=true});
        w.DeveloperResidents(true);foreach(var resident in w.Workers)resident.Person.Schedule=ResidentWork.FreeDay();Advance(w,5);
        Check(w.Jobs.Count==1&&w.Jobs[0].SafetyPaused&&w.Workers.All(a=>a.Job is null)&&w.Total("sword")==swords,"outside inputs keep one paused workshop job");
        w=DemoWorld.LoadJson(w.SaveJson());w.ConfigureSafety(w.Safety with{StopOutsideSupplies=false});
        for(int i=0;i<180*60&&w.ProductionOrders.Any(o=>o.Target>0);i++)w.Update(1d/60);
        Check(w.ProductionOrders.All(o=>o.Target==0)&&w.Total("sword")==swords+1&&w.Total("iron")==iron-2&&w.Total("coal")==coal-1,"workshop resumes and consumes exact recipe once");

        w=Corridor();actor=w.Workers[0];actor.Person.WorkPriorities["construction"]=1;
        w.SpawnItem("cobblestone",20,goal);Check(w.QueueBuild(new(72,60),BuildKind.Wall),"standing fixture");
        w.ConfigureSafety(new(){WorkAreas=new[]{new SafetyVolume(new(72,60,0),new(72,60,0))}});Advance(w,3);
        Check(actor.Job is null&&!w.Buildings.ContainsKey(new(72,60)),"allowed target alone does not permit an outside work stand");

        // Pending material reservations release without deleting the construction plan.
        w=Corridor();actor=w.Workers[0];actor.Person.WorkPriorities["construction"]=1;
        var stack=w.SpawnItem("cobblestone",20,from);var site=new Cell(72,60);
        Check(w.QueueBuild(site,BuildKind.Wall),"build fixture");var job=w.Jobs.Single(j=>j.Cell==site);
        int cost=job.Materials.Sum(m=>m.Count);
        w.ConfigureSafety(new(){WorkAreas=new[]{Box(68,75,-1,2)},StopOutsideSupplies=true});
        Check(job.SafetyPaused&&job.Materials.All(m=>m.StackId==0)&&w.Available("cobblestone")==20,"pause releases source reservations");
        w.Items.Remove(stack);w=DemoWorld.LoadJson(w.SaveJson());Advance(w,1);
        Check(!w.Buildings.ContainsKey(site)&&w.Jobs.Single().SafetyPaused,"consumed former source does not break save or allow free building");
        w.SpawnItem("cobblestone",cost,goal);Advance(w,20);
        Check(w.Buildings.ContainsKey(site)&&w.Total("cobblestone")==0,"new allowed materials re-reserved and consumed once");

        // Work range changes interrupt a real carried load and resume after restoring permission.
        w=Corridor();actor=w.Workers[0];actor.Person.WorkPriorities["construction"]=1;w.SpawnItem("cobblestone",20,new(69,60));
        w.QueueBuild(site,BuildKind.Wall);job=w.Jobs.Single();
        for(int i=0;i<1200&&!job.Materials.Any(m=>m.Stage==1);i++)w.Update(1d/60);
        Check(job.Materials.Any(m=>m.Stage==1),"real materials carried");int total=w.Total("cobblestone");
        w.ConfigureSafety(new(){WorkAreas=new[]{Box(55,58)}});
        Check(actor.Job is null&&job.SafetyPaused&&w.Total("cobblestone")==total&&!job.Materials.Any(m=>m.Stage==1),"restriction drops real cargo and pauses work");
        w=DemoWorld.LoadJson(w.SaveJson());w.ConfigureSafety(new());Advance(w,25);
        Check(w.Buildings.ContainsKey(site),"saved paused work resumes after restriction lifted");

        // Source permission is distinct from work destinations.
        w=Corridor();actor=w.Workers[0];actor.Person.Hunger=25;w.SpawnItem("meal",1,from);
        w.ConfigureSafety(new(){WorkAreas=new[]{Box(68,75)},StopOutsideSupplies=true});
        Check(!w.ActResident(new(actor.Id,"eat")).Ok,"outside food blocked by stop-outside switch");
        w.ConfigureSafety(w.Safety with{StopOutsideSupplies=false});
        Check(w.ActResident(new(actor.Id,"eat")).Ok,"outside food allowed when source restriction disabled");
        w.ConfigureSafety(w.Safety with{ForbiddenAreas=new[]{Box(64,64)}});
        Check(actor.Person.Activity is null&&w.Total("meal")==1,"in-flight food route cancelled with food conserved");
        w.ConfigureSafety(w.Safety with{ForbiddenAreas=Array.Empty<SafetyVolume>()});Advance(w,25);
        Check(actor.Person.Hunger>40&&w.Total("meal")==0,"permitted outside food is actually consumed");
        actor.Person.Thirst=20;w.SpawnItem("water_bucket",1,from);
        Check(w.SupplyCare(actor.Id,actor.Id,"water_bucket").Ok,"self water outside work area");Advance(w,15);
        Check(actor.Person.Thirst>50&&w.Total("water_bucket")==0&&w.Total("bucket")==1,"outside water used once and empty bucket returned");

        // Alert care must complete while ordinary work stays suspended, including during sleep hours.
        w=Corridor();var patient=w.Workers[0];var helper=w.Workers[1];helper.X=69;w.Workers[2].X=71;
        helper.Person.WorkPriorities["care"]=1;
        helper.Person.Schedule=Enumerable.Repeat("sleep",24).ToArray();
        w.InjureResident(patient.Id,"torso","fracture",90);patient.Person.Hunger=20;patient.Person.Thirst=20;
        w.SpawnItem("meal",1,new(73,60));w.SpawnItem("water_bucket",1,new(73,60));
        w.ConfigureSafety(new(){WorkAreas=new[]{Box(68,75)},StopOutsideSupplies=true});
        Check(w.SetRallyPoint(new(72,60)).Ok&&w.SetCivilianAlert(true).Ok,"safe rally enabled");
        Advance(w,40);
        Check(patient.Person.Hunger>40&&patient.Person.Thirst>50&&w.Total("meal")==0&&w.Total("water_bucket")==0,"rally feeding and water use real supplies");
        Check(w.CivilianAlert&&helper.Job is null,"care does not lift rally or resume ordinary work");

        w=Corridor();Check(w.SetRallyPoint(new(72,60)).Ok&&w.SetCivilianAlert(true).Ok,"rally fixture");Advance(w,12);
        w.ConfigureSafety(new(){WorkAreas=new[]{new SafetyVolume(new(72,60),new(72,60))}});Advance(w,3);
        Check(w.Workers.All(a=>a.Combat.Destination is null||w.SafetyWorkAllowed(a.Combat.Destination.Value)),"range change clears assigned rally stands outside work area");
        w.ConfigureSafety(new(){WorkAreas=new[]{Box(55,58)}});
        Check(!w.SetCivilianAlert(true).Ok,"restricted rally point cannot activate alert");

        // Medical/rescue routes use the same full-path restriction and release their reservations.
        w=Corridor();patient=w.Workers[0];helper=w.Workers[1];w.InjureResident(patient.Id,"torso","fracture",90);
        var bed=new Cell(74,60);w.Buildings[bed]=new(bed,BuildKind.Bed);w.SetMedicalBed(bed,true);
        w.ConfigureSafety(rules);
        Check(!w.RescueResident(helper.Id,patient.Id).Ok,"rescue cannot cross forbidden middle");
        w.ConfigureSafety(rules with{ForbiddenAreas=Array.Empty<SafetyVolume>()});
        Check(w.RescueResident(helper.Id,patient.Id).Ok,"rescue starts after route reopened");
        for(int i=0;i<1800&&helper.Person.Activity?.Kind!="rescue_carry";i++)w.Update(1d/60);
        Check(helper.Person.Activity?.Kind=="rescue_carry","actual patient pickup");var carriedAt=helper.Tile;
        w.ConfigureSafety(new(){WorkAreas=new[]{Box(55,58)}});
        Check(helper.Person.Activity is null&&patient.Tile==carriedAt&&patient.Person.MedicalBed is null,"restriction safely drops patient and frees bed");
        w=DemoWorld.LoadJson(w.SaveJson());w.ConfigureSafety(new());
        Check(w.RescueResident("medic","guard").Ok,"saved interrupted rescue can restart");Advance(w,25);
        Check(w.Workers[0].Person.MedicalBed==bed,"rescue finishes after permission restored");

        w=Corridor();patient=w.Workers[0];helper=w.Workers[1];patient.X=57;bed=new(74,60);
        w.InjureResident(patient.Id,"torso","fracture",90);w.Buildings[bed]=new(bed,BuildKind.Bed);w.SetMedicalBed(bed,true);
        w.ConfigureSafety(rules with{ForbiddenAreas=Array.Empty<SafetyVolume>()});
        Check(w.RescueResident(helper.Id,patient.Id).Ok,"rescue between separate work areas");Advance(w,40);
        Check(patient.Person.MedicalBed==bed,"carried patient may transit outside work area with no forbidden zone");

        w=Corridor();w.InjureResident("guard","left_arm","cut",30);w.SpawnItem("bandage",1,from);w.ConfigureSafety(rules);
        Check(!w.ActResident(new("medic","treat","guard")).Ok,"medical supplies cannot cross forbidden route");
        w.ConfigureSafety(rules with{ForbiddenAreas=Array.Empty<SafetyVolume>()});
        Check(w.ActResident(new("medic","treat","guard")).Ok,"medical route restored");Advance(w,25);
        Check(w.Total("bandage")==0&&ResidentHealth.Bleeding(w.Workers[0].Person)==0,"real bandage delivered and applied");

        // Defining a forbidden region over a resident allows only exit, not work or through travel.
        w=Corridor();actor=w.Workers[1];w.ConfigureSafety(new(){ForbiddenAreas=new[]{Box(55,58)}});Advance(w,4);
        Check(actor.Tile.X==59&&w.SafetyTransitAllowed(actor.Tile),"resident exits at first safe cell");
        Check(w.ResidentNavigation.FindPath(actor.Tile,new(56,60)) is null,"exit does not grant re-entry");

        // The restricted graph retains existing upper-floor navigation candidates.
        w=CombatChecks.Fixture();var upper=new Cell(65,60,1);
        for(int x=64;x<=68;x++)for(int z=59;z<=61;z++){var c=new Cell(x,z);w.Buildings[c]=new(c,BuildKind.Wall);}
        var stair=new Cell(63,60);w.Buildings[stair]=new(stair,BuildKind.PlankStairs);
        w.ConfigureSafety(new(){ForbiddenAreas=new[]{new SafetyVolume(new(100,100,0),new(101,101,2))}});
        Check(w.ResidentNavigation.FindPath(new(60,60),upper) is not null,"upper floor remains navigable after lazy graph creation");
        w.ConfigureSafety(new(){ForbiddenAreas=new[]{new SafetyVolume(stair,stair+new Cell(0,0,2))}});
        var detour=w.ResidentNavigation.FindPath(new(60,60),upper);
        Check(detour is null||detour.All(w.SafetyTransitAllowed),"cross-floor route never enters forbidden stair");
        w.ConfigureSafety(new(){ForbiddenAreas=new[]{new SafetyVolume(new(64,59,1),new(68,61,1))}});
        Check(w.ResidentNavigation.FindPath(new(60,60),upper) is null&&w.SafetyTransitAllowed(new(65,60,0)),"upper-floor restriction is level-specific");
        Console.WriteLine("SAFETY_AREAS_OK: full routes, targets, source permission, reservation release, current save, real cargo, escape and cross-floor traversal");
    }
}
