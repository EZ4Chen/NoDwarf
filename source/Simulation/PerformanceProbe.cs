using System.Diagnostics;

namespace NoDwarf.Simulation;

// Opt-in, synchronous diagnostic scopes. Exclusive time/allocation subtract nested
// instrumented work; measurements include GC pauses but do not identify their cause.
public sealed class PerformanceProbe
{
    internal sealed class Entry
    {
        public long Calls, InclusiveTicks, ExclusiveTicks, ExclusiveBytes, MaxTicks;
    }
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Dictionary<string, long> _counts = new();
    private long _recordedTicks, _recordedBytes;
    public int ManagedThreadId { get; } = Environment.CurrentManagedThreadId;
    public Scope Measure(string name)
    {
        if (Environment.CurrentManagedThreadId != ManagedThreadId)
            throw new InvalidOperationException("Use one performance probe per thread");
        if (!_entries.TryGetValue(name, out var entry)) _entries[name] = entry = new Entry();
        return new Scope(this, entry, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(), _recordedTicks, _recordedBytes);
    }
    public void Count(string name, long count = 1) => _counts[name] = _counts.GetValueOrDefault(name) + count;
    public object Report() => new
    {
        managedThreadId = ManagedThreadId,
        counters = _counts,
        timings = _entries.OrderByDescending(p => p.Value.ExclusiveTicks).Select(p => new
        {
            name = p.Key, calls = p.Value.Calls,
            inclusiveMs = p.Value.InclusiveTicks * 1000d / Stopwatch.Frequency,
            exclusiveMs = p.Value.ExclusiveTicks * 1000d / Stopwatch.Frequency,
            exclusiveAllocatedMiB = p.Value.ExclusiveBytes / 1048576d,
            maxInclusiveMs = p.Value.MaxTicks * 1000d / Stopwatch.Frequency
        }).ToArray()
    };

    public readonly struct Scope : IDisposable
    {
        private readonly PerformanceProbe? _probe;
        private readonly Entry? _entry;
        private readonly long _start, _bytes, _priorTicks, _priorBytes;
        internal Scope(PerformanceProbe probe, Entry entry, long start, long bytes, long priorTicks, long priorBytes)
        { _probe = probe; _entry = entry; _start = start; _bytes = bytes; _priorTicks = priorTicks; _priorBytes = priorBytes; }
        public void Dispose()
        {
            if (_probe is null || _entry is null) return;
            long inclusive = Stopwatch.GetTimestamp() - _start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
            long exclusive = Math.Max(0, inclusive - (_probe._recordedTicks - _priorTicks));
            long exclusiveBytes = Math.Max(0, bytes - (_probe._recordedBytes - _priorBytes));
            _probe._recordedTicks += exclusive; _probe._recordedBytes += exclusiveBytes;
            _entry.Calls++; _entry.InclusiveTicks += inclusive; _entry.ExclusiveTicks += exclusive;
            _entry.ExclusiveBytes += exclusiveBytes; _entry.MaxTicks = Math.Max(_entry.MaxTicks, inclusive);
        }
    }
}
