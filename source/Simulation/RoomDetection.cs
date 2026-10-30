namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    private sealed class RoomRegion
    {
        public readonly HashSet<Cell> Cells = new();
        public bool Outside;
        public int Level, MinX, MaxX, MinZ, MaxZ;
        public RoomEnvelope? Envelope;
        public RoomHeat? Heat;
    }
    private readonly List<RoomRegion> _roomRegions = new();
    private readonly Stack<Cell> _roomFrontier = new();

    private void InvalidateRooms(Cell changed)
    {
        // Include the boundary, floor and headroom dependencies of every visited cell.
        // An outdoor result is a connected witness to an opening, not a whole-map flood.
        int removed=_roomRegions.RemoveAll(r => changed.Level >= r.Level - 1 &&
            changed.X >= r.MinX - 1 && changed.X <= r.MaxX + 1 &&
            changed.Z >= r.MinZ - 1 && changed.Z <= r.MaxZ + 1);
        // A cached outdoor witness may become a new neighbouring room. Also watch the
        // bounded wall scan, where excavating solid rock can expose an uncached room.
        if(removed>0||_roomHeat.Any(h=>changed.Level>=h.Region.Level-1&&
            changed.X>=h.Region.MinX-2&&changed.X<=h.Region.MaxX+2&&
            changed.Z>=h.Region.MinZ-2&&changed.Z<=h.Region.MaxZ+2))_roomHeatLinksDirty=true;
    }

    private void InvalidateRoomBatch(IReadOnlyCollection<Cell> changed)
    {
        bool Near(Cell c,RoomRegion r,int margin)=>c.Level>=r.Level-1&&c.X>=r.MinX-margin&&c.X<=r.MaxX+margin&&c.Z>=r.MinZ-margin&&c.Z<=r.MaxZ+margin;
        int removed=_roomRegions.RemoveAll(r=>changed.Any(c=>Near(c,r,1)));
        if(removed>0||_roomHeat.Any(h=>changed.Any(c=>Near(c,h.Region,2))))_roomHeatLinksDirty=true;
    }
    private RoomRegion FindRoomRegion(Cell start)
    {
        FlushBlockChanges();
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
