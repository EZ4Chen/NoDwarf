using NoDwarf.Simulation;
using System.Threading;
namespace NoDwarf;

// Immutable after capture. No live DemoWorld, scene nodes, resources or callbacks.
internal sealed record InteriorOcclusionInput(TerrainMap Terrain,HashSet<Cell> Walls,HashSet<Cell> Doors,Cell[] BuildingCells)
{
    public bool Solid(Cell c)=>Terrain.Solid(c)||Walls.Contains(c);
}
internal sealed record InteriorTopology(IReadOnlyList<Cell> Cells,bool[] Membership,int Visits);
internal sealed class InteriorOcclusionScratch
{
    public readonly bool[] Columns=new bool[TerrainMap.Size*TerrainMap.Size];
    public readonly List<int> Touched=new();
}
internal static class InteriorOcclusionBuilder
{
    private static readonly Cell[] Directions={new(1,0),new(-1,0),new(0,1),new(0,-1)};
    public static InteriorTopology Rooms(InteriorOcclusionInput input,int floor,CancellationToken token=default)
    {
        bool Open(Cell c)=>DemoWorld.Inside(c)&&!input.Terrain.Solid(c)&&!input.Terrain.Ramp(c)
            &&input.Solid(c+new Cell(0,0,-1))&&!input.Walls.Contains(c)&&!input.Doors.Contains(c);
        // 1 = current connected search, 2 = enclosed, 3 = connected outdoor witness.
        // A witness need not cover the entire exterior: every discovered cell is
        // connected to the escape, and later searches can stop when they reach it.
        var visited=new byte[TerrainMap.Size*TerrainMap.Size];var members=new bool[visited.Length];
        var cells=new List<Cell>();var component=new List<Cell>();var frontier=new Stack<Cell>();int visits=0;
        var seeds=input.Terrain.Excavated.Where(c=>c.Level==floor)
            .Concat(input.BuildingCells.Where(c=>c.Level==floor).SelectMany(c=>Directions.Select(d=>c+d)));
        foreach(var seed in seeds)
        {
            token.ThrowIfCancellationRequested();
            if(!DemoWorld.Inside(seed)||visited[seed.X*TerrainMap.Size+seed.Z]!=0||!Open(seed))continue;
            component.Clear();component.Add(seed);frontier.Clear();frontier.Push(seed);visited[seed.X*TerrainMap.Size+seed.Z]=1;
            bool enclosed=true;
            int processed=0;
            while(frontier.TryPop(out var c))
            {
                if((processed++&127)==0)token.ThrowIfCancellationRequested();
                foreach(var d in Directions)
                {
                    var n=c+d;if(!DemoWorld.Inside(n)){enclosed=false;break;}
                    int id=n.X*TerrainMap.Size+n.Z;
                    if(visited[id]==3){enclosed=false;break;}
                    if(visited[id]!=0)continue;
                    bool open=Open(n);
                    if(open){visited[id]=1;component.Add(n);frontier.Push(n);}
                    else if(!input.Solid(n)&&!input.Doors.Contains(n)){enclosed=false;break;}
                }
                if(!enclosed)break;
            }
            visits+=component.Count;
            foreach(var c in component)visited[c.X*TerrainMap.Size+c.Z]=(byte)(enclosed?2:3);
            if(!enclosed)continue;
            cells.AddRange(component);foreach(var c in component)members[c.X*TerrainMap.Size+c.Z]=true;
        }
        return new(cells,members,visits);
    }
    public static Dictionary<Cell,float> Caps(InteriorOcclusionInput input,InteriorTopology topology,int floor,int height,
        (int X,int Z)[] offsets,InteriorOcclusionScratch scratch,CancellationToken token=default)
    {
        foreach(int id in scratch.Touched)scratch.Columns[id]=false;
        scratch.Touched.Clear();int visited=0;
        foreach(var c in topology.Cells)
        {
            if((visited++&127)==0)token.ThrowIfCancellationRequested();
            foreach(var offset in offsets)
            {
                int x=c.X+offset.X,z=c.Z+offset.Z;
                if(x<0||z<0||x>=TerrainMap.Size||z>=TerrainMap.Size)continue;
                int id=x*TerrainMap.Size+z;
                if(!scratch.Columns[id]){scratch.Columns[id]=true;scratch.Touched.Add(id);}
            }
        }
        var caps=new Dictionary<Cell,float>();visited=0;
        foreach(int id in scratch.Touched)
        {
            if((visited++&127)==0)token.ThrowIfCancellationRequested();
            int x=id/TerrainMap.Size,z=id%TerrainMap.Size;
            bool rim=(x>0&&topology.Membership[id-TerrainMap.Size])||(x+1<TerrainMap.Size&&topology.Membership[id+TerrainMap.Size])
                ||(z>0&&topology.Membership[id-1])||(z+1<TerrainMap.Size&&topology.Membership[id+1]);
            for(int y=floor;y<floor+height-1;y++)
            {var block=new Cell(x,z,y);if(DemoWorld.Inside(block)&&input.Solid(block))caps[block]=floor+(y==floor&&rim?.2f:0f);}
        }
        return caps;
    }
}
