namespace NoDwarf.Simulation;
public static class TickRateChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("TICK_RATE: "+message);}
    public static void Run()
    {
        foreach(int speed in new[]{1,2,3})
        {
            var c=new SimulationClock();c.SetSpeed(speed);
            Check(c.TargetTicksPerSecond==60*speed&&c.Advance(1)==60*speed,"60/120/180 target throughput");
            Check(c.GameSeconds==speed,"speed changes throughput, not time per tick");
            var frames=new SimulationClock();frames.SetSpeed(speed);for(int i=0;i<144;i++)frames.Advance(1d/144);
            Check(frames.Tick==c.Tick,"independent of render frame rate");
        }
        var restored=new SimulationClock();restored.Restore(600,3,true,.012,60);
        Check(restored.Tick==600&&restored.GameSeconds==10&&restored.PendingSeconds==.012&&restored.Advance(2)==0,"current clock roundtrip; pause freezes");
        restored.Step();Check(restored.Tick==601,"single-step is one 60 Hz tick");
        var backlog=new SimulationClock();backlog.Advance(10);Check(backlog.PendingSeconds>4.9,"bounded catchup retains backlog");
        backlog.Advance(0);Check(backlog.Tick==600&&backlog.PendingSeconds<1e-8,"catchup doesn't drop time");
        bool rejected=false;try{restored.Restore(300,1,false,0,30);}catch(ArgumentException){rejected=true;}Check(rejected,"outdated tick rate rejected");
        var w=new DemoWorld(flat:true);w.DeveloperResidents(false);var clock=new SimulationClock();
        for(int frame=0;frame<240;frame++)for(int tick=0,count=clock.Advance(1d/120);tick<count;tick++)w.Update(SimulationClock.TickSeconds);
        Check(Math.Abs(w.SimulationTime-2)<1e-9,"world time unchanged at 60 Hz");
        Check(DemoWorld.LoadJson(w.SaveJson()).Calendar==w.Calendar,"calendar survives roundtrip");
        Console.WriteLine("TICK_RATE_OK: 60/120/180 TPS, frame independence, pause/step/backlog, outdated rate rejection, world time");
    }
}
