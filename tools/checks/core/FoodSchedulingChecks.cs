using NoDwarf.Simulation;
internal static class FoodSchedulingChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception("FOOD_SCHEDULING: "+why);}
    static void Advance(DemoWorld w,double t){for(int i=0;i<t*60;i++)w.Update(1d/60);}
    static DemoWorld Fixture(){var w=CombatChecks.Fixture();foreach(var a in w.Workers){a.Person.Hunger=a.Person.Thirst=a.Person.Rest=90;a.Person.Schedule=ResidentWork.FreeDay();}w.Workers[0].Person.Hunger=20;return w;}
    public static void Run()
    {
        var w=Fixture();var newer=w.SpawnItem("meal",1,new(61,60),new(){ExpiresAt=600});var older=w.SpawnItem("meal",1,new(64,60),new(){ExpiresAt=200});
        Check(w.ActResident(new("guard","eat")).Ok&&w.Workers[0].Person.Activity?.ItemId==older.Id,"public meal chooses nearby earlier expiry");Advance(w,15);
        Check(w.Items.Any(s=>s.Id==newer.Id)&&!w.Items.Any(s=>s.Id==older.Id),"older consumed exactly once");
        w=Fixture();newer=w.SpawnItem("meal",1,new(61,60),new(){ExpiresAt=600});older=w.SpawnItem("meal",1,new(64,60),new(){ExpiresAt=200});w.SetItemForbidden(older.Id,true);
        Check(w.ActResident(new("guard","eat")).Ok&&w.Workers[0].Person.Activity?.ItemId==newer.Id,"ban filtered before expiry");
        w=Fixture();newer=w.SpawnItem("meal",1,new(61,60),new(){ExpiresAt=600});older=w.SpawnItem("meal",1,new(100,60),new(){ExpiresAt=200});
        Check(w.ActResident(new("guard","eat")).Ok&&w.Workers[0].Person.Activity?.ItemId==newer.Id,"distant old food does not override nearby supply");
        w=Fixture();newer=w.SpawnItem("meal",1,new(61,60),new(){ExpiresAt=600});older=w.SpawnItem("meal",1,new(64,60),new(){ExpiresAt=200});
        w.ConfigureSafety(new(){ForbiddenAreas=new[]{new SafetyVolume(new(63,59,-1),new(65,61,2))}});
        Check(w.ActResident(new("guard","eat")).Ok&&w.Workers[0].Person.Activity?.ItemId==newer.Id,"forbidden older source filtered before expiry");
        w=Fixture();older=w.SpawnItem("meal",1,new(64,60),new(){ExpiresAt=200});w.ActResident(new("guard","eat"));older.Properties=older.Properties with{ExpiresAt=1};Advance(w,4);
        Check(w.Total("rot")==1&&w.Workers[0].Person.Hunger<25,"in-flight spoil cancels meal and retains rot");

        w=Fixture();w.Workers[0].Person.Hunger=90;var patient=w.Workers[1];patient.Person.Hunger=20;w.InjureResident(patient.Id,"torso","fracture",90);w.Workers[0].Person.WorkPriorities["care"]=1;
        newer=w.SpawnItem("meal",1,new(61,60),new(){ExpiresAt=600});older=w.SpawnItem("meal",1,new(64,60),new(){ExpiresAt=200});Advance(w,.2);
        Check(w.Workers[0].Person.Activity?.ItemId==older.Id,"feeding uses same expiry preference");Advance(w,15);Check(patient.Person.Hunger>40&&w.Items.Any(s=>s.Id==newer.Id),"older ration reaches patient");

        w=Fixture();w.Workers[0].Person.Hunger=90;w.Workers[0].Person.WorkPriorities["cooking"]=1;
        RoomEnvironmentChecks.Shell(w,new(58,58));var stove=new Cell(61,61);w.Buildings[stove]=new(stove,BuildKind.Stove);
        w.SpawnItem("wood",2,new(60,60));newer=w.SpawnItem("raw_food",2,new(60,60),new(){ExpiresAt=600});older=w.SpawnItem("raw_food",2,new(62,62),new(){ExpiresAt=200});
        Check(w.QueueProductionRecipe("cook")&&w.Jobs.Single().Materials.Any(m=>m.StackId==older.Id),"cooking reserves older reachable batch");
        for(int i=0;i<1200&&!w.Jobs.Any(j=>j.Progress>0);i++)w.Update(1d/60);
        var job=w.Jobs.Single();Check(job.Progress>0,"processing fixture");foreach(var m in job.Materials.Where(m=>m.Item=="raw_food")){var s=w.Items.Single(s=>s.Id==m.StackId);s.Properties=s.Properties with{ExpiresAt=w.SimulationTime+.1};}
        Advance(w,3);Check(w.Total("meal")==0&&w.Total("rot")==2&&w.Total("raw_food")==2,"processing expiry cancels without premature output or double consumption");
        Advance(w,30);Check(w.Total("meal")==2&&w.Total("raw_food")==0,"fresh batch restarts and completes once");
        w=Fixture();var first=w.SpawnItem("meal",2,new(61,60),new(){ExpiresAt=100});var second=w.SpawnItem("meal",2,new(61,60),new(){ExpiresAt=200});
        Check(first.Id!=second.Id,"different ages never merge");var merged=w.SpawnItem("meal",1,first.Cell,first.Properties);Check(merged.Id==first.Id&&merged.Count==3,"equal properties merge without refreshing expiry");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Items.Single(s=>s.Id==first.Id).Properties.ExpiresAt==100&&w.Items.Single(s=>s.Id==second.Id).Properties.ExpiresAt==200,"save preserves batch ages");
        Console.WriteLine("FOOD_SCHEDULING_OK: near expiry, distance bound, ban and forbidden paths, feeding, cooking, spoil in transit/process and batch age persistence");
    }
}
