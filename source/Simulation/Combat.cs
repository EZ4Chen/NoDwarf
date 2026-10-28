namespace NoDwarf.Simulation;

// Orders and cooldowns belong to simulation time, never to rendering frames.
public sealed class CombatOrder
{
    public string AttackWeapon {get;set;}="";
    public double AttackTime {get;set;}
    public double AimX {get;set;}=0;
    public double AimZ {get;set;}=1;
    public double AimY {get;set;}
    public bool ContactResolved {get;set;}
    public double FacingX {get;set;}=0;
    public double FacingZ {get;set;}=1;
    public double Stamina {get;set;}=100;
    public string Defense {get;set;}="";
    public string DefensePreference {get;set;}="auto";
    public double DefenseTime {get;set;}
    public double DefenseRecovery {get;set;}
    public string LastContact {get;set;}="";
    public double ReactionUntil {get;set;}
    public bool Drafted {get;set;}
    public string Target {get;set;}="";
    public Cell? Destination {get;set;}
    public double Cooldown {get;set;}
    public double RetryAt {get;set;}
    public int Strikes {get;set;}
    public double FlashUntil {get;set;}
}
public sealed partial class DemoWorld
{
    public List<DemoWorker> Enemies {get;}=new();
    public IEnumerable<DemoWorker> Actors=>Workers.Concat(Enemies);
    public Cell? RallyPoint {get;private set;}
    public bool CivilianAlert {get;private set;}
    private int _raidSerial;
    public string BattleOutcome {get;private set;}="";
    private void UpdateBattleOutcome()
    {
        if(Enemies.Count==0)return;
        string outcome=Workers.Count>0&&Workers.All(w=>w.Person.Incapacitated)?"defeat":Enemies.All(e=>e.Person.Incapacitated)?"victory":"";
        if(outcome==BattleOutcome)return;
        BattleOutcome=outcome;
        Log(outcome=="defeat"?"全员失能：营地已无法继续防卫。可暂停并读取此前存档。":outcome=="victory"?"当前敌人均已失能：可安排医疗；若敌人恢复行动会重新警报。":"警告：战斗恢复，请重新检查防卫和集合警报。");
    }
    public bool MilitaryControlled(DemoWorker w)=>w.Combat.Drafted||CivilianAlert;
    private bool Fighting(DemoWorker w)=>!w.Person.Incapacitated&&!w.Person.Dead;
    private void InterruptForOrders(DemoWorker w)
    {
        if(w.Job is not null)Release(w.Job);
        CancelResidentActivity(w);w.Person.PausedResearch=null;StopMovement(w);
        CancelMelee(w);
        w.Combat.Target="";w.Combat.Destination=null;w.Combat.RetryAt=0;
        w.Person.DecisionVersion++;Revision++;
    }
    public ResidentResult DraftResident(string id,bool drafted)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==id);
        if(w is null||drafted&&!Fighting(w))return new(false,"居民无法征召");
        InterruptForOrders(w);w.Combat.Drafted=drafted;w.NextJobSearch=0;
        w.Status=drafted?"已征召 · 原地警戒":"解除征召 · 等待工作";return new(true,w.Status);
    }
    public ResidentResult MilitaryMove(string id,Cell destination)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==id);
        if(w is null||!w.Combat.Drafted||!Fighting(w)||w.Person.Mobility<.05)return new(false,"请先征召可移动的居民");
        var landing=AvailableMilitaryDestination(w,destination);
        if(landing is null)return new(false,"目的地或附近同层位置不可达／已占用");
        destination=landing.Value;
        var path=FindPath(w.Tile,destination);if(path is null)return new(false,"目的地不可达");
        InterruptForOrders(w);w.Combat.Destination=destination;
        foreach(var c in path)w.Path.Enqueue(c);w.CheckedNavigationVersion=Navigation.Version;
        return new(true,"前往指定位置");
    }
    public ResidentResult MilitaryAttack(string id,string target)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==id);var enemy=Enemies.FirstOrDefault(e=>e.Id==target);
        if(w is null||!w.Combat.Drafted||!Fighting(w)||enemy is null||!Fighting(enemy))return new(false,"需要已征召居民和可攻击的敌人");
        InterruptForOrders(w);w.Combat.Target=target;return new(true,"追击 "+enemy.Name);
    }
    public ResidentResult MilitaryStop(string id)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==id);if(w is null)return new(false,"居民不存在");
        InterruptForOrders(w);w.Status="停止 · 原地警戒";return new(true,w.Status);
    }
    public ResidentResult SetRallyPoint(Cell c)
    {
        if(!Walkable(c))return new(false,"集合点需要可站立地面");
        RallyPoint=c;
        if(CivilianAlert)foreach(var w in Workers.Where(w=>!w.Combat.Drafted))InterruptForOrders(w);
        Revision++;return new(true,"已设置平民集合点");
    }
    public ResidentResult SetCivilianAlert(bool enabled)
    {
        if(enabled&&RallyPoint is null)return new(false,"请先在地面设置集合点");
        CivilianAlert=enabled;
        foreach(var w in Workers.Where(w=>!w.Combat.Drafted)){InterruptForOrders(w);w.NextJobSearch=0;}
        Log(enabled?"集合警报：平民暂停工作并前往集合点。":"警报解除：重新检查并分配工作。");return new(true,enabled?"集合警报已开启":"集合警报已解除");
    }
    public bool CanMelee(DemoWorker a,DemoWorker b)=>Fighting(a)&&Fighting(b)&&a.Person.Manipulation>=.05&&
        Walkable(a.Tile)&&Walkable(b.Tile)&&MeleeInReach(a,b);
    public ResidentResult StartTestRaid(int count=1)
    {
        if(count is <1 or >3)return new(false,"测试袭击人数为 1—3");
        if(Enemies.Any(Fighting))return new(false,"已有未结束的袭击");
        var resident=Workers.FirstOrDefault(Fighting);if(resident is null)return new(false,"需要至少一位可行动居民");
        // Probe bounded samples along all four actual map edges, then pick the nearest reachable entry.
        var entries=new List<Cell>();
        for(int i=1;i<Width-1;i+=8){entries.Add(Surface(0,i));entries.Add(Surface(Width-1,i));entries.Add(Surface(i,0));entries.Add(Surface(i,Depth-1));}
        var starts=entries.Where(Walkable).OrderBy(c=>Math.Abs(c.X-resident.Tile.X)+Math.Abs(c.Z-resident.Tile.Z))
            .Where(c=>Navigation.CanReach(c,resident.Tile)).Take(count).ToArray();
        if(starts.Length<count)return new(false,"地图边缘没有通往居民的入口；未生成敌人");
        Enemies.Clear();_raidSerial++;BattleOutcome="";
        for(int i=0;i<count;i++)Enemies.Add(new(){Id=$"raider-{_raidSerial}-{i}",Name=$"袭击者 {i+1}",X=starts[i].X,Z=starts[i].Z,Level=starts[i].Level,Combat=new(){Drafted=true},Person=new()});
        var entry=starts[0];
        Revision++;Log($"测试袭击：{count} 名敌人从 {entry.X}, {entry.Z} 进入地图。");return new(true,"测试袭击已生成");
    }
    private void CombatStrike(DemoWorker attacker,DemoWorker victim)=>BeginMelee(attacker,victim);
    private bool UpdateMilitary(DemoWorker w,double dt,ref int budget,bool enemy=false)
    {
        if(!enemy&&!MilitaryControlled(w))return false;
        if(enemy&&!w.Person.Dead)
        {
            double bleeding=w.Person.Injuries.Where(i=>i.Kind=="cut"&&!i.Treated).Sum(i=>i.Severity)*.001;
            w.Person.Blood=Math.Clamp(w.Person.Blood+(bleeding>0?-bleeding:.015)*dt,0,100);
        }
        var state=w.Combat;
        if(!Fighting(w)){CancelMelee(w);if(w.Job is not null)Release(w.Job);CancelResidentActivity(w);w.Status=w.Person.Dead?"已死亡":"失能 · 等待照料";return true;}
        if(!enemy&&!state.Drafted)
        {
            if(state.Destination is null&&RallyPoint is Cell point&&_simulationTime>=state.RetryAt&&budget>0)
            {budget--;state.Destination=AvailableMilitaryDestination(w,point);state.RetryAt=_simulationTime+2;if(state.Destination is null)w.Status="集合点及附近同层位置不可达／已占用";}
            if(state.Destination is Cell assigned&&w.Tile==assigned)
            {
                StopMovement(w);
                if(w.Person.Hunger<40&&Inventory(w).Any(s=>s.Item=="meal"))
                {ConsumePersonal(w,"meal");w.Person.Hunger=Math.Min(100,w.Person.Hunger+45);}
                w.Status=Inventory(w).Any(s=>s.Item=="meal")?"集合警戒 · 暂停工作":"集合警戒 · 无随身口粮，需补给或解除警报";
                return true;
            }
            if(state.Destination is Cell rally)MilitaryRoute(w,rally,dt,ref budget,"集合点不可达");
            return true;
        }
        // Explicit movement is a move order, not attack-move: it must allow retreat.
        if(!enemy&&state.Destination is Cell ordered)
        {
            if(w.Tile==ordered&&w.Path.Count==0){state.Destination=null;w.Status="已就位 · 警戒";}
            else MilitaryRoute(w,ordered,dt,ref budget,"目的地不可达");
            return true;
        }
        var opponents=enemy?Workers:Enemies;
        var target=opponents.FirstOrDefault(o=>o.Id==state.Target&&Fighting(o));
        if(target is null)
        {
            state.Target="";
            // Residents hold their position unless explicitly ordered to pursue.
            target=opponents.Where(Fighting).Where(o=>enemy||CanMelee(w,o)).OrderBy(o=>Math.Abs(o.X-w.X)+Math.Abs(o.Z-w.Z)).FirstOrDefault();
            if(enemy&&target is not null)state.Target=target.Id;
        }
        if(state.AttackWeapon!=""){StopMovement(w);return true;}
        if(target is not null&&CanMelee(w,target))
        {
            StopMovement(w);FaceOpponent(w,target);
            if(!PrepareDefense(w,target))CombatStrike(w,target);
            return true;
        }
        state.Defense="";state.DefenseTime=0;
        if(target is not null&&(enemy||state.Target!=""))
        {
            if(w.Path.Count==0&&_simulationTime>=state.RetryAt&&budget>0)
            {
                budget--;state.RetryAt=_simulationTime+1;StopMovement(w);
                var candidates=MeleeApproachCells(w,target).Where(c=>!MilitaryStandReserved(w,c)).ToArray();
                var path=Navigation.FindNearest(w.Tile,candidates,out _);
                if(path is not null)foreach(var c in path)w.Path.Enqueue(c);
                w.CheckedNavigationVersion=Navigation.Version;w.Status=path is null?"目标不可达 · 等待":"接近 "+target.Name;
            }
            StepMilitary(w,dt,ref budget);return true;
        }
        if(state.Destination is Cell destination)
        {
            if(w.Tile==destination&&w.Path.Count==0){state.Destination=null;w.Status="已就位 · 警戒";}
            else MilitaryRoute(w,destination,dt,ref budget,"目的地不可达");
        }
        else {StopMovement(w);w.Status=enemy?"无可行动目标":"已征召 · 原地警戒";}
        return true;
    }
    private void MilitaryRoute(DemoWorker w,Cell destination,double dt,ref int budget,string blocked)
    {
        if(w.Path.Count<=1&&MilitaryStandReserved(w,destination))
        {
            if(_simulationTime<w.Combat.RetryAt||budget<=0){w.Status="目的地已占用 · 等待空位";return;}
            budget--;w.Combat.RetryAt=_simulationTime+2;
            var replacement=AvailableMilitaryDestination(w,destination);
            StopMovement(w);
            if(replacement is null){w.Status="目的地已占用 · 等待空位";return;}
            destination=replacement.Value;w.Combat.Destination=destination;w.Combat.RetryAt=0;
        }
        if(w.Path.Count==0&&_simulationTime>=w.Combat.RetryAt&&budget>0)
        {
            budget--;w.Combat.RetryAt=_simulationTime+2;
            var path=FindPath(w.Tile,destination);if(path is not null)foreach(var c in path)w.Path.Enqueue(c);
            w.CheckedNavigationVersion=Navigation.Version;w.Status=path is null?blocked:"前往集合／指令位置";
        }
        StepMilitary(w,dt,ref budget);
    }
    private void StepMilitary(DemoWorker w,double dt,ref int budget)
    {if(w.Person.Mobility<.05){StopMovement(w);w.Status="无法移动 · 需要照料";return;}if(w.Path.Count>0&&EnsureWorkerRoute(w,ref budget)&&w.Path.Count>0)Move(w,dt);}
}
