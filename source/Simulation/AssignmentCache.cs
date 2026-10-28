namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    // Cache geometry, never ownership, safety or inventory decisions. Those are
    // checked live because an earlier resident can move or reserve stock this tick.
    private long _accessVersion = -1;
    private readonly Dictionary<DemoJob, (Cell Cell, Cell[] Positions)> _jobAccess = new();
    private readonly Dictionary<Cell, Cell[]> _itemAccess = new();
    private readonly Dictionary<Cell, DemoJob> _assignmentTargets = new();
    private readonly HashSet<Cell> _occupiedStands = new(), _occupiedJobs = new();
    private readonly HashSet<Cell> _residentFootCells = new();
    private DemoJob[]? _orderedJobs;
    private int _orderedRevision, _orderedCount;
    private bool PendingConstructionShell(DemoJob equipment)
    {
        foreach(var job in Jobs)
            if(job.Build is BuildKind kind&&(BlockCatalog.IsWall(kind)||kind==BuildKind.Door)&&SameConstructionSite(equipment,job))return true;
        return false;
    }
    private static Cell AssignmentDestination(DemoWorker worker)
    {
        var cell=worker.Tile;foreach(var step in worker.Path)cell=step;return cell;
    }
    private static bool MaterialsDelivered(DemoJob job)
    {
        foreach(var material in job.Materials)if(material.Stage!=2)return false;
        return true;
    }

    private void CheckAccessVersion()
    {
        if (_accessVersion == Navigation.Version) return;
        _accessVersion = Navigation.Version;
        _jobAccess.Clear(); _itemAccess.Clear();
    }
    private Cell[] JobAccess(DemoJob job)
    {
        CheckAccessVersion();
        if (_jobAccess.TryGetValue(job, out var cached) && cached.Cell == job.Cell) return cached.Positions;
        var result = (job.Haul ? ItemPositions(job.Cell) : ActionPositions(job)).Where(Walkable).ToArray();
        _jobAccess[job] = (job.Cell, result);
        return result;
    }
    private Cell[] ItemAccess(Cell cell)
    {
        CheckAccessVersion();
        if (!_itemAccess.TryGetValue(cell, out var result)) _itemAccess[cell] = result = ItemPositions(cell).Where(Walkable).ToArray();
        return result;
    }
    private bool CanReachAny(Cell from, Cell[] positions)
    {
        int component = Navigation.ComponentAt(from);
        if (component == 0) return false;
        foreach (var cell in positions) if (Navigation.CanReach(from,cell)) return true;
        return false;
    }
    private Cell[] AssignmentPositions(DemoJob job)
    {
        foreach (var material in job.Materials)
        {
            if (material.Stage == 2) continue;
            if (material.Stage != 0) break;
            foreach (var stack in Items) if (stack.Id == material.StackId) return ItemAccess(stack.Cell);
            return Array.Empty<Cell>();
        }
        return JobAccess(job);
    }
    private DemoJob[] OrderedJobs()
    {
        if (_orderedJobs is null || _orderedRevision != Revision || _orderedCount != Jobs.Count)
        {
            _orderedJobs = Jobs.OrderBy(j => j.Haul ? 2 : j.Production ? 1 : 0).ThenBy(j => j.Id).ToArray();
            _orderedRevision = Revision; _orderedCount = Jobs.Count;
        }
        return _orderedJobs;
    }
}
