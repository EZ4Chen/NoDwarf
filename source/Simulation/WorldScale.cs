namespace NoDwarf.Simulation;

public static class WorldScale
{
    public const float BlockMeters = 1f;
    public const float DwarfHeightMeters = 1.1f;
}

// Navigation bookmarks over a foundation footprint; they do not create floors or excavate terrain.
public sealed class FoundationPlan
{
    public Cell Min { get; }
    public Cell Max { get; }
    public int FloorHeight { get; private set; } = 3;
    public int Floor { get; private set; }
    public int WorldLevel => Min.Level + Floor * FloorHeight;
    public FoundationPlan(Cell a, Cell b)
    {
        if (a.Level != b.Level) throw new ArgumentException("Foundation corners must share a height");
        Min = new Cell(Math.Min(a.X, b.X), Math.Min(a.Z, b.Z), a.Level);
        Max = new Cell(Math.Max(a.X, b.X), Math.Max(a.Z, b.Z), a.Level);
    }
    public bool Contains(Cell c) => c.X >= Min.X && c.X <= Max.X && c.Z >= Min.Z && c.Z <= Max.Z;
    public void SetFloorHeight(int meters)
    {
        if (meters < 3 || meters > 12) throw new ArgumentOutOfRangeException(nameof(meters));
        FloorHeight = meters;
    }
    public void Step(int direction) => Floor = Math.Clamp(Floor + direction, -128, 128);
}
