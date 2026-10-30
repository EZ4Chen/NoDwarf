namespace NoDwarf.Simulation;

// Indexed deadlines: replacement/cancellation physically removes old entries, so no stale-queue growth.
public sealed class ScheduledUpdates<TKey> where TKey:notnull
{
    private sealed record Entry(TKey Key,double Due,long Serial);
    private readonly SortedSet<Entry> _due;
    public ScheduledUpdates(IComparer<TKey>? order=null)
    {
        _due=new(Comparer<Entry>.Create((a,b)=>{int c=a.Due.CompareTo(b.Due);if(c!=0)return c;return order is not null?order.Compare(a.Key,b.Key):a.Serial.CompareTo(b.Serial);}));
    }
    private readonly Dictionary<TKey,Entry> _entries=new();
    private long _serial;
    public int Count=>_entries.Count;
    public void Schedule(TKey key,double due)
    {
        if(!double.IsFinite(due)||due<0)throw new ArgumentOutOfRangeException(nameof(due));
        Cancel(key);var entry=new Entry(key,due,checked(++_serial));_entries.Add(key,entry);_due.Add(entry);
    }
    public bool Cancel(TKey key){if(!_entries.Remove(key,out var entry))return false;_due.Remove(entry);return true;}
    public bool TryTake(double now,out TKey key)
    {
        if(_due.Min is not {} entry||entry.Due>now+1e-9){key=default!;return false;}
        _due.Remove(entry);_entries.Remove(entry.Key);key=entry.Key;return true;
    }
    public void Clear(){_entries.Clear();_due.Clear();_serial=0;}
}

public static class CellUpdateOrder
{
    public static readonly IComparer<Cell> Instance=Comparer<Cell>.Create((a,b)=>
    {int c=a.Level.CompareTo(b.Level);if(c!=0)return c;c=a.X.CompareTo(b.X);return c!=0?c:a.Z.CompareTo(b.Z);});
}
