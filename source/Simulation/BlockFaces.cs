namespace NoDwarf.Simulation;

[Flags]
public enum BlockFaces { None=0,Top=1,Bottom=2,North=4,South=8,East=16,West=32,All=63 }

// Omit only completely covered faces. Partial stairs/ramps and cutout leaves
// must not be treated as opaque cubes. A view cut exposes a new cap immediately.
public static class BlockExposure
{
    public static readonly (BlockFaces Face,Cell Delta)[] Neighbors={
        (BlockFaces.Top,new(0,0,1)),(BlockFaces.Bottom,new(0,0,-1)),
        (BlockFaces.North,new(0,-1)),(BlockFaces.South,new(0,1)),
        (BlockFaces.East,new(1,0)),(BlockFaces.West,new(-1,0))};
    public static BlockFaces Faces(Cell cell,float top,Func<Cell,float> opaqueTop)
    {
        if(top<=cell.Level)return BlockFaces.None;
        var faces=BlockFaces.All;
        foreach(var (face,delta) in Neighbors)
        {
            float neighborTop=opaqueTop(cell+delta);
            bool covered=face==BlockFaces.Top ? top>=cell.Level+1&&neighborTop>top
                :face==BlockFaces.Bottom ? neighborTop>=cell.Level
                :neighborTop>=top;
            if(covered)faces&=~face;
        }
        return faces;
    }
    private static float OpaqueTop(DemoWorld world,WorldView view,Cell cell)
    {
        if(!view.InSlice(cell))return float.NegativeInfinity;
        bool solid=world.Terrain.Solid(cell)&&!world.Terrain.Ramp(cell);
        if(world.Buildings.TryGetValue(cell,out var b)&&BlockCatalog.IsWall(b.Kind)&&view.Visible(world,cell))solid=true;
        return solid?Math.Min(cell.Level+1,view.CellTop(cell)):float.NegativeInfinity;
    }
    public static BlockFaces BuildingFaces(DemoWorld world,WorldView view,Cell cell)
        =>Faces(cell,Math.Min(cell.Level+1,view.CellTop(cell)),neighbor=>OpaqueTop(world,view,neighbor));

    // Plans only hide other plan faces. They never change solid geometry or hide a real block's face.
    public static BlockFaces PlanFaces(DemoWorld world,WorldView view,Cell cell,IReadOnlySet<Cell> solidPlans)
        =>Faces(cell,Math.Min(cell.Level+1,view.CellTop(cell)),neighbor=>
            solidPlans.Contains(neighbor)&&view.Visible(world,neighbor)
                ?Math.Max(OpaqueTop(world,view,neighbor),Math.Min(neighbor.Level+1,view.CellTop(neighbor)))
                :OpaqueTop(world,view,neighbor));
}
