using NoDwarf.Simulation;
internal static class LifeFacilitiesChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception("LIFE_FACILITIES: "+why);}
    static void Advance(DemoWorld w,double t){for(int i=0;i<t*60;i++)w.Update(1d/60);}
    public static void Run()
    {
        var w=CombatChecks.Fixture();var worker=w.Workers[0];worker.Person.Rest=5;worker.Person.Hunger=worker.Person.Thirst=90;
        var bed=new Cell(61,60);w.Buildings[bed]=new(bed,BuildKind.Bed);Check(w.ActResident(new(worker.Id,"rest")).Ok,"outdoor bed");Advance(w,2);
        Check(worker.Person.MoodEffects.Single(e=>e.Key=="bedroom").Amount==1,"outdoor bed mood");
        RoomEnvironmentChecks.Shell(w,new(58,58));Advance(w,.2);
        Check(worker.Person.MoodEffects.Single(e=>e.Key=="bedroom") is {Amount:>=2} effect&&effect.Text.Contains("卧室"),"roof improves cause and value immediately");
        w.Buildings.Remove(bed);Advance(w,.2);
        Check(worker.Person.HomeBed is null&&worker.Person.MoodEffects.Single(e=>e.Key=="bedroom") is {Amount:-1,Text:"地板休息"},"removed bed loses comfort but floor rest continues");
        w=DemoWorld.LoadJson(w.SaveJson());worker=w.Workers[0];bed=new(61,61);w.Buildings[bed]=new(bed,BuildKind.Bed);w.ActResident(new(worker.Id,"rest"));Advance(w,3);
        Check(worker.Person.HomeBed==bed&&worker.Person.MoodEffects.Count(e=>e.Key=="bedroom")==1&&worker.Person.MoodEffects.Single(e=>e.Key=="bedroom").Amount<=6,"new bed updates one bounded mood effect after save");

        w=CombatChecks.Fixture();worker=w.Workers[0];worker.Person.Hunger=20;worker.Person.Thirst=90;
        var table=new Cell(63,60);var chair=new Cell(62,60);w.Buildings[table]=new(table,BuildKind.Table);w.Buildings[chair]=new(chair,BuildKind.Chair);
        var pantry=new Cell(60,61);w.SetStockpile(pantry,pantry);var meal=w.SpawnItem("meal",2,pantry);w.SpawnItem("rot",98,pantry);
        w.ConfigureSupplyReserve("meal",2,new[]{"food"});w.SetItemForbidden(meal.Id,true);
        Check(!w.ActResident(new(worker.Id,"eat")).Ok,"full pantry still obeys usage ban");w.SetItemForbidden(meal.Id,false);
        w.ActResident(new(worker.Id,"eat"));Advance(w,12);
        Check(worker.Person.MoodEffects.Any(e=>e.Key=="meal"&&e.Text.Contains("餐桌"))&&w.Total("meal")==1&&w.FreeStorageCount(pantry,"meal")>0,"meal frees full storage and gains dining comfort");
        w.Buildings.Remove(table);worker.Person.Hunger=20;w.ActResident(new(worker.Id,"eat"));Advance(w,12);
        Check(worker.Person.MoodEffects.Count(e=>e.Key=="meal")==1&&!worker.Person.MoodEffects.Single(e=>e.Key=="meal").Text.Contains("餐桌"),"removed table updates next meal without stacking bonuses");

        Console.WriteLine("LIFE_FACILITIES_OK: outdoor/indoor/floor/new bed, bounded mood, full pantry and reserve/ban, dining removal");
    }
}
