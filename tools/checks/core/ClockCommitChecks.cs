using NoDwarf.Simulation;

internal static class ClockCommitChecks
{
    public static void Run()
    {
        var clock=new SimulationClock();int completed=0;
        try
        {
            clock.Advance(.1,()=>{if(completed==2)throw new IOException("tick failure");completed++;});
            throw new Exception("Expected tick failure");
        }
        catch(IOException){}
        if(clock.Tick!=2||completed!=2||Math.Abs(clock.PendingSeconds-4*SimulationClock.TickSeconds)>1e-9)
            throw new Exception("Failed batch committed unfinished ticks");
        clock.Paused=true;
        if(clock.Advance(1,()=>throw new Exception("Paused clock ran"))!=0)throw new Exception("Pause advanced time");
        try{clock.Step(()=>throw new IOException("step failure"));throw new Exception("Expected step failure");}catch(IOException){}
        if(clock.Tick!=2)throw new Exception("Failed single step committed time");
        clock.Step(()=>completed++);
        if(clock.Tick!=3||completed!=3)throw new Exception("Successful single step was not committed");
        var restored=new SimulationClock();restored.Restore(clock.Tick,clock.Speed,false,clock.PendingSeconds);
        restored.Advance(0,()=>completed++);
        if(restored.Tick!=7||completed!=7)throw new Exception("Restored pending ticks lost or duplicated time");
        Console.WriteLine("CLOCK_COMMIT_OK: successful ticks only, failed batch/step, pause and pending-time roundtrip");
    }
}
