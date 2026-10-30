namespace NoDwarf.Simulation;

public sealed record DomainFact(long Sequence,string EventId,string Type,int Version,double Time,string Actor,string Target,string Result,string Scope="participants");
public sealed class DomainEventArchive
{
    public string Epoch {get;set;}=Guid.NewGuid().ToString("N");
    public long Sequence {get;set;}
    public List<DomainFact> Recent {get;set;}=new();
    public Dictionary<string,long> Delivered {get;set;}=new();
    public List<ResidentCommand> Commands {get;set;}=new();
}
// Committed facts only. Subscribers run at the end of a tick; their commands enter the next tick.
// Subscriber IDs are durable receipts, not transient delegate identities. Restore never republishes facts.
public sealed class DomainEventStream
{
    private DomainEventArchive state=new();
    private readonly SortedDictionary<string,Func<DomainFact,IEnumerable<ResidentCommand>>> listeners=new(StringComparer.Ordinal);
    private bool dispatching;
    public IReadOnlyList<DomainFact> Recent=>state.Recent.AsReadOnly();
    public List<string> Errors {get;}=new();
    public void Subscribe(string id,Func<DomainFact,IEnumerable<ResidentCommand>> listener)
    {
        if(dispatching||string.IsNullOrWhiteSpace(id)||id.Length>100||listeners.Count>=64||!listeners.TryAdd(id,listener))throw new InvalidOperationException("事件订阅 ID 无效或重复："+id);
        state.Delivered.TryAdd(id,state.Sequence);
    }
    internal DomainFact Commit(string type,double time,string actor,string target,string result)
    {
        if(dispatching)throw new InvalidOperationException("事件监听者应返回下一轮命令，不能递归提交事实");
        var fact=new DomainFact(++state.Sequence,state.Epoch+":"+state.Sequence,type,1,time,actor,target,result);
        state.Recent.Add(fact);
        // Do not discard facts before all current subscribers have seen them.
        return fact;
    }
    internal void Dispatch()
    {
        if(dispatching)throw new InvalidOperationException("事件派发重入");
        dispatching=true;
        try
        {
            foreach(var fact in state.Recent)foreach(var (id,listener) in listeners)
            {
                if(state.Delivered.GetValueOrDefault(id)>=fact.Sequence)continue;
                state.Delivered[id]=fact.Sequence;
                try
                {
                    foreach(var action in listener(fact).Take(16))if(state.Commands.Count<64)state.Commands.Add(action);
                }
                catch(Exception ex){Errors.Add(id+" / "+fact.EventId+"："+ex.Message);if(Errors.Count>32)Errors.RemoveAt(0);}
            }
            if(state.Recent.Count>256)state.Recent.RemoveRange(0,state.Recent.Count-256);
        }
        finally{dispatching=false;}
    }
    internal ResidentCommand[] TakeCommands(){var commands=state.Commands.ToArray();state.Commands.Clear();return commands;}
    internal DomainEventArchive Save()=>new(){Epoch=state.Epoch,Sequence=state.Sequence,Recent=new(state.Recent),Delivered=new(state.Delivered),Commands=new(state.Commands)};
    internal void Restore(DomainEventArchive archive)
    {
        if(archive is null||!Guid.TryParseExact(archive.Epoch,"N",out _)||archive.Sequence<0||archive.Recent is null||archive.Recent.Count>4096||archive.Delivered is null||archive.Delivered.Count>64||archive.Commands is null||archive.Commands.Count>64||archive.Commands.Any(c=>c is null)||archive.Delivered.Any(p=>string.IsNullOrEmpty(p.Key)||p.Key.Length>100||p.Value<0||p.Value>archive.Sequence))throw new InvalidDataException("事件模块存档无效");
        long previous=0;
        foreach(var e in archive.Recent)
        {
            if(e is null||e.Sequence<=previous||e.Sequence>archive.Sequence||e.EventId!=archive.Epoch+":"+e.Sequence||e.Version!=1||e.Type is not ("resident.fed" or "resident.cared")||!double.IsFinite(e.Time)||e.Time<0||e.Actor is null||e.Actor.Length>100||e.Target is null||e.Target.Length>100||e.Result is null||e.Result.Length>1000||e.Scope!="participants")throw new InvalidDataException("事件事实记录无效");
            previous=e.Sequence;
        }
        state=archive;
        foreach(string id in listeners.Keys)state.Delivered.TryAdd(id,state.Sequence);
    }
}
public sealed partial class DemoWorld
{
    private DomainEventStream? _domainEvents;
    public DomainEventStream DomainEvents=>_domainEvents??=CreateDomainEvents();
    private DomainEventStream CreateDomainEvents()
    {
        var events=new DomainEventStream();
        events.Subscribe("base:care-memory",fact=>
        {
            var actor=Workers.FirstOrDefault(w=>w.Id==fact.Actor);var target=Workers.FirstOrDefault(w=>w.Id==fact.Target);
            if(actor is null||target is null)return Array.Empty<ResidentCommand>();
            if(fact.Type=="resident.fed")
            {
                target.Person.Remember("fed:"+actor.Id,actor.Name+"送来了口粮。",fact.Time,target:actor.Id,actor:actor.Id,importance:85,eventId:fact.EventId);
                actor.Person.Remember("helped:"+target.Id,"给"+target.Name+"送达了一份口粮。",fact.Time,target:target.Id,actor:actor.Id,importance:75,eventId:fact.EventId);
            }
            if(fact.Type=="resident.cared")
            {
                target.Person.Remember("care:"+actor.Id,actor.Name+fact.Result,fact.Time,target:actor.Id,actor:actor.Id,eventId:fact.EventId);
                if(actor!=target)actor.Person.Remember("helped:"+target.Id,"完成了对"+target.Name+"的护理。",fact.Time,target:target.Id,actor:actor.Id,importance:75,eventId:fact.EventId);
            }
            return Array.Empty<ResidentCommand>();
        });
        events.Subscribe("base:care-relations",fact=>
        {
            if(fact.Type=="resident.cared"&&fact.Actor!=fact.Target)
            {
                var actor=Workers.FirstOrDefault(w=>w.Id==fact.Actor);var target=Workers.FirstOrDefault(w=>w.Id==fact.Target);
                if(actor is not null&&target is not null)ChangeOpinion(target,actor,8,"照料");
            }
            return Array.Empty<ResidentCommand>();
        });
        return events;
    }
}
