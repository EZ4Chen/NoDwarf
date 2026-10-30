namespace NoDwarf.Simulation;

public sealed record ResidentTaskIntent(string Actor,string Task,string Target,string Origin="manual");
public sealed record ResidentTaskReceipt(string Id,string Task,string Actor,string Target,string Outcome,string Reason,double Time);
public sealed class ResidentTaskArchive
{
    public long NextId {get;set;}
    public List<ResidentTaskReceipt> Recent {get;set;}=new();
}
public enum ResidentTaskStep { Running, Completed, Failed }
public delegate ResidentTaskStep ResidentTaskTick(DemoWorld world,DemoWorker actor,ResidentActivity state,double elapsed,ref int navigationBudget);
public sealed record ResidentTaskHandler(string Id,IReadOnlyList<string> Stages,
    Func<DemoWorld,DemoWorker,ResidentTaskIntent,bool> Start,
    Func<DemoWorld,DemoWorker,ResidentActivity,bool> Validate,
    ResidentTaskTick Tick,Action<DemoWorld,DemoWorker,ResidentActivity,string> Cleanup);
public sealed class ResidentTaskRegistry
{
    private readonly Dictionary<string,ResidentTaskHandler> handlers=new(StringComparer.Ordinal);
    public ResidentTaskRegistry Register(ResidentTaskHandler handler)
    {
        if(string.IsNullOrEmpty(handler.Id)||handler.Stages.Count==0||!handlers.TryAdd(handler.Id,handler))throw new InvalidOperationException("任务类型重复或无效："+handler.Id);
        return this;
    }
    public ResidentTaskHandler? Find(string id)=>handlers.GetValueOrDefault(id);
}
public sealed partial class DemoWorld
{
    private ResidentTaskArchive _residentTasks=new();
    private ResidentTaskRegistry? _taskRegistry;
    public IReadOnlyList<ResidentTaskReceipt> TaskReceipts=>_residentTasks.Recent.AsReadOnly();
    public ResidentTaskRegistry TaskRegistry=>_taskRegistry??=new ResidentTaskRegistry().Register(new("base:feeding",Array.AsReadOnly(new[]{"food_supply","feed"}),
        (world,actor,intent)=>world.Workers.FirstOrDefault(w=>w.Id==intent.Target) is {} patient&&world.BeginFoodSupplyCore(actor,patient,intent.Origin),
        (world,actor,state)=>world.ValidFoodActivity(actor,state),
        (DemoWorld world,DemoWorker actor,ResidentActivity state,double dt,ref int budget)=>{world.TickFeedingTask(actor,state,dt,ref budget);return ResidentTaskStep.Running;},
        (world,actor,state,reason)=>{ /* Reservation is the activity itself; carried food stays in inventory. */ }));
    public ResidentResult StartResidentTask(ResidentTaskIntent intent)
    {
        var actor=Workers.FirstOrDefault(w=>w.Id==intent.Actor);var handler=TaskRegistry.Find(intent.Task);
        if(actor is null||handler is null||intent.Origin is not ("manual" or "need" or "work" or "personality"))return new(false,"任务、人物或来源无效");
        if(!handler.Start(this,actor,intent))return new(false,"任务条件不满足");
        var state=actor.Person.Activity??throw new InvalidOperationException("任务处理器未创建活动");
        if(!handler.Stages.Contains(state.Kind))throw new InvalidOperationException("任务处理器创建了未知阶段");
        AttachTask(state,handler.Id);return new(true,"已安排任务");
    }
    private bool BeginFoodSupply(DemoWorker helper,DemoWorker patient)=>StartResidentTask(new(helper.Id,"base:feeding",patient.Id,helper==patient?"need":"work")).Ok;
    private void AttachTask(ResidentActivity activity,string task)
    {activity.TaskType=task;activity.TaskId="task:"+(++_residentTasks.NextId);}
    private void FinishResidentTask(DemoWorker actor,string outcome,string reason)
    {
        var state=actor.Person.Activity;if(state is null||state.TaskType=="")return;
        TaskRegistry.Find(state.TaskType)?.Cleanup(this,actor,state,reason);
        if(!_residentTasks.Recent.Any(r=>r.Id==state.TaskId))
        {
            _residentTasks.Recent.Add(new(state.TaskId,state.TaskType,actor.Id,state.Target,outcome,reason,_simulationTime));
            if(_residentTasks.Recent.Count>128)_residentTasks.Recent.RemoveAt(0);
        }
        state.TaskId="";state.TaskType="";
    }
    private bool TickFeedingTask(DemoWorker actor,ResidentActivity state,double dt,ref int budget)
    {
        if(!ValidFoodActivity(actor,state)||actor.Person.Manipulation<.05||actor.Path.Count>0&&actor.Person.Mobility<.05)
        {CancelResidentActivity(actor,"照料对象、能力或口粮已变化");actor.Person.FoodIssue="送饭条件改变，等待重新安排";return true;}
        if(actor.Path.Count>0)
        {
            if(!EnsureWorkerRoute(actor,ref budget)){if(actor.Path.Count==0)CancelResidentActivity(actor,"送饭路线中断");return true;}
            if(actor.Path.Count>0)Move(actor,dt);return true;
        }
        if(actor.Tile!=state.Destination){CancelResidentActivity(actor,"送饭路线中断");return true;}
        actor.Status=ActivityName(state.Kind);state.Progress+=dt;
        if(state.Progress<1)return true;
        return state.Kind=="food_supply"?CompleteFoodSupply(actor,state):CompleteFeeding(actor,state);
    }
    private void RestoreTaskBindings()
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var actor in Workers)
        {
            var a=actor.Person.Activity;if(a is null)continue;
            if(a.TaskType==""){if(a.TaskId!=""||a.Kind is not ("rest" or "relax" or "research" or "pickup" or "eat" or "treat" or "care_supply" or "social" or "rescue_pickup" or "rescue_carry" or "life_supply" or "life_use" or "water_fill"))throw new InvalidDataException("缺少活动任务绑定");continue;}
            var handler=TaskRegistry.Find(a.TaskType)??throw new InvalidDataException("缺少任务处理器："+a.TaskType);
            if(!handler.Stages.Contains(a.Kind)||!ids.Add(a.TaskId)||!a.TaskId.StartsWith("task:")||!long.TryParse(a.TaskId.AsSpan(5),out long serial)||serial<=0||serial>_residentTasks.NextId||_residentTasks.Recent.Any(r=>r.Id==a.TaskId))throw new InvalidDataException("任务阶段、身份或完成回执冲突");
            if(!handler.Validate(this,actor,a))CancelResidentActivity(actor,"读档后任务条件失效");
        }
    }
}
