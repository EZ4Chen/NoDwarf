namespace NoDwarf.Collections;

// Owner-thread cache. A lease pins a value until Release, so eviction cannot
// dispose a resource still in use by a renderer. Pinned bytes may exceed budget.
public sealed class ByteBudgetCache<TKey,TValue> where TKey:notnull
{
    private sealed class Entry(TKey key,TValue value,long bytes)
    {
        public readonly TKey Key=key;
        public readonly TValue Value=value;
        public readonly long Bytes=bytes;
        public int Pins=1;
    }
    private readonly Dictionary<TKey,LinkedListNode<Entry>> _entries=new();
    private readonly LinkedList<Entry> _unused=new();
    private readonly Action<TValue> _dispose;
    private long _budget;
    public ByteBudgetCache(long budget,Action<TValue> dispose){ArgumentOutOfRangeException.ThrowIfNegative(budget);_budget=budget;_dispose=dispose;}
    public long Budget {get=>_budget;set{ArgumentOutOfRangeException.ThrowIfNegative(value);_budget=value;Trim();}}
    public long Bytes {get;private set;}
    public long Hits {get;private set;}
    public long Misses {get;private set;}
    public long Evictions {get;private set;}
    public int Count=>_entries.Count;
    public long PinnedBytes=>_entries.Values.Where(n=>n.Value.Pins>0).Sum(n=>n.Value.Bytes);
    public TValue Acquire(TKey key,Func<(TValue Value,long Bytes)> create)
    {
        if(TryAcquire(key,out var found))return found;
        var (value,bytes)=create();ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        Misses++;var node=new LinkedListNode<Entry>(new Entry(key,value,bytes));_entries.Add(key,node);Bytes+=bytes;Trim();return value;
    }
    public bool TryAcquire(TKey key,out TValue value)
    {
        if(_entries.TryGetValue(key,out var node))
        {
            Hits++;if(node.Value.Pins++==0)_unused.Remove(node);value=node.Value.Value;return true;
        }
        value=default!;return false;
    }
    public void Release(TKey key)
    {
        if(!_entries.TryGetValue(key,out var node)||node.Value.Pins==0)throw new InvalidOperationException("Cache lease was already released");
        if(--node.Value.Pins==0)_unused.AddLast(node);
        Trim();
    }
    private void Trim()
    {
        while(Bytes>_budget&&_unused.First is {} node)
        {
            _unused.RemoveFirst();_entries.Remove(node.Value.Key);Bytes-=node.Value.Bytes;Evictions++;_dispose(node.Value.Value);
        }
    }
}
