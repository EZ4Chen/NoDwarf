namespace NoDwarf.Simulation;

public interface ICreatureEnvironment
{
    bool CanStand(Cell cell,CreatureMovementDefinition movement);
    bool CanTraverse(Cell from,Cell to,CreatureMovementDefinition movement);
}
public sealed record CreatureDecisionContext(long Id,Cell Cell,CreatureSpeciesDefinition Species,CreatureMovementDefinition Movement,uint RandomState);
public sealed record CreatureDecision(Cell? Step,uint RandomState);
public delegate CreatureDecision CreatureBehavior(CreatureDecisionContext context,ICreatureEnvironment world);
public sealed class CreatureBehaviorRegistry
{
    private readonly Dictionary<string,CreatureBehavior> _handlers=new(StringComparer.Ordinal);
    public CreatureBehaviorRegistry Register(string id,CreatureBehavior handler)
    {if(string.IsNullOrWhiteSpace(id)||handler is null||!_handlers.TryAdd(id,handler))throw new ArgumentException("重复或无效生物行为："+id);return this;}
    public bool Contains(string id)=>_handlers.ContainsKey(id);
    public CreatureBehavior Get(string id)=>_handlers.TryGetValue(id,out var h)?h:throw new InvalidDataException("未注册生物行为："+id);
    public static CreatureBehaviorRegistry Standard()=>new CreatureBehaviorRegistry()
        .Register("core:idle",(c,w)=>new(null,c.RandomState))
        .Register("core:wander",(c,w)=>
        {
            uint r=c.RandomState;r^=r<<13;r^=r>>17;r^=r<<5;
            Cell[] dirs=[new(1,0),new(0,1),new(-1,0),new(0,-1)];
            for(int i=0;i<4;i++)for(int y=0;y<=c.Movement.StepUp+c.Movement.DropDown;y++)
            {
                int level=y==0?0:y<=c.Movement.StepUp?y:-(y-c.Movement.StepUp);
                Cell next=c.Cell+dirs[((int)(r%4)+i)%4]+new Cell(0,0,level);
                if(w.CanTraverse(c.Cell,next,c.Movement))return new(next,r);
            }
            return new(null,r);
        });
}
public sealed class CreatureState
{
    public long Id {get;set;}
    public string Species {get;set;}="";
    public string DefinitionHash {get;set;}="";
    public Cell Cell {get;set;}
    public Cell? Destination {get;set;}
    public double MoveStarted {get;set;}
    public double MoveEnds {get;set;}
    public double NextThink {get;set;}
    public uint RandomState {get;set;}
    public double? DiedAt {get;set;}
    public Dictionary<string,double> Health {get;set;}=new();
}
public sealed class CreatureArchive
{
    public int Version {get;set;}=1;
    public long NextId {get;set;}
    public int Cursor {get;set;}
    public CreatureState[] Creatures {get;set;}=[];
}
public sealed record CreatureObservation(long Id,string Species,string Name,Cell Cell,Cell? Destination,double MoveFraction,bool Dead,CreatureViewDefinition View,IReadOnlyDictionary<string,double> Health);

