using NoDwarf.Simulation;
using System.Text.Json;

internal static class DiagnosticTraceChecks
{
    public static void Run()
    {
        DiagnosticTrace.Initialize();
        if(!DiagnosticTrace.Enabled)
        {
            if(DiagnosticTrace.Queued("disabled",1)!=0)throw new Exception("Disabled trace allocated task identity");
            DiagnosticTrace.Save();Console.WriteLine("TRACE_DISABLED_OK");return;
        }
        Task.WaitAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>
        {
            for(int i=0;i<100;i++)
            {
                long id=DiagnosticTrace.Queued("check",i);
                DiagnosticTrace.Mark("check","started",id,i);
                using var span=DiagnosticTrace.Span("check.compute",id,i);
                DiagnosticTrace.Mark("check","computed",id,i);
            }
        })).ToArray());
        for(int i=0;i<200_100;i++)DiagnosticTrace.Mark("overflow","check");
        DiagnosticTrace.Save();
        var path=DiagnosticTrace.OutputPath("timeline.json");var bytes=File.ReadAllBytes(path);
        // Late workers and repeated saves must not append to a completed session.
        Task.Run(()=>DiagnosticTrace.Mark("late","write")).Wait();DiagnosticTrace.Save();
        if(!bytes.SequenceEqual(File.ReadAllBytes(path)))throw new Exception("Late writes changed completed trace");
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        if(root.GetProperty("metadata").GetProperty("droppedEvents").GetInt64()==0)throw new Exception("Capture was not bounded");
        var actual=root.GetProperty("traceEvents").EnumerateArray().Where(e=>e.TryGetProperty("cat",out _)).ToArray();
        if(actual.Length!=200_000)throw new Exception("Unexpected capture bound");
        var queued=actual.Where(e=>e.GetProperty("name").GetString()=="check"&&e.GetProperty("args").GetProperty("phase").GetString()=="queued").ToArray();
        if(queued.Select(e=>e.GetProperty("args").GetProperty("task").GetInt64()).Distinct().Count()!=400)throw new Exception("Task IDs collided across threads");
        if(actual.Any(e=>e.GetProperty("dur").GetDouble()<0))throw new Exception("Negative span");
        Console.WriteLine("TRACE_CONCURRENCY_OK: unique tasks, bounded capture, late-write isolation");
    }
}
