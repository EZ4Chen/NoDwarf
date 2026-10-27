namespace NoDwarf.Simulation;

// The same visibility policy is used by terrain, placed blocks, people, selection and minimap.
// Air opened by excavation is discovered; untouched solid interiors are not x-ray visible.
public readonly record struct WorldView(int? Level, bool RevealAll = false, IReadOnlyDictionary<Cell,float>? WallCaps = null, int Stamp = 0)
{
    public float Top => Level is int level ? level + 1 : float.PositiveInfinity;
    public float CellTop(Cell c)=>WallCaps is not null && WallCaps.TryGetValue(c,out var cap)?Math.Min(Top,cap):Top;
    public bool InSlice(Cell c) => c.Level < CellTop(c);
    public bool Known(DemoWorld world, Cell c) => Known(world.Terrain,c);
    public bool Known(TerrainMap terrain, Cell c)
    {
        if(RevealAll) return true;
        bool Open(Cell p) => DemoWorld.Inside(p) && !terrain.Solid(p) && !terrain.Ramp(p);
        if(Open(c) || terrain.Ramp(c)) return true;
        foreach(var d in Neighbors) if(Open(c+d)) return true;
        return false;
    }
    // A diagonal rim is a silhouette, not discovery: never reveal its actual material or contents.
    public bool CornerRim(DemoWorld world,Cell c) => CornerRim(world.Terrain,c);
    public bool CornerRim(TerrainMap terrain,Cell c)
    {
        if(!terrain.Solid(c))return false;
        foreach(var delta in Diagonals)
        {
            var p=c+delta;
            if(DemoWorld.Inside(p)&&!terrain.Solid(p)&&!terrain.Ramp(p))return !Known(terrain,c);
        }
        return false;
    }
    public bool Visible(DemoWorld world, Cell c) => InSlice(c) && Known(world,c);
    private static readonly Cell[] Diagonals={new(1,1),new(1,-1),new(-1,1),new(-1,-1)};
    private static readonly Cell[] Neighbors = { new(1,0),new(-1,0),new(0,1),new(0,-1),new(0,0,1),new(0,0,-1) };
}