public sealed class CreaturePopulation
{
    private readonly CreatureCatalog _catalog;
    private readonly ICreatureEnvironment _environment;
    private readonly CreatureBehaviorRegistry _behaviors;
    private readonly List<CreatureState> _states=new();
    private long _nextId;
    private int _cursor;
    public CreatureCatalog Catalog=>_catalog;
    public int Count=>_states.Count;
    public int LastVisited {get;private set;}
    public int LastDecisions {get;private set;}
    public CreaturePopulation(CreatureCatalog catalog,ICreatureEnvironment environment,CreatureBehaviorRegistry? behaviors=null)
    {
        _catalog=catalog;_environment=environment;_behaviors=behaviors??CreatureBehaviorRegistry.Standard();
        foreach(var species in catalog.Species.Values.Where(s=>s.Controller=="wildlife"))if(!_behaviors.Contains(species.Behavior))throw new InvalidDataException("物种 "+species.Id+" 使用未注册行为："+species.Behavior);
    }
    public long Spawn(string speciesId,Cell cell,double now,uint seed=1)
    {
        Time(now);
        if(!_catalog.Species.TryGetValue(speciesId,out var species))throw new ArgumentException("未知物种："+speciesId);
        if(species.Controller!="wildlife")throw new InvalidOperationException("此物种由居民系统驱动，不能再生成一份野生实例");
        if(_states.Count>=_catalog.PopulationLimit||_states.Count(s=>s.Species==speciesId)>=species.Limit)throw new InvalidOperationException("生物数量达到配置上限（含待处理遗体）");
        if(!_environment.CanStand(cell,_catalog.Movements[species.Movement]))throw new InvalidOperationException("生物生成位置不可通行");
        if(_nextId==long.MaxValue)throw new InvalidOperationException("生物 ID 已耗尽");
        var state=new CreatureState{Id=++_nextId,Species=speciesId,DefinitionHash=species.Fingerprint,Cell=cell,NextThink=now+species.ThinkSeconds,RandomState=seed==0?1:seed,Health=_catalog.Bodies[species.Body].Parts.ToDictionary(p=>p.Id,p=>p.Health)};
        _states.Add(state);return state.Id;
    }
    public bool Remove(long id)
    {int i=_states.FindIndex(s=>s.Id==id);if(i<0)return false;_states.RemoveAt(i);if(i<_cursor)_cursor--;if(_cursor>=_states.Count)_cursor=0;return true;}
    public bool Damage(long id,string part,double amount,double now)
    {
        Time(now);if(!double.IsFinite(amount)||amount<=0)throw new ArgumentOutOfRangeException(nameof(amount));
        var s=Find(id);if(!s.Health.ContainsKey(part))throw new ArgumentException("未知生物部位："+part);
        if(s.DiedAt is not null)return false;
        s.Health[part]=Math.Max(0,s.Health[part]-amount);
        var body=_catalog.Bodies[_catalog.Species[s.Species].Body];
        if(body.Parts.Any(p=>p.Vital&&s.Health[p.Id]==0)){s.DiedAt=now;s.Destination=null;}
        return true;
    }
    public CreatureObservation Observe(long id,double now)
    {
        Time(now);var s=Find(id);var def=_catalog.Species[s.Species];
        double fraction=s.Destination is null?0:Math.Clamp((now-s.MoveStarted)/(s.MoveEnds-s.MoveStarted),0,1);
        return new(s.Id,s.Species,def.Name,s.Cell,s.Destination,fraction,s.DiedAt is not null,def.View,new System.Collections.ObjectModel.ReadOnlyDictionary<string,double>(new Dictionary<string,double>(s.Health)));
    }
    public long[] Ids()=>_states.Select(s=>s.Id).ToArray();
    public void Advance(double now)
    {
        Time(now);LastVisited=LastDecisions=0;int budget=Math.Min(_states.Count,_catalog.UpdateBudget);
        while(LastVisited<budget)
        {
            LastVisited++;var s=_states[_cursor];_cursor=(_cursor+1)%_states.Count;
            if(s.DiedAt is not null)continue;
            var def=_catalog.Species[s.Species];var movement=_catalog.Movements[def.Movement];
            if(s.Destination is Cell end)
            {
                if(!_environment.CanTraverse(s.Cell,end,movement)){s.Destination=null;s.NextThink=now+def.ThinkSeconds;continue;}
                if(now<s.MoveEnds)continue;
                s.Cell=end;s.Destination=null;
            }
            if(now<s.NextThink)continue;
            s.NextThink=now+def.ThinkSeconds;LastDecisions++;
            var decision=_behaviors.Get(def.Behavior)(new(s.Id,s.Cell,def,movement,s.RandomState),_environment);
            s.RandomState=decision.RandomState==0?1:decision.RandomState;
            if(decision.Step is not Cell next||next==s.Cell||!Adjacent(s.Cell,next)||!_environment.CanTraverse(s.Cell,next,movement))continue;
            s.Destination=next;s.MoveStarted=now;s.MoveEnds=now+Math.Sqrt(Math.Pow(next.X-s.Cell.X,2)+Math.Pow(next.Z-s.Cell.Z,2)+Math.Pow(next.Level-s.Cell.Level,2))/movement.Speed;
        }
        LastVisited=budget;
    }
    private static bool Adjacent(Cell a,Cell b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z)<=1&&Math.Abs(a.Level-b.Level)<=2;
    private static void Time(double now){if(!double.IsFinite(now)||now<0)throw new ArgumentOutOfRangeException(nameof(now));}
    private CreatureState Find(long id)=>_states.FirstOrDefault(s=>s.Id==id)??throw new ArgumentException("未知生物实例："+id);
    private static CreatureState Copy(CreatureState s)=>new(){Id=s.Id,Species=s.Species,DefinitionHash=s.DefinitionHash,Cell=s.Cell,Destination=s.Destination,MoveStarted=s.MoveStarted,MoveEnds=s.MoveEnds,NextThink=s.NextThink,RandomState=s.RandomState,DiedAt=s.DiedAt,Health=new(s.Health)};
    public CreatureArchive Save()=>new(){NextId=_nextId,Cursor=_cursor,Creatures=_states.Select(Copy).ToArray()};
    public void Restore(CreatureArchive archive,double now)
    {
        Time(now);
        if(archive is null||archive.Version!=1||archive.Creatures is null||archive.Creatures.Length>_catalog.PopulationLimit||archive.NextId<0||archive.Cursor<0||archive.Cursor>=Math.Max(1,archive.Creatures.Length))throw new InvalidDataException("生物存档结构无效");
        var ids=new HashSet<long>();var list=new List<CreatureState>();
        foreach(var s in archive.Creatures)
        {
            if(s is null||s.Id<=0||s.Id>archive.NextId||!ids.Add(s.Id)||s.Species is null||!_catalog.Species.TryGetValue(s.Species,out var def))throw new InvalidDataException("生物 ID 重复或缺少物种定义");
            if(def.Controller!="wildlife")throw new InvalidDataException("居民物种不能恢复为野生实例");
            var parts=_catalog.Bodies[def.Body].Parts;
            if(s.DefinitionHash!=def.Fingerprint)throw new InvalidDataException("物种配置已改变，请新建游戏："+s.Species);
            if(!DemoWorld.Inside(s.Cell)||s.Health is null||s.Health.Count!=parts.Count||parts.Any(p=>!s.Health.TryGetValue(p.Id,out var hp)||!double.IsFinite(hp)||hp<0||hp>p.Health)||!double.IsFinite(s.NextThink)||s.NextThink<0||s.RandomState==0||
                !double.IsFinite(s.MoveStarted)||!double.IsFinite(s.MoveEnds)||s.MoveStarted<0||s.MoveEnds<0||s.DiedAt is double death&&(!double.IsFinite(death)||death<0||death>now)||
                (s.DiedAt is not null)!=parts.Any(p=>p.Vital&&s.Health[p.Id]==0)||s.Destination is Cell end&&(!DemoWorld.Inside(end)||!Adjacent(s.Cell,end)||end==s.Cell||s.DiedAt is not null||s.MoveStarted>now||s.MoveEnds<=s.MoveStarted))throw new InvalidDataException("生物身体或移动状态无效");
            var restored=Copy(s);restored.DefinitionHash=def.Fingerprint;list.Add(restored);
        }
        if(list.GroupBy(s=>s.Species).Any(g=>g.Count()>_catalog.Species[g.Key].Limit))throw new InvalidDataException("物种数量超过配置上限");
        _states.Clear();_states.AddRange(list);_nextId=archive.NextId;_cursor=archive.Cursor;
    }
}
