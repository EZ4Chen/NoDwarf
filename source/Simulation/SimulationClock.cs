namespace NoDwarf.Simulation;

// Pure simulation state. No scene nodes or Godot dependencies.
// Rendering is independent; speed changes tick throughput, never the fixed simulation step.
public sealed class SimulationClock
{
    public const int BaseTicksPerSecond = 60;
    public const double TickSeconds = 1d / BaseTicksPerSecond;
    public long Tick { get; private set; }
    public bool Paused { get; set; }
    public int Speed { get; private set; } = 1;
    public double PendingSeconds { get; private set; }
    public double GameSeconds => (double)Tick / BaseTicksPerSecond;
    public int TargetTicksPerSecond => Paused ? 0 : BaseTicksPerSecond * Speed;

    public void SetSpeed(int speed)
    {
        if (speed is <1 or >3) throw new ArgumentOutOfRangeException(nameof(speed));
        Speed = speed;
    }

    public int Advance(double realSeconds)
    {
        if (!double.IsFinite(realSeconds) || realSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(realSeconds));
        if (Paused) return 0;
        PendingSeconds += realSeconds * Speed;
        int count = 0;
        // Keep backlog rather than silently discarding simulation time.
        while (PendingSeconds + 1e-12 >= TickSeconds && count < 300)
        {
            PendingSeconds = Math.Max(0, PendingSeconds - TickSeconds);
            Tick++;
            count++;
        }
        return count;
    }

    public void Restore(long tick,int speed,bool paused,double pending,int savedTickRate=BaseTicksPerSecond)
    {
        if(tick<0||!double.IsFinite(pending)||pending<0)throw new ArgumentException("Invalid clock");
        if(savedTickRate!=BaseTicksPerSecond)throw new ArgumentException("Unsupported saved tick rate");
        SetSpeed(speed);Tick=tick;Paused=paused;PendingSeconds=pending;
    }
    public void Step()
    {
        if (Paused) Tick++;
    }
}
