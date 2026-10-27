namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    private sealed class RoomRegion
    {
        public readonly HashSet<Cell> Cells = new();
        public bool Outside;
        public int Level, MinX, MaxX, MinZ, MaxZ;
    }
    private readonly List<RoomRegion> _roomRegions = new();
    private readonly Stack<Cell> _roomFrontier = new();

    private void InvalidateRooms(Cell changed)
    {
        // Include the boundary, floor and headroom dependencies of every visited cell.
        // An outdoor result is a connected witness to an opening, not a whole-map flood.
        _roomRegions.RemoveAll(r => Math.Abs(changed.Level - r.Level) <= 1 &&
            changed.X >= r.MinX - 1 && changed.X <= r.MaxX + 1 &&
            changed.Z >= r.MinZ - 1 && changed.Z <= r.MaxZ + 1);
    }

    private RoomRegion FindRoomRegion(Cell start)
    {
        foreach (var cached in _roomRegions)
            if (cached.Cells.Contains(start)) { Probe?.Count("rooms.cache-hits"); return cached; }
        var region = new RoomRegion { Level = start.Level, MinX = start.X, MaxX = start.X, MinZ = start.Z, MaxZ = start.Z };
        _roomFrontier.Clear(); _roomFrontier.Push(start); region.Cells.Add(start);
        // Depth-first traversal finds an outdoor escape without flooding the open map.
        // Enclosed regions are still explored completely; there is no size/time cutoff.
        while (_roomFrontier.TryPop(out var c))
        {
            if (c.X == 0 || c.X == Width - 1 || c.Z == 0 || c.Z == Depth - 1)
            { region.Outside = true; break; }
            foreach (var d in Directions)
            {
                var next = c + d;
                if (!Inside(next)) continue;
                if (!Solid(next + new Cell(0,0,-1)) || NaturalRamp(next))
                { region.Outside = true; break; }
                if (Boundary(next) || NaturalSolid(next + new Cell(0,0,1))) continue;
                if (!region.Cells.Add(next)) continue;
                region.MinX = Math.Min(region.MinX, next.X); region.MaxX = Math.Max(region.MaxX, next.X);
                region.MinZ = Math.Min(region.MinZ, next.Z); region.MaxZ = Math.Max(region.MaxZ, next.Z);
                _roomFrontier.Push(next);
            }
            if (region.Outside) break;
        }
        _roomFrontier.Clear();
        Probe?.Count("rooms.visited-cells", region.Cells.Count);
        _roomRegions.Add(region);
        return region;
    }
}
