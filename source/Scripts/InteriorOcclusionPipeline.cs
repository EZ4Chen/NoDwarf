using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Threading;
namespace NoDwarf;

public partial class Main
{
    private sealed record InteriorRequest(long Generation,InteriorCutKey Key,InteriorOcclusionInput Input,long TraceId);
    private sealed record InteriorResult(InteriorTopology Topology,Dictionary<Cell,float> Caps,int Columns,int Thread,double ComputeMs);
    private sealed record InteriorWork(InteriorRequest Request,CancellationTokenSource Cancellation,Task<InteriorResult> Task);
    private readonly InteriorOcclusionScratch _interiorWorkerScratch=new();
    private InteriorRequest? _pendingInterior;
    private InteriorWork? _interiorWork;
    private (long Generation,InteriorCutKey Key)? _desiredInterior;
    private (long Generation,int Floor,int Height)? _publishedInterior;
    private bool _backgroundInterior=true,_interiorStopped;
    private int _interiorDiscarded,_interiorApplied;
    private double _interiorWorkerMs;
    private readonly HashSet<int> _interiorWorkerThreads=new();
    private bool InteriorPending=>_interiorWork is not null||_pendingInterior is not null;

    private void ConfigureInteriorThreads()
    {
        var args=OS.GetCmdlineUserArgs();
        // Legacy checks make immediate geometry assertions. The dedicated thread
        // review tests the asynchronous path; normal play and TPS reviews use it too.
        bool immediateCheck=args.Any(a=>(a.EndsWith("-test")||a.EndsWith("-review"))&&a is not "--tps-test" and not "--interior-thread-test");
        _backgroundInterior=!args.Contains("--interior-sync")&&(!immediateCheck||args.Contains("--interior-async"));
    }
    private bool SetInteriorCaps(IReadOnlyDictionary<Cell,float>? caps)
    {
        var previous=_wallCaps;
        if(ReferenceEquals(previous,caps))return false;
        if(previous is not null&&caps is not null&&previous.Count==caps.Count&&caps.All(p=>previous.TryGetValue(p.Key,out float old)&&old==p.Value))return false;
        _wallCaps=caps;_viewStamp++;return true;
    }
    private void PublishInterior(InteriorCutKey key,Dictionary<Cell,float> caps)
    {
        using var trace=DiagnosticTrace.Span("interior.publish");
        _publishedInterior=(_interiorGeneration,key.Floor,key.Height);
        if(SetInteriorCaps(caps))_revision=-1;
    }
    private void PrepareInteriorAsync(int floor,int height)
    {
        PrepareInteriorCache();
        var key=InteriorKey(floor,height);_desiredInterior=(_interiorGeneration,key);
        if(TryInteriorCaps(key,out var caps))
        {DropPendingInterior();PublishInterior(key,caps);return;}
        // Retain the previous angle while computing on the same unchanged floor.
        // Edits, floor/world changes restore full walls until their new answer arrives.
        if(_publishedInterior!=(_interiorGeneration,floor,height))
        {SetInteriorCaps(null);_publishedInterior=null;}
        if(_interiorWork is {} current&&!current.Cancellation.IsCancellationRequested&&current.Request.Generation==_interiorGeneration&&current.Request.Key==key)
        {DropPendingInterior();return;}
        DropPendingInterior();
        _pendingInterior=new(_interiorGeneration,key,CaptureInteriorInput(),DiagnosticTrace.Queued("interior",_interiorGeneration));
        StartInteriorWork();
    }
    private void StartInteriorWork()
    {
        if(_interiorStopped||!_backgroundInterior||_interiorWork is not null||_pendingInterior is not {} request)return;
        _pendingInterior=null;
        if(request.Generation!=_interiorGeneration){DiagnosticTrace.Mark("interior","discarded",request.TraceId,request.Generation);return;}
        if(TryInteriorCaps(request.Key,out var cached))
        {if(InteriorRequestCurrent(request))PublishInterior(request.Key,cached);DiagnosticTrace.Mark("interior","cache-reused",request.TraceId,request.Generation);return;}
        InteriorTopology? existing=null;
        if(_interiorTopologies.TryGetValue(request.Key.Floor,out var topology))
        {existing=topology.Data;_topologyLru.Remove(topology.Age);_topologyLru.AddLast(topology.Age);}
        var cancellation=new CancellationTokenSource();var token=cancellation.Token;
        // Capture only immutable managed input and a scratch buffer owned by this
        // single worker. The task closure does not retain Main or the live world.
        var input=request.Input;int floor=request.Key.Floor,height=request.Key.Height;
        var offsets=request.Key.Footprint.Offsets;var scratch=_interiorWorkerScratch;
        var task=Task.Run(()=>
        {
            DiagnosticTrace.Mark("interior","started",request.TraceId,request.Generation);
            using var trace=DiagnosticTrace.Span("interior.compute",request.TraceId,request.Generation);
            try
            {
            long started=Stopwatch.GetTimestamp();
            var rooms=existing??InteriorOcclusionBuilder.Rooms(input,floor,token);
            var caps=InteriorOcclusionBuilder.Caps(input,rooms,floor,height,offsets,scratch,token);
            var result=new InteriorResult(rooms,caps,scratch.Touched.Count,System.Environment.CurrentManagedThreadId,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            DiagnosticTrace.Mark("interior","computed",request.TraceId,request.Generation);return result;
            }
            catch(OperationCanceledException){DiagnosticTrace.Mark("interior","canceled",request.TraceId,request.Generation);throw;}
            catch{DiagnosticTrace.Mark("interior","faulted",request.TraceId,request.Generation);throw;}
        },token);
        _interiorWork=new(request,cancellation,task);_interiorOcclusionBuilds++;
        _world.Probe?.Count("view.interior-background-start");
    }
    private bool InteriorRequestCurrent(InteriorRequest request)=>_backgroundInterior&&_interiorView&&_cutaway
        &&request.Generation==_interiorGeneration&&_interiorCacheEpoch==(_world,_world.Navigation.Version,_world.Terrain.Revision)
        &&_desiredInterior==(request.Generation,request.Key)
        &&request.Key==InteriorKey(ActiveLevel,_foundation?.FloorHeight??3);
    private void PumpInteriorWork()
    {
        if(_interiorStopped)return;
        if(_interiorWork is {} work&&work.Task.IsCompleted)
        {
            using var trace=DiagnosticTrace.Span("interior.collect",work.Request.TraceId,work.Request.Generation);
            DiagnosticTrace.Mark("interior","collected",work.Request.TraceId,work.Request.Generation);
            using var timing=_world.Probe?.Measure("view.interior-complete")??default;
            _interiorWork=null;work.Cancellation.Dispose();
            int discardedBefore=_interiorDiscarded;
            if(work.Task.IsCanceled)_interiorDiscarded++;
            else if(work.Task.IsFaulted)
            {
                var error=work.Task.Exception!;
                if(work.Request.Generation==_interiorGeneration)
                {
                    GD.PushError("Interior worker failed; falling back to synchronous calculation: "+error);
                    _backgroundInterior=false;DropPendingInterior();_interiorInputs=null;_revision=-1;
                }
                _interiorDiscarded++;
            }
            else if(work.Request.Generation!=_interiorGeneration||_interiorCacheEpoch!=(_world,_world.Navigation.Version,_world.Terrain.Revision))_interiorDiscarded++;
            else
            {
                var result=work.Task.GetAwaiter().GetResult(); // Already completed: never waits for computation.
                _interiorWorkerThreads.Add(result.Thread);_interiorWorkerMs+=result.ComputeMs;
                StoreInteriorTopology(work.Request.Key.Floor,result.Topology);
                StoreInteriorCaps(work.Request.Key,result.Caps);
                _world.Probe?.Count("view.interior-columns-tested",result.Columns);
                if(InteriorRequestCurrent(work.Request)){PublishInterior(work.Request.Key,result.Caps);_interiorApplied++;DiagnosticTrace.Mark("interior","applied",work.Request.TraceId,work.Request.Generation);}
                else
                {
                    _interiorDiscarded++;
                    // Yaw may move within Main's 5-degree redraw bucket while a cold
                    // job runs. Schedule that latest pose instead of leaving full walls
                    // indefinitely after rejecting the now-obsolete answer.
                    if(_backgroundInterior&&_interiorView&&_cutaway&&_desiredInterior==(work.Request.Generation,work.Request.Key))
                    {_interiorInputs=null;_revision=-1;}
                }
            }
            if(_interiorDiscarded>discardedBefore)DiagnosticTrace.Mark("interior","discarded",work.Request.TraceId,work.Request.Generation);
        }
        StartInteriorWork();
    }
    private void CancelInteriorWork()
    {
        if(_interiorWork is {} work)DiagnosticTrace.Mark("interior","cancel-requested",work.Request.TraceId,work.Request.Generation);
        _interiorWork?.Cancellation.Cancel();DropPendingInterior();_desiredInterior=null;_publishedInterior=null;
    }
    private void DropPendingInterior()
    {
        if(_pendingInterior is {} pending)DiagnosticTrace.Mark("interior","superseded",pending.TraceId,pending.Generation);
        _pendingInterior=null;
    }
    private void StopInteriorWork()
    {
        _interiorStopped=true;CancelInteriorWork();
        if(_interiorWork is {} work)
        {
            var cancellation=work.Cancellation;
            _=work.Task.ContinueWith(t=>{_ = t.Exception;cancellation.Dispose();},TaskScheduler.Default);
            _interiorWork=null;
        }
    }
}
