namespace NoDwarf.Simulation;
public sealed class StorageZone
{
    public int Id {get;set;}
    public int Priority {get;set;}=3;
    public HashSet<string> Allowed {get;set;}=new(ItemCatalog.All.Select(i=>i.Id));
    public HashSet<Cell> Cells {get;set;}=new();
}
public sealed partial class DemoWorld
{
    public List<StorageZone> StorageZones {get;}=new();
    public StorageZone? StorageAt(Cell c)=>StorageZones.FirstOrDefault(z=>z.Cells.Contains(c));
    public StorageZone? EditableStorageAt(Cell c)
    {
        if(!Stockpiles.Contains(c))return null;
        if(StorageAt(c) is {} existing)return existing;
        var cells=new HashSet<Cell>{c};var queue=new Queue<Cell>();queue.Enqueue(c);
        while(queue.TryDequeue(out var p))foreach(var d in Directions){var n=p+d;if(Stockpiles.Contains(n)&&StorageAt(n) is null&&cells.Add(n))queue.Enqueue(n);}
        DefineStorageZone(cells);Revision++;return StorageAt(c);
    }
    public int StoragePriority(Cell c,string item)=>!Stockpiles.Contains(c)?0:StorageAt(c) is { } zone?(zone.Allowed.Contains(item)?zone.Priority:0):3;
    private void DefineStorageZone(IEnumerable<Cell> cells)
    {
        var selected=cells.ToHashSet();if(selected.Count==0)return;
        foreach(var zone in StorageZones)zone.Cells.ExceptWith(selected);
        StorageZones.RemoveAll(z=>z.Cells.Count==0);
        StorageZones.Add(new(){Id=StorageZones.Select(z=>z.Id).DefaultIfEmpty().Max()+1,Cells=selected});
    }
    public bool ConfigureStorage(int id,int priority,IEnumerable<string> items)
    {
        var zone=StorageZones.FirstOrDefault(z=>z.Id==id);if(zone is null||priority is <1 or >5)return false;
        var allowed=items.ToHashSet();if(allowed.Any(i=>!ItemCatalog.All.Any(d=>d.Id==i)))return false;
        zone.Priority=priority;zone.Allowed=allowed;
        foreach(var job in Jobs.Where(j=>j.Haul&&zone.Cells.Contains(j.Cell)&&j.Materials.Any(r=>!allowed.Contains(r.Item))).ToArray())CancelJob(job);
        Revision++;return true;
    }
}
