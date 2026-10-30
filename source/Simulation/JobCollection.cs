using System.Collections;
namespace NoDwarf.Simulation;

// Job identity and ordering fields are init-only. All membership mutations go
// through this collection, including editor/test setup and save restoration.
public sealed class JobCollection : IReadOnlyList<DemoJob>
{
    private readonly List<DemoJob> _items=new();
    private readonly Dictionary<Cell,List<DemoJob>> _byCell=new();
    public IReadOnlyList<DemoJob> At(Cell cell)=>_byCell.TryGetValue(cell,out var jobs)?jobs:Array.Empty<DemoJob>();
    private void Index(DemoJob job){if(!_byCell.TryGetValue(job.Cell,out var list))_byCell[job.Cell]=list=new();list.Add(job);}
    private void Unindex(DemoJob job){var list=_byCell[job.Cell];list.Remove(job);if(list.Count==0)_byCell.Remove(job.Cell);}
    private readonly Action<DemoJob>? _prepare;
    public JobCollection(Action<DemoJob>? prepare=null)=>_prepare=prepare;
    public long Version {get;private set;}
    public int Count=>_items.Count;
    public DemoJob this[int index]
    {
        get=>_items[index];
        set {ArgumentNullException.ThrowIfNull(value);if(ReferenceEquals(_items[index],value))return;_prepare?.Invoke(value);Unindex(_items[index]);_items[index]=value;Index(value);Version++;}
    }
    public void Add(DemoJob job){ArgumentNullException.ThrowIfNull(job);_prepare?.Invoke(job);_items.Add(job);Index(job);Version++;}
    public bool Remove(DemoJob job){if(!_items.Remove(job))return false;Unindex(job);Version++;return true;}
    public void Clear(){if(_items.Count==0)return;_items.Clear();_byCell.Clear();Version++;}
    public bool Contains(DemoJob job)=>_items.Contains(job);
    public DemoJob[] ToArray()=>_items.ToArray();
    public List<DemoJob>.Enumerator GetEnumerator()=>_items.GetEnumerator();
    IEnumerator<DemoJob> IEnumerable<DemoJob>.GetEnumerator()=>GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
}
