namespace NoDwarf.Simulation;
public static class LifeExpansionChecks
{
    static void Check(bool b,string message){if(!b)throw new InvalidOperationException("LIFE: "+message);}
    static void Advance(DemoWorld w,double seconds){for(int i=0;i<seconds*10;i++)w.Update(.1);}
    public static void Run()
    {
        var w=CombatChecks.Fixture();var p=w.Workers[0].Person;
        p.Thirst=0;LifeHealth.Step(p,600,15);Check(p.Dehydration>0,"thirst causes dehydration");p.Thirst=100;double before=p.Dehydration;LifeHealth.Step(p,10,15);Check(p.Dehydration<before,"water slowly reverses dehydration");
        p=new();LifeHealth.Step(p,100,-20);Check(p.ColdExposure>0,"cold exposure");before=p.ColdExposure;LifeHealth.Step(p,10,20);Check(p.ColdExposure<before,"warming reverses cold");LifeHealth.Step(p,100,55);Check(p.HeatExposure>0,"heat exposure");
        p=new(){Malnutrition=300};LifeHealth.Step(p,599,20);Check(!p.Dead,"fatal grace period");LifeHealth.Step(p,1,20);Check(p.Dead&&p.DeathCause=="长期饥饿","persistent fatal cause");
        p=new();p.Injuries.Add(new(){Kind="burn",Part="left_arm",Severity=90});p.Injuries.Add(new(){Kind="burn",Part="right_arm",Severity=90});Check(p.Incapacitated&&!p.Dead&&p.MoveRate==0,"pain incapacitates without killing");
        w=CombatChecks.Fixture();w.InjureResident("guard","left_leg","fracture",50);w.SpawnItem("splint",1,new(56,60));
        Check(w.SupplyCare("medic","guard","splint").Ok,"reserve splint");Check(!w.SupplyCare("civilian","guard","splint").Ok,"no duplicate treatment");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,25);Check(w.Workers[0].Person.Injuries.Single().Fixed&&w.Total("splint")==0,"physical splint applied and saved");
        w=CombatChecks.Fixture();w.InjureResident("guard","left_hand","missing",100);HealthCare.Treat(w.Workers[0].Person,.8);w.SpawnItem("prosthesis",1,new(56,60));
        Check(w.SupplyCare("medic","guard","prosthesis","left_hand").Ok,"prosthesis task");Advance(w,25);Check(w.Workers[0].Person.Integrity("left_hand")==.5&&w.Total("prosthesis")==0,"partial function, permanent loss remains");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Workers[0].Person.Prostheses.Count==1,"prosthesis archive");
        w=CombatChecks.Fixture();w.Workers[0].Person.Thirst=10;w.SpawnItem("water_bucket",1,new(56,60));Check(w.SupplyCare("medic","guard","water_bucket").Ok,"send water");Advance(w,20);Check(w.Workers[0].Person.Thirst>50&&w.Total("water_bucket")==0&&w.Total("bucket")==1,"one bucket consumed and returned");
        w=CombatChecks.Fixture();var source=new Cell(65,60);w.Fluids.Add(source,1000);w.SetWaterPoint(source,true);w.SpawnItem("bucket",1,new(57,60));w.Workers[0].Person.Thirst=10;
        Check(w.SupplyCare("medic","guard","water_bucket").Ok,"empty bucket fetch");w=DemoWorld.LoadJson(w.SaveJson());Advance(w,35);
        Check(w.Workers[0].Person.Thirst>50&&w.Fluids.Removed==5&&w.Fluids.Conserved,"finite source, safe shore and archive");
        w=CombatChecks.Fixture();var food=w.SpawnItem("meal",2,new(60,60),new(){ExpiresAt=1});Advance(w,3);Check(w.Total("meal")==0&&w.Total("rot")==2,"food becomes inedible rot");
        w=CombatChecks.Fixture();w.InfectResident("guard","fever");w.Workers[1].X=61;w.Workers[1].Z=60;Advance(w,125);Check(w.Workers[1].Person.Diseases.Any(d=>d.Id=="fever"),"nearby exposure transmits fever");
        w=CombatChecks.Fixture();w.InfectResident("guard","fever");w.Workers[0].Person.Isolated=true;w.Workers[1].X=61;w.Workers[1].Z=60;Advance(w,125);Check(w.Workers[1].Person.Diseases.Count==0,"contact isolation prevents fever transmission");
        w=CombatChecks.Fixture();var doctor=w.Workers[1];doctor.Person.WorkPriorities["doctor"]=4;doctor.Person.Activity=new(){Kind="rest",Destination=doctor.Tile,Origin="schedule"};
        w.InjureResident("guard","left_hand","missing",100);w.Workers[0].Person.Blood=10;w.SpawnItem("bandage",1,doctor.Tile);Advance(w,.2);
        Check(doctor.Person.Activity?.Origin=="emergency","urgent care interrupts ordinary sleep/work");
        w.RefreshLifeAlerts();Check(w.LifeAlerts.Any(a=>a.Id.StartsWith("bleed:"))&&w.LifeAlerts.Any(a=>a.Id=="food"),"actionable alerts");
        w=CombatChecks.Fixture();var table=new Cell(63,60);w.Buildings[table]=new(table,BuildKind.Table);var chair=new Cell(62,60);w.Buildings[chair]=new(chair,BuildKind.Chair);
        var meal=w.SpawnItem("meal",1,w.Workers[0].Tile);meal.Owner="guard";w.Workers[0].Person.Hunger=20;w.ActResident(new("guard","eat"));Advance(w,10);
        Check(w.Workers[0].Person.MoodEffects.Any(m=>m.Text.Contains("餐桌")),"dining furniture provides actual benefit");
        var game=new Cell(58,62);w.Buildings[game]=new(game,BuildKind.GameTable);w.ActResident(new("civilian","relax"));Advance(w,5);Check(w.Workers[2].Status.Contains("棋桌"),"game table destination");
        // Cancellation and source removal keep physical supplies; no completed effect is fabricated.
        w=CombatChecks.Fixture();w.Workers[0].Person.Thirst=20;w.SpawnItem("water_bucket",1,new(57,60));w.SupplyCare("medic","guard","water_bucket");
        w.DraftResident("medic",true);Advance(w,3);Check(w.Total("water_bucket")==1&&w.Workers[0].Person.Thirst<20,"cancel returns water reservation");
        w=CombatChecks.Fixture();w.InjureResident("guard","left_leg","fracture",50);w.Workers[1].Person.WorkPriorities["doctor"]=1;w.SpawnItem("splint",1,new(57,60));Advance(w,30);
        Check(w.Workers[0].Person.Injuries[0].Fixed,"automatic splint uses doctor priority even with care disabled");
        w=CombatChecks.Fixture();w.Workers[0].Person.Thirst=0;w.InjureResident("guard","torso","fracture",90);w.Workers[1].Person.WorkPriorities["care"]=1;w.SpawnItem("water_bucket",1,new(57,60));Advance(w,30);
        Check(w.Workers[0].Person.Thirst>40,"automatic water for incapacitated patient");
        w=CombatChecks.Fixture();var resident=w.Workers[0];resident.Person.ColdExposure=120;resident.Person.HeatExposure=10;resident.Person.Dehydration=50;resident.Person.CriticalSeconds=20;resident.Person.InfectionExposure=90;resident.Person.Isolated=true;
        var saved=DemoWorld.LoadJson(w.SaveJson()).Workers[0].Person;Check(saved.ColdExposure==120&&saved.Dehydration==50&&saved.CriticalSeconds==20&&saved.Isolated&&saved.InfectionExposure==90,"new health state archive");
        resident.Person.DeathCause="严重脱水";saved=DemoWorld.LoadJson(w.SaveJson()).Workers[0].Person;Check(saved.Dead&&saved.DeathCause=="严重脱水","death cannot heal through reload");
        w=CombatChecks.Fixture();food=w.SpawnItem("meal",1,new(60,60),new(){ExpiresAt=10});food.Owner="guard";w=DemoWorld.LoadJson(w.SaveJson());Check(w.Items.Single().Properties.ExpiresAt==10,"expiry timestamp does not refresh on load");Advance(w,13);
        Check(w.Total("meal")==0&&w.Total("rot")==1,"personal food retains expiry across save");
        Console.WriteLine("LIFE_EXPANSION_OK: thirst/heat/cold, fatal grace, pain, splints, prostheses, finite water, spoilage, transmission/isolation, emergency care, alerts, dining and leisure");
    }
}
