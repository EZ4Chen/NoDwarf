namespace NoDwarf.Simulation;
public static class MeleeChecks
{
    private static void Check(bool value,string reason){if(!value)throw new InvalidOperationException("MELEE: "+reason);}
    private static (DemoWorld World,DemoWorker A,DemoWorker B) Fixture(string weapon="sword")
    {
        var world=CombatChecks.Fixture();var a=world.Workers[0];var b=new DemoWorker{Id="melee-target",Name="目标",X=60,Z=61};world.Enemies.Add(b);
        var item=world.SpawnItem(weapon,1,a.Tile);item.Owner=a.Id;item.Slot="main";
        a.Combat.Drafted=true;b.Combat.Drafted=true;world.SetCombatDefense(a.Id,"none");world.SetCombatDefense(b.Id,"none");
        return(world,a,b);
    }
    private static void Step(DemoWorld world,double seconds,double dt=1d/30)
    {int count=(int)Math.Ceiling(seconds/dt);for(int i=0;i<count;i++)world.AdvanceMelee(Math.Min(dt,seconds-i*dt));}
    private static double Damage(DemoWorker w)=>w.Person.Injuries.Sum(i=>i.Severity);
    public static void Run()
    {
        var (w,a,b)=Fixture();Check(w.BeginMelee(a,b),"start");Step(w,.3);Check(Damage(b)==0,"windup cannot hit");
        var loaded=DemoWorld.LoadJson(w.SaveJson());Step(w,1);Step(loaded,1);Check(Damage(b)>0,"swept blade contacts body");
        Check(Damage(b)==Damage(loaded.Enemies[0]),"mid windup save deterministic");double damage=Damage(b);Step(w,2);Check(Damage(b)==damage,"one contact per attack");
        (w,a,b)=Fixture();w.BeginMelee(a,b);Step(w,.3);b.X+=3;Step(w,1);Check(Damage(b)==0&&a.Combat.LastContact=="miss","moving away causes actual miss");
        (w,a,b)=Fixture();w.BeginMelee(a,b);Step(w,.3);var wall=new Cell(60,61);w.Buildings[wall]=new(wall,BuildKind.Wall);w.Buildings[wall+new Cell(0,0,1)]=new(wall+new Cell(0,0,1),BuildKind.Wall);Step(w,1);Check(Damage(b)==0&&a.Combat.LastContact=="wall","wall inserted during windup blocks");
        (w,a,b)=Fixture();var shield=w.SpawnItem("shield",1,b.Tile);shield.Owner=b.Id;shield.Slot="off";
        b.Combat.FacingZ=-1;b.Combat.Defense="block";b.Combat.DefenseTime=.2;
        w.BeginMelee(a,b);Step(w,.8);Check(Damage(b)==0&&a.Combat.LastContact=="block"&&b.Combat.Stamina<100,"front shield contact consumes stamina");
        (w,a,b)=Fixture();shield=w.SpawnItem("shield",1,b.Tile);shield.Owner=b.Id;shield.Slot="off";
        b.Combat.Defense="block";b.Combat.DefenseTime=.2;w.BeginMelee(a,b);Step(w,1);Check(Damage(b)>0,"back attack bypasses shield");
        (w,a,b)=Fixture();shield=w.SpawnItem("shield",1,b.Tile);shield.Owner=b.Id;shield.Slot="off";
        b.Combat.FacingZ=-1;b.Combat.Defense="block";b.Combat.DefenseTime=.2;b.Combat.Stamina=1;w.BeginMelee(a,b);Step(w,.65);
        Check(b.Combat.LastContact=="guard_break"&&b.Combat.DefenseRecovery>0,"guard break");
        (w,a,b)=Fixture();var sword=w.SpawnItem("sword",1,b.Tile);sword.Owner=b.Id;sword.Slot="main";
        b.Combat.FacingZ=-1;w.BeginMelee(a,b);Step(w,.35);b.Combat.Defense="parry";b.Combat.DefenseTime=0;Step(w,.4);
        Check(Damage(b)==0&&a.Combat.LastContact=="parry","timed weapon parry");
        (w,a,b)=Fixture();sword=w.SpawnItem("sword",1,b.Tile);sword.Owner=b.Id;sword.Slot="main";
        b.Combat.FacingZ=-1;b.Combat.Defense="parry";b.Combat.DefenseTime=1;w.BeginMelee(a,b);Step(w,.8);Check(Damage(b)>0,"expired parry cannot defend forever");
        (w,a,b)=Fixture();w.BeginMelee(a,b);w.MilitaryMove(a.Id,new(56,60));Step(w,1);Check(Damage(b)==0&&a.Combat.AttackWeapon=="","move cancels attack with recovery");
        (w,a,b)=Fixture();w.BeginMelee(a,b);w.Inventory(a).First(i=>i.Slot=="main").Slot="";Step(w,1);Check(Damage(b)==0,"unequip cancels active attack");
        (w,a,b)=Fixture();var fine=DemoWorld.LoadJson(w.SaveJson());w.BeginMelee(a,b);fine.BeginMelee(fine.Workers[0],fine.Enemies[0]);Step(w,1.25,.1);Step(fine,1.25,1d/60);
        Check(Damage(b)==Damage(fine.Enemies[0]),"fixed trajectory substeps survive coarse updates");
        loaded=DemoWorld.LoadJson(w.SaveJson());Step(loaded,1);Check(Damage(loaded.Enemies[0])==Damage(b),"resolved contact stays resolved across load");
        (w,a,b)=Fixture();a.Combat.Stamina=0;Check(!w.BeginMelee(a,b),"exhaustion gates attacks");Step(w,1);Check(a.Combat.Stamina>0,"idle recovers stamina");
        (w,a,b)=Fixture();w.BeginMelee(a,b);var ally=w.Workers[1];ally.X=60;ally.Z=60.7;Step(w,1);
        Check(a.Combat.LastContact=="ally"&&Damage(b)==0&&Damage(ally)==0,"friendly body intercepts without friendly fire");
        (w,a,b)=Fixture();b.Combat.Defense="parry";b.Combat.DefenseTime=.1;b.Combat.FacingZ=-1;w.BeginMelee(a,b);Step(w,1);
        Check(Damage(b)>0,"bare hands cannot weapon-parry");
        (w,a,b)=Fixture();shield=w.SpawnItem("shield",1,a.Tile);shield.Owner=a.Id;shield.Slot="off";
        w.InjureResident(a.Id,"torso","cut",20);Check(Damage(a)==20,"shield no longer grants passive all-body armour");
        var invalid=System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!;invalid["Workers"]![0]!["Combat"]!["Stamina"]=-1;
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid combat archive rejected");
        foreach(var weapon in MeleeProfiles.Weapons.Values)
        {var blade=MeleeProfiles.Blade(weapon,weapon.Windup+weapon.Active*.5);Check(float.IsFinite(blade.B.X)&&weapon.Duration>0&&weapon.Reach>0,"generic profile "+weapon.Id);}
        (w,a,b)=Fixture();sword=w.SpawnItem("sword",1,b.Tile);sword.Owner=b.Id;sword.Slot="main";
        w.SetCombatDefense(a.Id,"parry");bool automaticParry=false;
        for(int i=0;i<60;i++){w.Update(1d/30);automaticParry|=a.Combat.LastContact=="parry";}
        Check(automaticParry,"AI schedules parry window against sword windup");
        var stress=CombatChecks.Fixture();stress.Workers.Clear();stress.Items.Clear();
        for(int i=0;i<12;i++)
        {
            var attacker=new DemoWorker{Id="sweep-"+i,Name="攻击者",X=50+i%4*5,Z=50+i/4*5};
            var defender=new DemoWorker{Id="target-"+i,Name="目标",X=attacker.X,Z=attacker.Z+1};
            stress.Workers.Add(attacker);stress.Enemies.Add(defender);
            var item=stress.SpawnItem("sword",1,attacker.Tile);item.Owner=attacker.Id;item.Slot="main";
            Check(stress.BeginMelee(attacker,defender),"stress attack setup");
        }
        double maxMs=0;var total=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<30;i++){var tick=System.Diagnostics.Stopwatch.StartNew();stress.AdvanceMelee(1d/30);maxMs=Math.Max(maxMs,tick.Elapsed.TotalMilliseconds);}
        Check(stress.Enemies.All(e=>Damage(e)>0),"24 actors: independent contact resolution");
        Console.WriteLine($"MELEE_PERF: 24 actors / 12 concurrent strikes, 30 ticks {total.Elapsed.TotalMilliseconds:F1} ms total, max {maxMs:F2} ms (collision subsystem only)");
        Console.WriteLine("MELEE_OK: windup, sweep, dodge, walls, shield front/back/break, timed parry, cancel, stamina, save and tick sizes");
    }
}
