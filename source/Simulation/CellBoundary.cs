namespace NoDwarf.Simulation;
// Horizontal perimeter bits: west, east, north, south. Cell includes height.
public static class CellBoundary
{
    public static readonly Cell[] Neighbors={new(-1,0),new(1,0),new(0,-1),new(0,1)};
    public static int Mask(Cell cell,Func<Cell,bool> sameRegion)
    {
        int mask=0;for(int side=0;side<4;side++)if(!sameRegion(cell+Neighbors[side]))mask|=1<<side;return mask;
    }
}
