using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;

namespace NoDwarf.Simulation;

// Explicitly opt-in, bounded capture. No disk I/O or stack walking in hot paths.
// Thread IDs below are managed IDs, not OS thread IDs from EventPipe/Instruments.
public static class DiagnosticTrace
{
    private const int Limit=200_000;
    private static readonly object Gate=new();
    private static readonly List<Entry> Entries=new();
    private static readonly string? DirectoryPath=Environment.GetEnvironmentVariable("NODWARF_PERF_OUTPUT_DIR");
    private static readonly long Origin=Stopwatch.GetTimestamp();
    private static readonly DateTime OriginUtc=DateTime.UtcNow;
    private static bool _enabled=DirectoryPath is not null&&Environment.GetEnvironmentVariable("NODWARF_PROFILE_TRACE")=="1";
    private static long _nextId,_frame=-1,_dropped;
    private static Timer? _runtimeTimer;
    private static readonly List<object> RuntimeSamples=new();
    private sealed record Entry(string Name,string Phase,long Task,long Revision,long Frame,int Thread,long Start,long Duration);
    public static bool Enabled=>Volatile.Read(ref _enabled);
    public static void Initialize()
    {
        if(DirectoryPath is null)return;
        Directory.CreateDirectory(DirectoryPath!);
        File.WriteAllText(Path.Combine(DirectoryPath!,"ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,originUtc=OriginUtc,originTimestamp=Origin,frequency=Stopwatch.Frequency}));
        Mark("session","ready");
        if(Enabled){_ = RuntimeCounterEvents.Log;_runtimeTimer=new Timer(_=>SampleRuntime(),null,0,1000);}
        if(Environment.GetEnvironmentVariable("NODWARF_PROFILE_HANDSHAKE")=="1")
        {
            var wait=Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(DirectoryPath!,"collectors-ready")))
            {
                if(wait.Elapsed.TotalSeconds>25)throw new TimeoutException("Profiling collectors did not attach");
                Thread.Sleep(50); // Diagnostic startup barrier, before gameplay/measurement.
            }
        }
    }
    private static void SampleRuntime()
    {
        if(!Enabled)return;
        using var process=Process.GetCurrentProcess();
        var sample=new{atMs=Stopwatch.GetElapsedTime(Origin).TotalMilliseconds,
            allocatedBytes=GC.GetTotalAllocatedBytes(false),heapBytes=GC.GetTotalMemory(false),
            gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),
            gcPauseTotalMs=GC.GetTotalPauseDuration().TotalMilliseconds,
            threadPoolThreads=ThreadPool.ThreadCount,threadPoolPending=ThreadPool.PendingWorkItemCount,
            threadPoolCompleted=ThreadPool.CompletedWorkItemCount,lockContentions=Monitor.LockContentionCount,
            processCpuMs=process.TotalProcessorTime.TotalMilliseconds,workingSetBytes=process.WorkingSet64};
        lock(Gate){if(_enabled&&RuntimeSamples.Count<3600)RuntimeSamples.Add(sample);}
    }
    public static string OutputPath(string fallback)=>DirectoryPath is null?fallback:Path.Combine(DirectoryPath,Path.GetFileName(fallback));
    public static async Task FinishCaptureAsync()
    {
        if(DirectoryPath is null||Environment.GetEnvironmentVariable("NODWARF_PROFILE_HANDSHAKE")!="1")return;
        // Godot can terminate the embedded runtime without an EventPipe rundown.
        // Keep it alive outside measurement until collectors have flushed.
        File.WriteAllText(Path.Combine(DirectoryPath,"finished.json"),"{}");
        var wait=Stopwatch.StartNew();
        while(!File.Exists(Path.Combine(DirectoryPath,"collectors-stopped")))
        {
            if(wait.Elapsed.TotalSeconds>30)throw new TimeoutException("Profiling collectors did not acknowledge shutdown");
            await Task.Delay(50);
        }
    }
    public static void Frame(long frame){if(Enabled)Interlocked.Exchange(ref _frame,frame);}
    public static long Queued(string category,long revision)
    {
        if(!Enabled)return 0;
        long id=Interlocked.Increment(ref _nextId);Mark(category,"queued",id,revision);return id;
    }
    public static void Mark(string name,string phase,long task=0,long revision=0)
    {
        if(!Enabled)return;
        Add(new(name,phase,task,revision,Interlocked.Read(ref _frame),Environment.CurrentManagedThreadId,Stopwatch.GetTimestamp(),0));
        DiagnosticEvents.Log.Marker(name,phase,task,revision);
    }
    public static Scope Span(string name,long task=0,long revision=0)=>Enabled?new(name,task,revision):default;
    private static void Add(Entry entry)
    {
        lock(Gate)
        {
            if(!_enabled)return;
            if(Entries.Count<Limit)Entries.Add(entry);else _dropped++;
        }
    }
    public readonly struct Scope : IDisposable
    {
        private readonly string? _name;
        private readonly long _task,_revision,_frame,_start;
        private readonly int _thread;
        internal Scope(string name,long task,long revision)
        {_name=name;_task=task;_revision=revision;_frame=Interlocked.Read(ref DiagnosticTrace._frame);_start=Stopwatch.GetTimestamp();_thread=Environment.CurrentManagedThreadId;}
        public void Dispose()
        {
            if(_name is not null&&Enabled)Add(new(_name,"span",_task,_revision,_frame,_thread,_start,Stopwatch.GetTimestamp()-_start));
        }
    }
    public static void Save()
    {
        _runtimeTimer?.Dispose();
        Entry[] entries;long dropped;object[] runtime;
        lock(Gate)
        {
            if(!_enabled)return;
            Volatile.Write(ref _enabled,false);entries=Entries.ToArray();dropped=_dropped;Entries.Clear();runtime=RuntimeSamples.ToArray();RuntimeSamples.Clear();
        }
        double Us(long ticks)=>ticks*1_000_000d/Stopwatch.Frequency;
        var events=entries.OrderBy(e=>e.Start).Select(e=>(object)new{name=e.Name,cat="NoDwarf",ph=e.Phase=="span"?"X":"i",s="t",
            ts=Us(e.Start-Origin),dur=Us(e.Duration),pid=Environment.ProcessId,tid=e.Thread,
            args=new{phase=e.Phase,task=e.Task,revision=e.Revision,frame=e.Frame}}).ToList();
        foreach(int thread in entries.Select(e=>e.Thread).Distinct())
            events.Add(new{name="thread_name",ph="M",pid=Environment.ProcessId,tid=thread,args=new{name=$"Managed thread {thread}"}});
        File.WriteAllText(Path.Combine(DirectoryPath!,"timeline.json"),JsonSerializer.Serialize(new{traceEvents=events,displayTimeUnit="ms",
            metadata=new{originUtc=OriginUtc,originTimestamp=Origin,frequency=Stopwatch.Frequency,droppedEvents=dropped,limit=Limit,
                note="Managed thread IDs; spans are elapsed wall time, not CPU samples. Tasks may be unfinished at capture end. Frame is the main-frame counter observed at event time."}}));
        File.WriteAllText(Path.Combine(DirectoryPath!,"runtime.json"),JsonSerializer.Serialize(new{source="In-process .NET public runtime APIs, sampled once per second on a diagnostic timer; cumulative counters, not individual pause/lock events.",samples=runtime}));
    }
}

