namespace NoDwarf.Simulation;
public static class CombatChecks
{
    private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("COMBAT: "+why);}
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)(seconds*30);i++)w.Update(1d/30);}
    public static DemoWorld Fixture()
    {
        var w=new DemoWorld(flat:true);w.Workers.Clear();w.Items.Clear();w.Stockpiles.Clear();w.Jobs.Clear();
        w.Workers.Add(new(){Id="guard",Name="守卫",X=60,Z=60});
        w.Workers.Add(new(){Id="medic",Name="医生",X=57,Z=60});
        w.Workers.Add(new(){Id="civilian",Name="工匠",X=57,Z=62});
        foreach(var resident in w.Workers)foreach(var type in ResidentWork.Types)resident.Person.WorkPriorities[type.Id]=0;
        return w;
    }
    private static DemoWorker Enemy(DemoWorld w,Cell c)
    {var e=new DemoWorker{Id="test-enemy",Name="测试敌人",X=c.X,Z=c.Z,Level=c.Level,Combat=new(){Drafted=true}};w.Enemies.Add(e);return e;}
    public static void Run()
    {
        var w=Fixture();var guard=w.Workers[0];var enemy=Enemy(w,new(61,60));
        var sword=w.SpawnItem("sword",1,guard.Tile);sword.Owner=guard.Id;sword.Slot="main";
        Check(w.DraftResident(guard.Id,true).Ok,"draft");Check(w.MilitaryAttack(guard.Id,enemy.Id).Ok,"attack order");
        Advance(w,1.2);Check(enemy.Person.Injuries.Count>0,"sword uses existing injuries");
        var damage=enemy.Person.Injuries.Sum(i=>i.Severity);w.Update(0);Check(enemy.Person.Injuries.Sum(i=>i.Severity)==damage,"zero tick does not attack");
        var loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(loaded.Enemies.Count==1&&loaded.Workers[0].Combat.Target==enemy.Id&&loaded.Workers[0].Combat.Cooldown==guard.Combat.Cooldown,"combat archive");
        Advance(w,12);Advance(loaded,12);
        Check(loaded.Enemies[0].Person.Injuries.Sum(i=>i.Severity)==enemy.Person.Injuries.Sum(i=>i.Severity),"reload preserves strike sequence and cooldown");
        Check(enemy.Person.Incapacitated,"enemy incapacitated");int strikes=guard.Combat.Strikes;Advance(w,3);Check(guard.Combat.Strikes==strikes,"stop hitting downed enemy");
        w=Fixture();guard=w.Workers[0];enemy=Enemy(w,new(62,60));
        var wall=new Cell(61,60);w.Buildings[wall]=new(wall,BuildKind.Wall);w.Buildings[wall+new Cell(0,0,1)]=new(wall+new Cell(0,0,1),BuildKind.Wall);
        Check(!w.CanMelee(guard,enemy),"wall rejects hit");enemy.X=60;enemy.Level=2;Check(!w.CanMelee(guard,enemy),"stacked floor rejects hit");enemy.X=61;enemy.Level=0;Check(!w.CanMelee(guard,enemy),"solid target cell rejects hit");
        w=Fixture();guard=w.Workers[0];enemy=Enemy(w,new(61,60,1));var step=new Cell(60,60);w.Buildings[step]=new(step,BuildKind.PlankStairs);var landing=new Cell(61,60);w.Buildings[landing]=new(landing,BuildKind.Wall);
        Check(w.CanMelee(guard,enemy),"legal step permits melee");
        // Actual materials in transit must be returned exactly once on draft.
        w=Fixture();guard=w.Workers[0];guard.Person.WorkPriorities["construction"]=1;
        w.SpawnItem("cobblestone",10,new(60,60));Check(w.QueueBuild(new(68,60),BuildKind.Wall),"queue construction");
        for(int i=0;i<300&&!w.Jobs.Any(j=>j.Materials.Any(r=>r.Stage==1));i++)w.Update(1d/30);
        Check(w.Jobs.Any(j=>j.Materials.Any(r=>r.Stage==1)),"cargo in transit fixture");int total=w.Total("cobblestone");
        w.DraftResident(guard.Id,true);Check(w.Total("cobblestone")==total&&guard.Job is null&&!w.Jobs.Any(j=>j.Worker==0),"draft returns cargo without duplication");
        w.DraftResident(guard.Id,false);Advance(w,25);Check(w.Buildings.ContainsKey(new(68,60)),"undraft resumes construction");
        w=Fixture();Check(!w.SetCivilianAlert(true).Ok,"rally required");w.SetRallyPoint(new(55,60));w.SetCivilianAlert(true);Advance(w,8);
        Check(w.Workers.All(r=>r.Tile==r.Combat.Destination&&r.Level==w.RallyPoint!.Value.Level&&r.Job is null)&&w.Workers.Select(r=>r.Tile).Distinct().Count()==w.Workers.Count,"civilians gather");loaded=DemoWorld.LoadJson(w.SaveJson());Check(loaded.CivilianAlert&&loaded.RallyPoint==w.RallyPoint,"alarm archive");
        Check(!w.ActResident(new("civilian","research")).Ok,"alert blocks normal activities");w.SetCivilianAlert(false);
        ClockAndBlockedChecks();
        MeleeChecks.Run();
        CombatRevisionChecks.Run();
        MedicalChecks();
        ProductionToBattle();
        Console.WriteLine("COMBAT_OK: melee, occlusion, stairs, cooldown archive, cargo, alert, medical supply");
    }
    private static void ClockAndBlockedChecks()
    {
        var w=Fixture();var e=Enemy(w,new(61,60));w.DraftResident("guard",true);
        var clock=new SimulationClock{Paused=true};for(int i=0;i<clock.Advance(2);i++)w.Update(1d/30);
        Check(w.SimulationTime==0&&e.Person.Injuries.Count==0,"paused clock does not hit");
        clock.Paused=false;clock.SetSpeed(3);int ticks=clock.Advance(1);for(int i=0;i<ticks;i++)w.Update(1d/30);
        Check(ticks==90&&Math.Abs(w.SimulationTime-3)<.00001&&e.Person.Injuries.Count>0,"3x simulation advances combat exactly 90 ticks");
        w=Fixture();foreach(var r in w.Workers){r.X=60;r.Z=60;w.DraftResident(r.Id,true);}
        for(int x=58;x<=62;x++)for(int z=58;z<=62;z++)if(x is 58 or 62||z is 58 or 62)
            for(int y=0;y<3;y++){var c=new Cell(x,z,y);w.Buildings[c]=new(c,BuildKind.Wall);}
        e=Enemy(w,new(65,60));long searches=w.Navigation.Searches;Advance(w,5);
        Check(w.Workers.All(r=>r.Person.Injuries.Count==0)&&e.Status.Contains("不可达"),"sealed room blocks pursuit and attacks");
        Check(w.Navigation.Searches-searches<=6,"unreachable enemy retries at most once per second");
    }
    private static void ProductionToBattle()
    {
        var w=new DemoWorld(flat:true);w.QueueBlueprint(new(5,4),DemoWorld.ExampleBlueprint());Advance(w,240);
        Check(w.WorkshopReady&&w.QueueSword(),"production workshop and sword order");Advance(w,60);
        var sword=w.Items.FirstOrDefault(s=>s.Item=="sword"&&s.Owner=="");Check(sword is not null,"sword manufactured");
        var guard=w.Workers[0];Check(w.ActResident(new(guard.Id,"pickup",ItemId:sword!.Id)).Ok,"take manufactured sword");Advance(w,20);
        var carried=w.Inventory(guard).FirstOrDefault(i=>i.Item=="sword");Check(carried is not null&&w.ActResident(new(guard.Id,"equip",ItemId:carried.Id)).Ok,"equip manufactured sword");
        Check(w.StartTestRaid(3).Ok&&w.Enemies.Count==3&&w.Enemies.All(e=>e.Tile.X is 0 or 254||e.Tile.Z is 0 or 254)&&w.Enemies.Select(e=>e.Tile).Distinct().Count()==3,"three distinct real edge spawns");
        Check(w.Workers.Count==3&&!w.StartTestRaid().Ok,"enemies separated and duplicate raid rejected");
        w.SetRallyPoint(new(7,7));w.SetCivilianAlert(true);w.DraftResident(guard.Id,true);
        w.MilitaryAttack(guard.Id,w.Enemies[0].Id);Advance(w,5);w=DemoWorld.LoadJson(w.SaveJson());Advance(w,90);
        Check(w.Enemies.All(e=>e.Job is null)&&w.Workers.Where(r=>!r.Combat.Drafted).All(r=>r.Job is null),"raid never gives enemies work or restarts civilian work");
        w.SetCivilianAlert(false);w.DraftResident(guard.Id,false);
    }
    private static void MedicalChecks()
    {
        var w=Fixture();var medic=w.Workers[1];var patient=w.Workers[0];patient.Person.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=35});
        Check(!w.ActResident(new(medic.Id,"treat",patient.Id)).Ok,"missing supplies feedback");
        w.SpawnItem("bandage",2,new(55,60));
        Check(w.ActResident(new(medic.Id,"treat",patient.Id)).Ok&&medic.Person.Activity?.Kind=="care_supply","public supply reservation");
        Check(w.Available("bandage")==1,"one supply reserved");Check(!w.ActResident(new("civilian","treat",patient.Id)).Ok,"duplicate caregiver rejected");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Available("bandage")==1,"reservation survives load");
        Advance(w,4);w=DemoWorld.LoadJson(w.SaveJson());Advance(w,14);Check(w.Workers[0].Person.Injuries[0].Treated&&w.Total("bandage")==1,"walk take and treat consumes exactly one");
        w=Fixture();patient=w.Workers[0];patient.Person.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=35});w.SpawnItem("bandage",1,new(55,60));
        w.ActResident(new("medic","treat",patient.Id));w.DraftResident("medic",true);Check(w.Available("bandage")==1,"draft releases medical reservation");
        w.DraftResident("medic",false);w.ActResident(new("medic","treat",patient.Id));patient.Person.Blood=0;Advance(w,.1);Check(w.Available("bandage")==1,"death releases medical reservation");
        w=Fixture();patient=w.Workers[0];patient.Person.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=35});w.SpawnItem("bandage",1,new(55,60));
        for(int x=54;x<=56;x++)for(int z=59;z<=61;z++)if(x!=55||z!=60)for(int y=0;y<2;y++){var c=new Cell(x,z,y);w.Buildings[c]=new(c,BuildKind.Wall);}
        Check(!w.ActResident(new("medic","treat",patient.Id)).Ok&&w.Available("bandage")==1,"inaccessible supplies do not reserve or heal");
        w=Fixture();patient=w.Workers[0];patient.Person.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=35});w.SpawnItem("bandage",1,new(55,60));
        w.Workers[1].Person.WorkPriorities["doctor"]=1;Advance(w,20);Check(patient.Person.Injuries[0].Treated&&w.Total("bandage")==0,"automatic doctor supplies and treats");
    }
}
