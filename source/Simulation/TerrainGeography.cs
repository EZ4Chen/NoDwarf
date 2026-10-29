namespace NoDwarf.Simulation;

public readonly record struct RiverBoundary(Cell Cell,bool Inlet);
public sealed partial class TerrainMap
{
    private readonly HashSet<Cell> _caverns=new();
    private readonly HashSet<Cell> _knownCaverns=new();
    public bool KnownAir(Cell c)=>!_caverns.Contains(c)||_knownCaverns.Contains(c);
    public IEnumerable<Cell> DiscoveredCavernFloors=>CavernFloors.Where(_knownCaverns.Contains);
    private void DiscoverCaverns(Cell opening)
    {
        var queue=new Queue<Cell>();var found=new List<Cell>();
        void Visit(Cell c){if(_caverns.Contains(c)&&_knownCaverns.Add(c)){queue.Enqueue(c);found.Add(c);}}
        foreach(var d in FluidSimulation.Adjacent)Visit(opening+d);
        while(queue.TryDequeue(out var c))foreach(var d in FluidSimulation.Adjacent)Visit(c+d);
        // One dirty column per floor is enough to rebuild its full-height mesh.
        foreach(var c in found)if(!_caverns.Contains(c+new Cell(0,0,-1))){_changes.Add(c);Revision++;}
    }
    private readonly Dictionary<(int X,int Z),int> _waterColumns=new();
    private readonly List<RiverBoundary> _riverBoundaries=new();
    public IEnumerable<Cell> CavernFloors=>_caverns.Where(c=>!_caverns.Contains(c+new Cell(0,0,-1)));
    public IReadOnlyCollection<Cell> Caverns=>_caverns;
    public IReadOnlyList<RiverBoundary> RiverBoundaries=>_riverBoundaries;
    public IEnumerable<Cell> InitialSurfaceWater=>_waterColumns.SelectMany(p=>Enumerable.Range(this[p.Key.X,p.Key.Z].Level,p.Value-this[p.Key.X,p.Key.Z].Level).Select(y=>new Cell(p.Key.X,p.Key.Z,y)));

    private void GenerateModernTerrain()
    {
        var g=Generation!;var tile=g.Site;int stage=tile.Height-1;
        void Wet(int x,int z,int radius)
        {
            for(int dx=-radius;dx<=radius;dx++)for(int dz=-radius;dz<=radius;dz++)
                if(dx*dx+dz*dz<=radius*radius&&x+dx>=0&&x+dx<Size&&z+dz>=0&&z+dz<Size)
                    _waterColumns[(x+dx,z+dz)]=stage;
        }
        void Channel(int x,int z,int width)
        {
            // Shared edge anchors align adjoining atlas tiles. Keep the junction
            // away from the central camp plateau; no surface channel crosses it.
            int steps=Math.Max(Math.Abs(x-56),Math.Abs(z-56));
            for(int t=0;t<=steps;t++)Wet(56+(x-56)*t/Math.Max(1,steps),56+(z-56)*t/Math.Max(1,steps),width/2);
        }
        var ports=new List<(int X,int Z,bool Inlet,int Width)>();
        if(tile.RiverWidth>0)
        {
            int dx=tile.DownstreamX-g.SiteX,dz=tile.DownstreamY-g.SiteY;
            if(tile.DownstreamX<0){dx=g.SiteX==0?-1:g.SiteX==g.Width-1?1:0;dz=dx==0?(g.SiteY==0?-1:1):0;}
            ports.Add((dx<0?0:dx>0?Size-1:56,dz<0?0:dz>0?Size-1:56,false,tile.RiverWidth));
        }
        foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)})
        {
            int x=g.SiteX+d.Item1,z=g.SiteY+d.Item2;if(x<0||z<0||x>=g.Width||z>=g.Height)continue;
            var neighbor=g.Tile(x,z);
            if(neighbor.RiverWidth>0&&neighbor.DownstreamX==g.SiteX&&neighbor.DownstreamY==g.SiteY)
                ports.Add((d.Item1<0?0:d.Item1>0?Size-1:56,d.Item2<0?0:d.Item2>0?Size-1:56,true,neighbor.RiverWidth));
        }
        // Headwater tiles receive their catchment at the boundary opposite the outlet.
        if(tile.RiverWidth>0&&!ports.Any(p=>p.Inlet))
        {
            var outlet=ports[0];ports.Add((outlet.X==0?Size-1:outlet.X==Size-1?0:56,outlet.Z==0?Size-1:outlet.Z==Size-1?0:56,true,tile.RiverWidth));
        }
        foreach(var p in ports)Channel(p.X,p.Z,p.Width);
        if(tile.Lake)Wet(56,56,18+(int)(tile.Rainfall*8));
        foreach(var p in _waterColumns)
        {
            // A raised bank contains the generated water at its initial head.
            for(int dx=-2;dx<=2;dx++)for(int dz=-2;dz<=2;dz++)
            {
                int x=p.Key.X+dx,z=p.Key.Z+dz;if(x<0||z<0||x>=Size||z>=Size||_waterColumns.ContainsKey((x,z)))continue;
                var old=_surface[x,z];_surface[x,z]=old with {Level=Math.Max(old.Level,stage+1),Tree=false};
            }
            _surface[p.Key.X,p.Key.Z]=new(stage-2,GroundKind.Gravel);
        }
        foreach(var p in ports)
        {
            for(int offset=-p.Width/2;offset<=p.Width/2;offset++)
            {
                int x=p.X+(p.X==0||p.X==Size-1?0:offset),z=p.Z+(p.Z==0||p.Z==Size-1?0:offset);
                if(!_waterColumns.ContainsKey((x,z)))continue;
                for(int y=stage-2;y<stage;y++)_riverBoundaries.Add(new(new(x,z,y),p.Inlet));
            }
        }
        var random=new Random(unchecked(Seed^0x37ca19));
        for(int layer=0;layer<g.CavernLayers;layer++)
        {
            int floor=42-layer*35;var centers=new List<(int X,int Z)>();
            for(int room=0;room<6;room++)
            {
                int cx=25+random.Next(Size-50),cz=25+random.Next(Size-50);centers.Add((cx,cz));
                int rx=8+g.CavernSize*5+random.Next(5),rz=8+g.CavernSize*5+random.Next(5);
                for(int dx=-rx;dx<=rx;dx++)for(int dz=-rz;dz<=rz;dz++)
                {
                    double radius=dx*dx/(double)(rx*rx)+dz*dz/(double)(rz*rz);if(radius>=1)continue;
                    int height=3+(int)((1-radius)*(2+g.CavernSize*2));
                    for(int h=0;h<height;h++)Carve(cx+dx,cz+dz,floor+h);
                }
                if(room==0)continue;
                var previous=centers[room-1];int x=previous.X,z=previous.Z;
                while(x!=cx||z!=cz)
                {
                    if(x!=cx)x+=Math.Sign(cx-x);else z+=Math.Sign(cz-z);
                    for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)for(int h=0;h<3;h++)Carve(x+dx,z+dz,floor+h);
                }
            }
        }
        void Carve(int x,int z,int y)
        {
            var c=new Cell(x,z,y);
            if(InBounds(c)&&y>MinLevel&&y<_surface[x,z].Level-10)_caverns.Add(c);
        }
    }
}