[EventSource(Name="NoDwarf-Performance")]
internal sealed class DiagnosticEvents : EventSource
{
    public static readonly DiagnosticEvents Log=new();
    [Event(1,Level=EventLevel.Informational)]
    public void Marker(string name,string phase,long task,long revision)
    {if(IsEnabled())WriteEvent(1,name,phase,task,revision);}
}

// Embedded hosts do not always expose System.Runtime counters. These use public
// runtime APIs and are explicitly named so they cannot be confused with that provider.
[EventSource(Name="NoDwarf-Runtime")]
internal sealed class RuntimeCounterEvents : EventSource
{
    public static readonly RuntimeCounterEvents Log=new();
    private readonly PollingCounter[] _counters;
    private RuntimeCounterEvents()
    {
        _counters=new[]{
            new PollingCounter("allocated-bytes-total",this,()=>GC.GetTotalAllocatedBytes(false)),
            new PollingCounter("heap-bytes",this,()=>GC.GetTotalMemory(false)),
            new PollingCounter("gc-pause-ms-total",this,()=>GC.GetTotalPauseDuration().TotalMilliseconds),
            new PollingCounter("gc0-total",this,()=>GC.CollectionCount(0)),
            new PollingCounter("gc1-total",this,()=>GC.CollectionCount(1)),
            new PollingCounter("gc2-total",this,()=>GC.CollectionCount(2)),
            new PollingCounter("pool-threads",this,()=>ThreadPool.ThreadCount),
            new PollingCounter("pool-pending",this,()=>ThreadPool.PendingWorkItemCount),
            new PollingCounter("lock-contentions-total",this,()=>Monitor.LockContentionCount)};
        foreach(var counter in _counters)counter.DisplayName=counter.Name;
    }
}
