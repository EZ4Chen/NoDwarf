using System.Numerics;
namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private static void ValidateMeleeState(CombatOrder s)
    {
        if(s.AttackWeapon is null||s.AttackWeapon!=""&&!MeleeProfiles.Weapons.ContainsKey(s.AttackWeapon)||
            !double.IsFinite(s.AttackTime)||s.AttackTime<0||s.AttackTime>3||
            !double.IsFinite(s.Stamina)||s.Stamina<0||s.Stamina>100||
            !double.IsFinite(s.AimY)||Math.Abs(s.AimY)>1||
            !double.IsFinite(s.AimX)||!double.IsFinite(s.AimZ)||Math.Abs(s.AimX*s.AimX+s.AimZ*s.AimZ-1)>.01||
            !double.IsFinite(s.FacingX)||!double.IsFinite(s.FacingZ)||Math.Abs(s.FacingX*s.FacingX+s.FacingZ*s.FacingZ-1)>.01||
            !double.IsFinite(s.DefenseTime)||s.DefenseTime<0||!double.IsFinite(s.DefenseRecovery)||s.DefenseRecovery<0||s.DefenseRecovery>3||
            !double.IsFinite(s.ReactionUntil)||s.Defense is not("" or "block" or "parry")||
            s.DefensePreference is not("auto" or "block" or "parry" or "none")||s.LastContact is null||s.LastContact.Length>32)
            throw new InvalidDataException("近战阶段或防御状态无效。");
    }
    public MeleeWeapon EquippedWeapon(DemoWorker actor)=>MeleeProfiles.Weapon(Inventory(actor).FirstOrDefault(i=>i.Slot=="main")?.Item??"unarmed");
    public MeleeShield? EquippedShield(DemoWorker actor)=>MeleeProfiles.Shields.GetValueOrDefault(Inventory(actor).FirstOrDefault(i=>i.Slot=="off")?.Item??"");
    private Vector3 Feet(DemoWorker w)=>new((float)w.X,WorkerHeight(w),(float)w.Z);
    private static Vector3 Direction(double x,double z)=>Vector3.Normalize(new Vector3((float)x,0,(float)z));
    private static Vector3 Local(Vector3 p,Vector3 origin,Vector3 forward)=>origin+new Vector3(forward.Z,0,-forward.X)*p.X+Vector3.UnitY*p.Y+forward*p.Z;
    private static Vector3 Inverse(Vector3 p,Vector3 origin,Vector3 forward)
    {var d=p-origin;return new(Vector3.Dot(d,new(forward.Z,0,-forward.X)),d.Y,Vector3.Dot(d,forward));}
    private void FaceOpponent(DemoWorker a,DemoWorker b)
    {
        var delta=new Vector3((float)(b.X-a.X),0,(float)(b.Z-a.Z));if(delta.LengthSquared()<.001)return;
        delta=Vector3.Normalize(delta);a.Combat.FacingX=delta.X;a.Combat.FacingZ=delta.Z;
    }
    private bool MeleeInReach(DemoWorker a,DemoWorker b)
    {
        float range=EquippedWeapon(a).Reach+.25f;
        return Vector2.Distance(new((float)a.X,(float)a.Z),new((float)b.X,(float)b.Z))<=range&&
            Math.Abs(WorkerHeight(a)-WorkerHeight(b))<=1.05&&
            TerrainContact(Feet(a)+Vector3.UnitY*.85f,Feet(b)+Vector3.UnitY*.85f,.015f) is null;
    }
    private IEnumerable<Cell> MeleeApproachCells(DemoWorker actor,DemoWorker target)
    {
        int range=(int)Math.Ceiling(EquippedWeapon(actor).Reach);
        for(int x=-range;x<=range;x++)for(int z=-range;z<=range;z++)for(int y=-1;y<=1;y++)
        {
            if(x==0&&z==0||Math.Sqrt(x*x+z*z)>EquippedWeapon(actor).Reach+.25)continue;
            var c=target.Tile+new Cell(x,z,y);if(Inside(c)&&Walkable(c)&&TerrainContact(new(c.X,c.Level+.85f,c.Z),Feet(target)+Vector3.UnitY*.85f,.015f) is null)yield return c;
        }
    }
    public ResidentResult SetCombatDefense(string id,string mode)
    {
        var w=Actors.FirstOrDefault(a=>a.Id==id);if(w is null||mode is not("auto" or "block" or "parry" or "none"))return new(false,"防御方式无效");
        w.Combat.DefensePreference=mode;w.Combat.Defense="";w.Combat.DefenseTime=0;Revision++;return new(true,"防御方式："+mode);
    }
    private void CancelMelee(DemoWorker w)
    {
        if(w.Combat.AttackWeapon!="")w.Combat.Cooldown=Math.Max(w.Combat.Cooldown,.25);
        w.Combat.AttackWeapon="";w.Combat.Defense="";w.Combat.DefenseTime=0;
    }
    private bool PrepareDefense(DemoWorker actor,DemoWorker target)
    {
        var s=actor.Combat;
        if(s.DefenseRecovery>0||s.Stamina<5||s.DefensePreference=="none")return false;
        bool threat=target.Combat.AttackWeapon!=""&&!target.Combat.ContactResolved&&
            target.Combat.AttackTime<MeleeProfiles.Weapon(target.Combat.AttackWeapon).Windup+MeleeProfiles.Weapon(target.Combat.AttackWeapon).Active;
        if(!threat){s.Defense="";s.DefenseTime=0;return false;}
        string defense=s.DefensePreference=="parry"?"parry":EquippedShield(actor) is not null?"block":s.DefensePreference=="block"?"":"parry";
        if(defense=="parry"&&!EquippedWeapon(actor).CanParry)defense="";
        if(defense=="")return false;
        if(defense=="parry"&&s.Defense=="")
        {
            var incoming=MeleeProfiles.Weapon(target.Combat.AttackWeapon);var own=EquippedWeapon(actor);
            double prepareAt=Math.Max(0,incoming.Windup+incoming.Active*.5-own.ParryReady-own.ParryWindow*.5);
            if(target.Combat.AttackTime<prepareAt){actor.Status="观察来袭 · 准备招架";return true;}
        }
        if(s.Defense!=defense){s.Defense=defense;s.DefenseTime=0;}
        actor.Status=defense=="block"?"举盾防御":"武器招架";return true;
    }
    public bool BeginMelee(DemoWorker attacker,DemoWorker target)
    {
        var s=attacker.Combat;var weapon=EquippedWeapon(attacker);
        if(s.Cooldown>0||s.AttackWeapon!=""||s.DefenseRecovery>0||!CanMelee(attacker,target)||s.Stamina<weapon.Cost)return false;
        FaceOpponent(attacker,target);s.AimX=s.FacingX;s.AimZ=s.FacingZ;s.AimY=0;
        // Aim the actual off-centre blade at the opponent, rotating the whole visible stance too.
        if(weapon.Trajectory is "baked-sword" or "baked-pickaxe")
        {
            var tip=MeleeProfiles.Blade(weapon,weapon.Windup+weapon.Active*.6).B;
            double angle=Math.Atan2(-tip.X,tip.Z),x=s.AimX,z=s.AimZ;
            s.AimX=x*Math.Cos(angle)+z*Math.Sin(angle);s.AimZ=z*Math.Cos(angle)-x*Math.Sin(angle);
        }
        s.AttackWeapon=weapon.Id;s.AttackTime=0;s.ContactResolved=false;s.LastContact="";s.Defense="";s.DefenseTime=0;
        s.Stamina-=weapon.Cost;s.Cooldown=weapon.Duration;s.Strikes++;attacker.Status="攻击起手 · "+target.Name;return true;
    }
    private sealed record Contact(float Time,string Kind,DemoWorker? Actor=null,string Part="torso");
    private readonly Dictionary<(int,int),List<DemoWorker>> _meleeBuckets=new();
    // Called once after movement/orders, with one common spatial snapshot for all combatants.
    public void AdvanceMelee(double dt)
    {
        if(!double.IsFinite(dt)||dt<=0)return;
        if(!Actors.Any(a=>a.Combat.AttackWeapon!=""||a.Combat.Stamina<100||a.Combat.Defense!=""||a.Combat.DefenseRecovery>0))return;
        var actors=Actors.ToArray();bool hasAttack=actors.Any(a=>a.Combat.AttackWeapon!="");_meleeBuckets.Clear();
        foreach(var actor in actors)
        {
            var s=actor.Combat;
            if(s.AttackWeapon==""&&s.Defense==""&&actor.Path.TryPeek(out var next))
            {
                double dx=next.X-actor.X,dz=next.Z-actor.Z,length=Math.Sqrt(dx*dx+dz*dz);
                if(length>.001){s.FacingX=dx/length;s.FacingZ=dz/length;}
            }
            s.DefenseRecovery=Math.Max(0,s.DefenseRecovery-dt);
            if(s.Defense!="")s.DefenseTime+=dt;
            if(s.AttackWeapon==""&&s.Defense=="")s.Stamina=Math.Min(100,s.Stamina+18*dt);
            if(!hasAttack)continue;
            var key=((int)Math.Floor(actor.X/4),(int)Math.Floor(actor.Z/4));
            if(!_meleeBuckets.TryGetValue(key,out var bucket))_meleeBuckets[key]=bucket=new();bucket.Add(actor);
        }
        foreach(var actor in actors.OrderBy(a=>a.Id,StringComparer.Ordinal))
        {
            var s=actor.Combat;if(s.AttackWeapon=="")continue;
            var weapon=MeleeProfiles.Weapon(s.AttackWeapon);
            if(!Fighting(actor)||EquippedWeapon(actor).Id!=weapon.Id){CancelMelee(actor);continue;}
            double from=s.AttackTime,to=Math.Min(weapon.Duration,from+dt);
            if(!s.ContactResolved&&to>=weapon.Windup&&from<weapon.Windup+weapon.Active)
            {
                double start=Math.Max(from,weapon.Windup),end=Math.Min(to,weapon.Windup+weapon.Active);
                var candidates=NearbyCombatants(actor,weapon.Reach+1).ToArray();
                int steps=Math.Max(1,(int)Math.Ceiling((end-start)/.01));
                for(int step=0;step<steps&&!s.ContactResolved;step++)
                {
                    double t0=start+(end-start)*step/steps,t1=start+(end-start)*(step+1)/steps;
                    var before=WorldBlade(actor,weapon,t0);var after=WorldBlade(actor,weapon,t1);
                    Contact? best=null;
                    void Query(Vector3 a,Vector3 b,float? fixedPhase=null)
                    {
                        var hit=TraceMelee(actor,a,b,weapon.Radius,candidates);
                        if(hit is not null){if(fixedPhase is float phase)hit=hit with{Time=phase+hit.Time*.00001f};if(best is null||hit.Time<best.Time)best=hit;}
                    }
                    // Blade overlap plus swept samples. Bound temporal steps and spatial spacing independently.
                    Query(before.A,before.B,0);Query(after.A,after.B,1);
                    int points=Math.Max(1,(int)Math.Ceiling(Vector3.Distance(before.A,before.B)/.06f));
                    for(int p=0;p<=points;p++)Query(Vector3.Lerp(before.A,before.B,(float)p/points),Vector3.Lerp(after.A,after.B,(float)p/points));
                    if(best is not null){s.ContactResolved=true;ResolveMelee(actor,weapon,best);}
                }
            }
            s.AttackTime=to;if(to>=weapon.Duration){s.AttackWeapon="";if(!s.ContactResolved){s.LastContact="miss";actor.Status="挥空";}}
        }
    }
    private IEnumerable<DemoWorker> NearbyCombatants(DemoWorker actor,float range)
    {
        for(int x=(int)Math.Floor((actor.X-range)/4);x<=(int)Math.Floor((actor.X+range)/4);x++)
            for(int z=(int)Math.Floor((actor.Z-range)/4);z<=(int)Math.Floor((actor.Z+range)/4);z++)
                if(_meleeBuckets.TryGetValue((x,z),out var bucket))foreach(var candidate in bucket)if(candidate!=actor&&Math.Abs(WorkerHeight(candidate)-WorkerHeight(actor))<3)yield return candidate;
    }
    private (Vector3 A,Vector3 B) WorldBlade(DemoWorker actor,MeleeWeapon weapon,double time)
    {
        var blade=MeleeProfiles.Blade(weapon,time);var s=actor.Combat;var origin=Feet(actor)+Vector3.UnitY*(float)s.AimY;
        var facing=Direction(s.AimX,s.AimZ);return(Local(blade.A,origin,facing),Local(blade.B,origin,facing));
    }
    private Contact? TraceMelee(DemoWorker attacker,Vector3 a,Vector3 b,float radius,DemoWorker[] candidates)
    {
        Contact? best=TerrainContact(a,b,radius) is float t?new(t,"wall"):null;
        void Offer(float? time,string kind,DemoWorker actor,string part="torso")
        {if(time is float hit&&(best is null||hit<best.Time-1e-5))best=new(hit,kind,actor,part);}
        foreach(var other in candidates.OrderBy(c=>c.Id,StringComparer.Ordinal))
        {
            var origin=Feet(other);var s=other.Combat;var facing=s.AttackWeapon!=""?Direction(s.AimX,s.AimZ):Direction(s.FacingX,s.FacingZ);
            var la=Inverse(a,origin,facing);var lb=Inverse(b,origin,facing);
            bool frontal=Vector3.Dot(Feet(attacker)-origin,facing)>0;
            if(Fighting(other)&&s.AttackWeapon==""&&s.DefenseRecovery<=0&&frontal)
            {
                if(s.Defense=="block"&&EquippedShield(other) is {} shield&&s.DefenseTime>=shield.RaiseTime)
                {
                    var pose=CombatGripPoses.Guard(true);var geometry=shield.Geometry;
                    Offer(MeleeGeometry.Box(pose.Inverse(la),pose.Inverse(lb),geometry.Min-new Vector3(radius),geometry.Max+new Vector3(radius)),"block",other);
                }
                var parryWeapon=EquippedWeapon(other);
                if(s.Defense=="parry"&&parryWeapon.CanParry&&s.DefenseTime>=parryWeapon.ParryReady&&s.DefenseTime<=parryWeapon.ParryReady+parryWeapon.ParryWindow)
                {
                    var pose=CombatGripPoses.Guard(false);var geometry=parryWeapon.Geometry;
                    Offer(MeleeGeometry.Capsule(la,lb,pose.Transform(geometry.Start),pose.Transform(geometry.End),radius+geometry.Radius),"parry",other);
                }
            }
            // Broad dwarven body, two arms, head and legs; local axes follow the combat facing.
            Offer(MeleeGeometry.Capsule(la,lb,new(0,.5f,0),new(0,1.0f,0),radius+.32f),"body",other);
            Offer(MeleeGeometry.Sphere(la,lb,new(0,1.2f,0),radius+.23f),"body",other,"head");
            Offer(MeleeGeometry.Capsule(la,lb,new(-.45f,.6f,0),new(-.45f,.98f,0),radius+.18f),"body",other,"right_arm");
            Offer(MeleeGeometry.Capsule(la,lb,new(.45f,.6f,0),new(.45f,.98f,0),radius+.18f),"body",other,"left_arm");
            Offer(MeleeGeometry.Capsule(la,lb,new(-.19f,.1f,0),new(-.19f,.5f,0),radius+.15f),"body",other,"right_leg");
            Offer(MeleeGeometry.Capsule(la,lb,new(.19f,.1f,0),new(.19f,.5f,0),radius+.15f),"body",other,"left_leg");
        }
        return best;
    }
    private float? TerrainContact(Vector3 a,Vector3 b,float radius)
    {
        Vector3 min=Vector3.Min(a,b)-new Vector3(radius),max=Vector3.Max(a,b)+new Vector3(radius);float? nearest=null;
        for(int x=(int)Math.Floor(min.X+.5f);x<=(int)Math.Floor(max.X+.5f);x++)
            for(int z=(int)Math.Floor(min.Z+.5f);z<=(int)Math.Floor(max.Z+.5f);z++)
                for(int y=(int)Math.Floor(min.Y);y<=(int)Math.Floor(max.Y);y++)
                {
                    var c=new Cell(x,z,y);if(Inside(c)&&!Solid(c)&&!(Buildings.TryGetValue(c,out var building)&&building.Kind==BuildKind.Floodgate&&!GateOpen(c)))continue;
                    var t=MeleeGeometry.Box(a,b,new Vector3(x-.5f,y,z-.5f)-new Vector3(radius),new Vector3(x+.5f,y+1,z+.5f)+new Vector3(radius));
                    if(t is float hit&&(nearest is null||hit<nearest))nearest=hit;
                }
        return nearest;
    }
    private void ResolveMelee(DemoWorker attacker,MeleeWeapon weapon,Contact hit)
    {
        var s=attacker.Combat;s.LastContact=hit.Kind;
        if(hit.Actor is not {} victim){attacker.Status="武器撞到墙体";return;}
        bool hostile=Enemies.Contains(attacker)!=Enemies.Contains(victim);
        if(!hostile){s.LastContact="ally";attacker.Status="攻击被友军挡住";return;}
        var defense=victim.Combat;
        if(hit.Kind is "block" or "parry")
        {
            double cost=hit.Kind=="block"?(EquippedShield(victim)?.Cost??10)+Math.Max(0,weapon.Impact-(EquippedShield(victim)?.Strength??0)):EquippedWeapon(victim).ParryCost+weapon.Impact*.3;
            bool broken=defense.Stamina<cost;defense.Stamina=Math.Max(0,defense.Stamina-cost);
            defense.LastContact=broken?"guard_break":hit.Kind;defense.ReactionUntil=_simulationTime+.3;
            if(broken){defense.Defense="";defense.DefenseRecovery=.8;attacker.Status="破防";victim.Status="防御失衡";}
            else {attacker.Status=hit.Kind=="block"?"攻击被盾牌挡住":"攻击被武器招架";victim.Status=hit.Kind=="block"?"盾牌格挡":"招架成功";defense.Defense="";defense.DefenseRecovery=.15;}
            // The first intercept consumes this strike even on guard break; following attacks exploit the opening.
            return;
        }
        double damage=weapon.Damage*(1+attacker.Person.Level("combat")*.025)*(1+attacker.Person.Talents.GetValueOrDefault("combat")*.15)*attacker.Person.Manipulation;
        ApplyInjury(victim,hit.Part,weapon.Injury,Math.Clamp(damage,.01,100));AwardWork(attacker,"combat",12);
        defense.FlashUntil=_simulationTime+.22;defense.LastContact="hit";defense.ReactionUntil=_simulationTime+.22;
        attacker.Status="命中 "+victim.Name;
        if(!Fighting(victim)){InterruptForOrders(victim);victim.Status=victim.Person.Dead?"已死亡":"失能 · 等待照料";Log(victim.Name+"失去战斗能力。");}
    }
}
