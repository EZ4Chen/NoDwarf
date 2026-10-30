using Godot;
using NoDwarf.Simulation;
using System.Threading;

namespace NoDwarf;

// Worker-side mesh generation: only immutable input and managed arrays. No nodes,
// resources, SurfaceTool, rendering-server calls, or reads of the live DemoWorld.
internal sealed class TerrainMeshSnapshot
{
    public readonly TerrainMap Terrain;
    public readonly WorldView View;
    public readonly HashSet<Cell> Walls;
    public readonly Dictionary<(int,int),HashSet<int>> Extra=new();
    public TerrainMeshSnapshot(TerrainMap terrain,WorldView view,HashSet<Cell> walls)
    {
        Terrain=terrain;View=view;Walls=walls;
        foreach(var c in terrain.Excavated.Concat(terrain.PlacedBlocks.Select(p=>p.Cell)).Concat(terrain.Caverns.Where(c=>view.RevealAll||terrain.KnownAir(c))).Concat(view.WallCaps?.Keys??Enumerable.Empty<Cell>()))
            foreach(var d in Offsets)
            {
                var p=c+d;if(!DemoWorld.Inside(p))continue;
                if(!Extra.TryGetValue((p.X,p.Z),out var ys))Extra[(p.X,p.Z)]=ys=new();
                ys.Add(p.Level);
            }
    }
    private static readonly Cell[] Offsets={new(0,0),new(1,0),new(-1,0),new(0,1),new(0,-1),new(0,0,1),new(0,0,-1)};
}
internal readonly record struct TerrainSurface(string Id,bool Top,bool Section,bool Cut,bool Rim,bool Underground);
internal sealed record TerrainMeshArrays(Vector3[] Vertices,Vector3[] Normals,Vector2[] Uvs,Color[] Colors);
internal sealed record TerrainMeshResult(Dictionary<TerrainSurface,TerrainMeshArrays> Surfaces,TerrainMeshArrays Edges,Vector3[] Collision,int WorkerThread);
internal static class TerrainMeshBuilder
{
    private sealed class Buffer
    {
        public readonly List<Vector3> Vertices=new(),Normals=new();
        public readonly List<Vector2> Uvs=new();
        public readonly List<Color> Colors=new();
        public TerrainMeshArrays Finish()
        {
            // The original SurfaceTool emitter uses smoothing group 0. Equal
            // positions within a material surface share unit face-normal sums,
            // including UV seams; preserve that lighting rather than flattening it.
            var sums=new Dictionary<Vector3,Vector3>();
            for(int i=0;i<Vertices.Count;i++)sums[Vertices[i]]=sums.GetValueOrDefault(Vertices[i])+Normals[i];
            var normals=new Vector3[Vertices.Count];
            for(int i=0;i<normals.Length;i++)normals[i]=sums[Vertices[i]].Normalized();
            return new(Vertices.ToArray(),normals,Uvs.ToArray(),Colors.ToArray());
        }
    }
    private static Vector3 Point(float x,float y,float z)=>new(x-(TerrainMap.Size-1)/2f,y,z-(TerrainMap.Size-1)/2f);
    private static void Quad(Buffer s,Vector3 a,Vector3 b,Vector3 c,Vector3 d,float width=1,float height=1,Color? ca=null,Color? cb=null,Color? cc=null,Color? cd=null)
    {
        // Godot front faces wind clockwise. Match SurfaceTool's flat triangle normals.
        var normal=(b-a).Cross(c-a).Normalized();
        Add(a,new(0,0),ca);Add(c,new(width,height),cc);Add(b,new(width,0),cb);
        normal=(c-a).Cross(d-a).Normalized();
        Add(a,new(0,0),ca);Add(d,new(0,height),cd);Add(c,new(width,height),cc);
        void Add(Vector3 vertex,Vector2 uv,Color? shade){s.Vertices.Add(vertex);s.Normals.Add(normal);s.Uvs.Add(uv);s.Colors.Add(shade??Godot.Colors.White);}
    }
    public static TerrainMeshResult Build(TerrainMeshSnapshot snapshot,(int,int) key,CancellationToken cancellation=default)
    {
        var terrain=snapshot.Terrain;var view=snapshot.View;float clip=view.Top;var extra=snapshot.Extra;
        var surfaces=new Dictionary<TerrainSurface,Buffer>();var edges=new Buffer();
        Buffer Surface(string id,bool top,bool section=false,bool cut=false,bool rim=false,bool underground=false)
        {
            var material=new TerrainSurface(id,top,section,cut,rim,underground);
            if(!surfaces.TryGetValue(material,out var buffer))surfaces[material]=buffer=new();
            return buffer;
        }
            for(int x=key.Item1;x<Math.Min(key.Item1+16,TerrainMap.Size);x++) for(int z=key.Item2;z<Math.Min(key.Item2+16,TerrainMap.Size);z++)
            {
                cancellation.ThrowIfCancellationRequested();
                var g=terrain[x,z];bool underground=clip<=g.Level;int highest=Math.Min(g.Level,(int)Math.Min(clip,TerrainMap.MaxLevel+1));
                int low=g.Level-1;
                foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    low=Math.Min(low,DemoWorld.Inside(new Cell(x+dx,z+dz))?terrain[x+dx,z+dz].Level-1:TerrainMap.MinLevel);
                var levels=new HashSet<int>{highest,highest-1};
                for(int level=Math.Max(TerrainMap.MinLevel,low);level<=highest;level++)levels.Add(level);
                if(extra.TryGetValue((x,z),out var additional))levels.UnionWith(additional);
                foreach(int y in levels)

                {
                    if(y<TerrainMap.MinLevel||y>TerrainMap.MaxLevel||y>highest&&y>=clip)continue;
                    var cell=new Cell(x,z,y);bool ramp=terrain.Ramp(cell);
                    float cellClip=view.CellTop(cell);
                    if(y>=cellClip||(!terrain.Solid(cell)&&!ramp))continue;
                    bool rim=view.CornerRim(terrain,cell);bool fog=!rim&&!view.Known(terrain,cell);
                    string id=(fog||rim)?"nodwarf:stone":terrain.BlockAt(cell);float top=Math.Min(y+1,cellClip);
                    Vector3 Corner(float dx,float dz)=>Point(x+dx,ramp?Math.Min(terrain.TileHeight(x,z,dx,dz),cellClip):top,z+dz);
                    var a=Corner(-.5f,-.5f);var b=Corner(.5f,-.5f);var c=Corner(.5f,.5f);var d=Corner(-.5f,.5f);
                    var above=cell+new Cell(0,0,1);
                    bool solidAbove=terrain.Solid(above)||terrain.Ramp(above)||snapshot.Walls.Contains(above);
                    // A camera cut also exposes the top below a hidden solid. Seal it
                    // with the existing knowledge/material policy, never the sky background.
                    bool cutAbove=solidAbove&&view.CellTop(above)<=above.Level;
                    Color Contact(int dx,int dz)
                    {
                        if(ramp||fog||rim)return Godot.Colors.White;
                        bool Blocks(int ox,int oz)
                        {var n=cell+new Cell(ox,oz,1);return view.CellTop(n)>n.Level&&(terrain.Solid(n)||snapshot.Walls.Contains(n));}
                        bool sideX=Blocks(dx,0),sideZ=Blocks(0,dz);
                        int count=sideX&&sideZ?3:(sideX?1:0)+(sideZ?1:0)+(Blocks(dx,dz)?1:0);
                        float value=1-count*.12f;return new(value,value,value,1);
                    }
                    if(ramp||y+1>=cellClip||!solidAbove||cutAbove)
                        Quad(Surface(id,true,fog,(y+1>=cellClip&&solidAbove)||cutAbove,rim,underground),a,d,c,b,
                            ca:Contact(-1,-1),cb:Contact(-1,1),cc:Contact(1,1),cd:Contact(1,-1));
                    if(underground&&!fog&&!rim&&!ramp&&!terrain.Solid(cell+new Cell(0,0,1)))
                    {
                        foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                        {
                            // Ground contact line is independent of sun shadows and has no collision.
                            var neighbor=cell+new Cell(dx,dz,1);
                            if(!(terrain.Solid(neighbor)||snapshot.Walls.Contains(neighbor)))continue;
                            var center=Point(x+dx*.475f,top+.008f,z+dz*.475f);
                            var half=new Vector3(dx==0?.5f:.025f,0,dz==0?.5f:.025f);
                            Quad(edges,center+new Vector3(-half.X,0,-half.Z),center+new Vector3(-half.X,0,half.Z),center+new Vector3(half.X,0,half.Z),center+new Vector3(half.X,0,-half.Z));

                        }
                    }
                    foreach(var (p,q,dx,dz) in new[]{(a,b,0,-1),(b,c,1,0),(c,d,0,1),(d,a,-1,0)})
                    {
                        if((terrain.Solid(cell+new Cell(dx,dz))||snapshot.Walls.Contains(cell+new Cell(dx,dz))) && view.CellTop(cell+new Cell(dx,dz))>=top)continue;
                        if(p.Y<=y&&q.Y<=y)continue;
                        Quad(Surface(id,false,fog,rim:rim,underground:underground),p,q,new(q.X,y,q.Z),new(p.X,y,p.Z),1,top-y);
                    }
                    if(y>TerrainMap.MinLevel&&!terrain.Solid(cell+new Cell(0,0,-1))&&!(snapshot.Walls.Contains(cell+new Cell(0,0,-1))&&view.CellTop(cell+new Cell(0,0,-1))>=y))
                        Quad(Surface(id,false,fog,rim:rim,underground:underground),Point(x-.5f,y,z+.5f),Point(x-.5f,y,z-.5f),Point(x+.5f,y,z-.5f),Point(x+.5f,y,z+.5f));
                }
            }
        var result=surfaces.ToDictionary(p=>p.Key,p=>p.Value.Finish());
        var collision=new Vector3[result.Values.Sum(s=>s.Vertices.Length)];int offset=0;
        foreach(var surface in result.Values){surface.Vertices.CopyTo(collision,offset);offset+=surface.Vertices.Length;}
        return new(result,edges.Finish(),collision,System.Environment.CurrentManagedThreadId);
    }
}
