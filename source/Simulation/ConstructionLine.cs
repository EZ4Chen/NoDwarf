namespace NoDwarf.Simulation;

public static class ConstructionLine
{
    // Lock height to the starting cell and snap to the dominant horizontal axis.
    // Inclusive, ordered from the press point so scarce material goes there first.
    public static Cell[] Cells(Cell from,Cell to)
    {
        if(!DemoWorld.Inside(from))return Array.Empty<Cell>();
        bool alongX=Math.Abs((long)to.X-from.X)>=Math.Abs((long)to.Z-from.Z);
        int end=alongX?Math.Clamp(to.X,0,DemoWorld.Width-1):Math.Clamp(to.Z,0,DemoWorld.Depth-1);
        int start=alongX?from.X:from.Z,step=Math.Sign(end-start);
        return Enumerable.Range(0,Math.Abs(end-start)+1).Select(i=>alongX
            ?new Cell(from.X+i*step,from.Z,from.Level):new Cell(from.X,from.Z+i*step,from.Level)).ToArray();
    }
}
