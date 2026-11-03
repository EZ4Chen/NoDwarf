namespace NoDwarf.Simulation;
public static class CombatRevisionChecks
{
    private static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("COMBAT_REVISION: "+why);}
    private static void Advance(DemoWorld w,int ticks){for(int i=0;i<ticks;i++)w.Update(1d/30);}
    private static DemoWorker Enemy(DemoWorld w,int x=61){var e=new DemoWorker{Id="revision-enemy",Name="敌人",X=x,Z=60,Combat=new(){Drafted=true}};w.Enemies.Add(e);return e;}
    public static DemoWorld Storeys()
    {
        var w=CombatChecks.Fixture();
        for(int x=62;x<=66;x++)for(int z=58;z<=62;z++)for(int y=0;y<3;y++){var c=new Cell(x,z,y);w.Buildings[c]=new(c,BuildKind.StoneBricks);}
        for(int i=0;i<3;i++)
        {
            for(int y=0;y<i;y++){var support=new Cell(59+i,60,y);w.Buildings[support]=new(support,BuildKind.StoneBricks);}
            var stair=new Cell(59+i,60,i);w.Buildings[stair]=new(stair,BuildKind.PlankStairs,0);
            w.Workers[i].X=57;w.Workers[i].Z=59+i;w.DraftResident(w.Workers[i].Id,true);
        }
        return w;
    }
    public static void Run()
    {
        var w=CombatChecks.Fixture();var guard=w.Workers[0];Enemy(w);w.DraftResident(guard.Id,true);
        Check(w.MilitaryMove(guard.Id,new(55,60)).Ok,"retreat order");Advance(w,30);
        Check(guard.X<59&&guard.Combat.Strikes==0,"explicit retreat overrides automatic attack");
        w=CombatChecks.Fixture();guard=w.Workers[0];guard.Combat.Cooldown=1.2;Advance(w,60);Check(guard.Combat.Cooldown==0,"civilian cooldown advances");
        w.DraftResident(guard.Id,true);Check(guard.Combat.Cooldown==0,"redraft does not freeze/restart cooldown");
        w=CombatChecks.Fixture();var e=Enemy(w,70);e.Person.Blood=19.99;w.Update(.01);Check(w.BattleOutcome=="victory","temporarily incapacitated victory");
        w=DemoWorld.LoadJson(w.SaveJson());Advance(w,30);Check(w.BattleOutcome==""&&!w.Enemies[0].Person.Incapacitated,"recovered threat reopens battle after load");
        w=CombatChecks.Fixture();guard=w.Workers[0];
        for(int i=0;i<40;i++)guard.Person.Injuries.Add(new(){Part="left_foot",Kind="fracture",Severity=.1});
        Check(w.InjureResident(guard.Id,"torso","cut",30).Ok&&guard.Person.Injuries.Count<=40&&guard.Person.Injuries.Any(i=>i.Part=="torso"&&i.Severity==30),"40-record old save cannot block damage");
        guard.Person.Injuries.First(i=>i.Part=="torso").Treated=true;
        w.InjureResident(guard.Id,"torso","cut",10);Check(guard.Person.Injuries.Single(i=>i.Part=="torso").Severity==40&&!guard.Person.Injuries.Single(i=>i.Part=="torso").Treated,"new damage reopens accumulated wound");
        w=DemoWorld.LoadJson(w.SaveJson());Check(w.Workers[0].Person.Injuries.Count==2,"compacted damage archive");
        w=CombatChecks.Fixture();guard=w.Workers[0];var medic=w.Workers[1];guard.Person.Injuries.Add(new(){Part="left_arm",Kind="cut",Severity=10});
        medic.Person.WorkPriorities["doctor"]=1;medic.Person.WorkPriorities["construction"]=2;w.SpawnItem("cobblestone",10,new(57,60));w.QueueBuild(new(58,63),BuildKind.Wall);Advance(w,600);
        Check(w.Buildings.ContainsKey(new(58,63))&&medic.MedicalIssue.Contains("缺料"),"failed care yields to construction while retaining reason");
        w.SpawnItem("bandage",1,new(57,60));Advance(w,600);Check(guard.Person.Injuries.All(i=>i.Treated)&&medic.MedicalIssue=="","care recovers when supplied");
        w=CombatChecks.Fixture();Enemy(w,100);foreach(var a in w.Workers)a.Person.Blood=19.99;
        w.Update(.01);Check(w.BattleOutcome=="defeat","temporary defeat");Advance(w,90);Check(w.BattleOutcome=="","recovered residents reopen battle");
        w=CombatChecks.Fixture();w.DraftResident("guard",true);w.MilitaryMove("guard",new(62,60));
        w.Workers[1].X=62;w.Workers[1].Z=60;Advance(w,300);
        Check(w.Workers[0].Tile!=w.Workers[1].Tile&&w.Workers[0].Combat.Destination is null,"newly occupied goal selects nearby free endpoint");
        MultiLevel();
        Console.WriteLine("COMBAT_REVISION_OK: retreat, civilian cooldown, revived threat/save, bounded wounds, medical fallback, shared stairs/ladders, endpoint separation and broken-route recovery");
    }
    private static void MultiLevel()
    {
        var w=Storeys();
        foreach(var actor in w.Workers)Check(w.MilitaryMove(actor.Id,new(64,60,3)).Ok,"three upstairs orders");
        var goals=w.Workers.Select(a=>a.Combat.Destination!.Value).ToArray();Check(goals.Distinct().Count()==3&&goals.All(c=>c.Level==3),"separate goals on requested storey");
        Advance(w,60);w=DemoWorld.LoadJson(w.SaveJson());Advance(w,1000);
        Check(w.Workers.Select(a=>a.Tile).SequenceEqual(goals),"three residents climb after mid-route load");
        foreach(var actor in w.Workers)Check(w.MilitaryMove(actor.Id,new(57,60)).Ok,"three downstairs orders");Advance(w,1000);Check(w.Workers.All(a=>a.Level==0&&a.Combat.Destination is null),"three descend");
        // Opposing traffic uses soft passage, not exclusive reservations over the whole stair.
        w=Storeys();w.Workers[2].X=64;w.Workers[2].Z=60;w.Workers[2].Level=3;
        w.MilitaryMove(w.Workers[0].Id,new(64,60,3));w.MilitaryMove(w.Workers[2].Id,new(57,60));Advance(w,1000);
        Check(w.Workers[0].Level==3&&w.Workers[2].Level==0,"opposing stair traffic does not deadlock");
        w=Storeys();
        for(int z=58;z<=60;z++)for(int y=0;y<2;y++)w.Buildings.Remove(new(64,z,y));
        Check(w.MilitaryMove(w.Workers[0].Id,new(64,60,3)).Ok&&w.MilitaryMove(w.Workers[1].Id,new(64,60,0)).Ok,"same XZ on different floors can both be ordered");
        Check(w.Workers[0].Combat.Destination==new Cell(64,60,3)&&w.Workers[1].Combat.Destination==new Cell(64,60,0),"endpoint reservations include height");
        Advance(w,1200);Check(w.Workers[0].Tile==new Cell(64,60,3)&&w.Workers[1].Tile==new Cell(64,60,0),"simultaneous above/below arrivals remain on intended floor");
        w=LadderChecks.Fixture();
        for(int x=61;x<=63;x++)for(int z=59;z<=61;z++)for(int y=0;y<4;y++){var c=new Cell(x,z,y);w.Buildings[c]=new(c,BuildKind.StoneBricks);}
        for(int y=0;y<4;y++){var c=new Cell(60,60,y);w.Buildings[c]=new(c,BuildKind.Ladder,0);}
        for(int i=0;i<3;i++){var a=new DemoWorker{Id="climber"+i,Name="攀爬者"+i,X=58,Z=59+i};w.Workers.Add(a);w.DraftResident(a.Id,true);Check(w.MilitaryMove(a.Id,new(62,60,4)).Ok,"ladder orders");}
        var missing=new Cell(60,60,2);w.Buildings.Remove(missing);Advance(w,90);
        Check(w.Workers.All(a=>a.Level<4&&a.Person.Injuries.Count==0),"broken ladder does not teleport across gap");
        w=DemoWorld.LoadJson(w.SaveJson());w.Buildings[missing]=new(missing,BuildKind.Ladder,0);Advance(w,1500);
        Check(w.Workers.All(a=>a.Level==4&&a.Combat.Destination is null)&&w.Workers.Select(a=>a.Tile).Distinct().Count()==3,"repaired ladder retries to separate landings");
    }
}
