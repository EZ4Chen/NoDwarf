using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class TerrainView : Node3D
{
    public static Vector3 Point(float x, float y, float z) => new(x - (TerrainMap.Size - 1) / 2f, y, z - (TerrainMap.Size - 1) / 2f);
    public static Color ColorOf(Ground g) => new(BlockCatalog.Get(g.BlockId).Color);
    private readonly Dictionary<(int,int),Node3D> _chunks=new();
    private TerrainMap? _lastTerrain;
    private int _lastRevision=-1,_lastTrees=-1;
    private float _lastClip=float.NaN;
    private bool _lastReveal;
    private int _lastStamp;
    private Dictionary<Cell,float> _lastCaps=new();
    public int LastRebuiltChunks {get;private set;}
    private static readonly StandardMaterial3D Fog=new(){AlbedoColor=Colors.Black,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
    private Node3D? _trees;
    public void Generate(DemoWorld world,WorldView view)
    {
        var terrain=world.Terrain;float clip=view.Top;
        bool all=_lastTerrain!=terrain||_lastClip!=clip||_lastReveal!=view.RevealAll;
        if(!all&&_lastRevision==terrain.Revision&&_lastStamp==view.Stamp){UpdateGroundCover(world,view);return;}
        using var timing = world.Probe?.Measure("view.terrain-remesh") ?? default;
        if(_lastTerrain!=terrain) { foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();} _chunks.Clear();_trees=null; }
        var dirty=new HashSet<(int,int)>();
        if(all) for(int x=0;x<TerrainMap.Size;x+=16)for(int z=0;z<TerrainMap.Size;z+=16)dirty.Add((x,z));
        else foreach(var c in terrain.ChangesSince(_lastRevision)) foreach(var d in new[]{new Cell(0,0),new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1),new Cell(1,1),new Cell(1,-1),new Cell(-1,1),new Cell(-1,-1)})
            if(DemoWorld.Inside(c+d))dirty.Add((((c.X+d.X)/16)*16,((c.Z+d.Z)/16)*16));
        // Camera rotation changes only wall caps: rebuild their chunks plus neighboring
        // faces, retaining all other meshes and picking collision bodies.
        if(!all&&_lastStamp!=view.Stamp)
        {
            var caps=view.WallCaps;
            foreach(var c in _lastCaps.Keys.Concat(caps?.Keys??Enumerable.Empty<Cell>()).Distinct())
            {
                float before=_lastCaps.TryGetValue(c,out var oldCap)?oldCap:clip;
                float after=caps is not null&&caps.TryGetValue(c,out var newCap)?newCap:clip;
                if(before==after)continue;
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                    if(DemoWorld.Inside(c+new Cell(dx,dz)))dirty.Add(((c.X+dx)/16*16,(c.Z+dz)/16*16));
            }
        }
        LastRebuiltChunks=dirty.Count;
        // Only exposed candidates need meshing; buried rock is implicit.
        world.Probe?.Count("view.terrain-chunks-rebuilt", dirty.Count);
        var extra=new Dictionary<(int,int),HashSet<int>>();
        void Candidate(Cell c)
        {
            if(!DemoWorld.Inside(c))return;
            if(!extra.TryGetValue((c.X,c.Z),out var ys))extra[(c.X,c.Z)]=ys=new();
            ys.Add(c.Level);
        }
        foreach(var c in terrain.Excavated.Concat(view.WallCaps?.Keys ?? Enumerable.Empty<Cell>()))
            foreach(var d in new[]{new Cell(0,0),new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1),new Cell(0,0,1),new Cell(0,0,-1)})Candidate(c+d);
        foreach(var key in dirty)
        {
            if(_chunks.Remove(key,out var old)){RemoveChild(old);old.QueueFree();}
            var holder=new Node3D();AddChild(holder);_chunks[key]=holder;
            var surfaces=new Dictionary<(string,bool,bool,bool,bool,bool),SurfaceTool>();
            SurfaceTool Surface(string id,bool top,bool section=false,bool cut=false,bool rim=false,bool underground=false)
            {
                if(surfaces.TryGetValue((id,top,section,cut,rim,underground),out var found))return found;
                var material=BlockTextures.TerrainMaterial(id,top,terrain.Region);
                if(section)material=Fog;
                else if(rim){material=(StandardMaterial3D)material.Duplicate();material.AlbedoColor=new Color(.26f,.28f,.30f,1);}
                else if(cut){material=(StandardMaterial3D)material.Duplicate();material.AlbedoColor*=new Color(.62f,.65f,.68f,1);}
                if(underground&&!section)
                {
                    material=(StandardMaterial3D)material.Duplicate();
                    material.ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded;
                    if(rim)material.AlbedoColor=new Color(.18f,.20f,.22f,1);
                    else if(cut){material.AlbedoColor=new Color(.34f,.40f,.45f,1);}
                    else material.AlbedoColor=top?new Color(.80f,.83f,.85f,1):new Color(.43f,.49f,.55f,1);
                }
                var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetMaterial(material);surfaces[(id,top,section,cut,rim,underground)]=tool;return tool;
            }
            var edges=new SurfaceTool();edges.Begin(Mesh.PrimitiveType.Triangles);
            edges.SetMaterial(new StandardMaterial3D{AlbedoColor=new Color("20272e"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled});
            bool hasEdges=false;
            for(int x=key.Item1;x<Math.Min(key.Item1+16,TerrainMap.Size);x++) for(int z=key.Item2;z<Math.Min(key.Item2+16,TerrainMap.Size);z++)
            {
                var g=terrain[x,z];bool underground=clip<=g.Level;int highest=Math.Min(g.Level,(int)Math.Min(clip,TerrainMap.MaxLevel+1));
                int low=g.Level-1;
                foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    low=Math.Min(low,DemoWorld.Inside(new Cell(x+dx,z+dz))?terrain[x+dx,z+dz].Level-1:TerrainMap.MinLevel);
                var levels=new HashSet<int>{highest,highest-1};
                for(int level=Math.Max(TerrainMap.MinLevel,low);level<=highest;level++)levels.Add(level);
                if(extra.TryGetValue((x,z),out var additional))levels.UnionWith(additional);
                foreach(int y in levels)

                {
                    if(y<TerrainMap.MinLevel||y>highest)continue;
                    var cell=new Cell(x,z,y);bool ramp=terrain.Ramp(cell);
                    float cellClip=view.CellTop(cell);
                    if(y>=cellClip||(!terrain.Solid(cell)&&!ramp))continue;
                    bool rim=view.CornerRim(world,cell);bool fog=!rim&&!view.Known(world,cell);
                    string id=(fog||rim)?"nodwarf:stone":terrain.BlockAt(cell);float top=Math.Min(y+1,cellClip);
                    Vector3 Corner(float dx,float dz)=>Point(x+dx,ramp?Math.Min(terrain.TileHeight(x,z,dx,dz),cellClip):top,z+dz);
                    var a=Corner(-.5f,-.5f);var b=Corner(.5f,-.5f);var c=Corner(.5f,.5f);var d=Corner(-.5f,.5f);
                    var above=cell+new Cell(0,0,1);
                    bool solidAbove=terrain.Solid(above)||terrain.Ramp(above);
                    // A camera cut also exposes the top below a hidden solid. Seal it
                    // with the existing knowledge/material policy, never the sky background.
                    bool cutAbove=solidAbove&&view.CellTop(above)<=above.Level;
                    if(ramp||y+1>=cellClip||!solidAbove||cutAbove)
                        BlockTextures.Quad(Surface(id,true,fog,(y+1>=cellClip&&solidAbove)||cutAbove,rim,underground),a,d,c,b);
                    if(underground&&!fog&&!rim&&!ramp&&!terrain.Solid(cell+new Cell(0,0,1)))
                    {
                        foreach(var (dx,dz) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                        {
                            // Ground contact line is independent of sun shadows and has no collision.
                            var neighbor=cell+new Cell(dx,dz,1);
                            if(!world.Solid(neighbor))continue;
                            var center=Point(x+dx*.475f,top+.008f,z+dz*.475f);
                            var half=new Vector3(dx==0?.5f:.025f,0,dz==0?.5f:.025f);
                            BlockTextures.Quad(edges,center+new Vector3(-half.X,0,-half.Z),center+new Vector3(-half.X,0,half.Z),center+new Vector3(half.X,0,half.Z),center+new Vector3(half.X,0,-half.Z));
                            hasEdges=true;
                        }
                    }
                    foreach(var (p,q,dx,dz) in new[]{(a,b,0,-1),(b,c,1,0),(c,d,0,1),(d,a,-1,0)})
                    {
                        if(terrain.Solid(cell+new Cell(dx,dz)) && view.CellTop(cell+new Cell(dx,dz))>=top)continue;
                        if(p.Y<=y&&q.Y<=y)continue;
                        BlockTextures.Quad(Surface(id,false,fog,rim:rim,underground:underground),p,q,new(q.X,y,q.Z),new(p.X,y,p.Z),1,top-y);
                    }
                    if(y>TerrainMap.MinLevel&&!terrain.Solid(cell+new Cell(0,0,-1)))
                        BlockTextures.Quad(Surface(id,false,fog,rim:rim,underground:underground),Point(x-.5f,y,z+.5f),Point(x-.5f,y,z-.5f),Point(x+.5f,y,z-.5f),Point(x+.5f,y,z+.5f));
                }
            }
            if(hasEdges)
            {
                edges.GenerateNormals();var edgeMesh=new ArrayMesh();edges.Commit(edgeMesh);
                holder.AddChild(new MeshInstance3D{Mesh=edgeMesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
            }
            var mesh=new ArrayMesh();foreach(var surface in surfaces.Values){surface.GenerateNormals();surface.Commit(mesh);}
            if(mesh.GetSurfaceCount()>0)
            {
                var instance=new MeshInstance3D{Mesh=mesh};holder.AddChild(instance);
                using var collisionTiming = world.Probe?.Measure("view.terrain-collision-create") ?? default;
                instance.CreateTrimeshCollision();
            }
        }
        if(all||_lastTrees!=world.TreeRevision)
        {
            _lastTrees=world.TreeRevision;
            if(_trees is not null){RemoveChild(_trees);_trees.QueueFree();}
            _trees=new Node3D();AddChild(_trees);
            BuildVoxelTrees(world,clip);
        }
        CaptureTreeFalls(world,view);
        UpdateGroundCover(world,view);
        _lastTerrain=terrain;_lastRevision=terrain.Revision;_lastClip=clip;_lastReveal=view.RevealAll;_lastStamp=view.Stamp;
        _lastCaps=view.WallCaps?.ToDictionary(p=>p.Key,p=>p.Value)??new();
    }
    // Godot uses clockwise front faces. Rendering was double-sided, but ray picking
    // correctly ignores back faces: keep collision and visible surface orientation aligned.
    private static void Quad(SurfaceTool s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    { s.SetColor(color); foreach (var v in new[] { a, c, b, a, d, c }) s.AddVertex(v); }
}

public partial class WorldOverview : Control
{
    private ImageTexture _texture = null!;
    public DemoWorld World { get; set; } = null!;
    public Vector2 CameraCell { get; set; }
    public float ViewSize { get; set; }
    public WorldView Slice {get;set;}
    public Action<Vector2>? Navigate;
    private TerrainMap? _mapTerrain;
    private int _mapRevision = -1;
    private Image? _mapImage;
    public void RefreshMap()
    {
        bool full = _mapTerrain != World.Terrain || _mapImage is null;
        var changed = full ? null : World.Terrain.ChangesSince(_mapRevision)
            .Where(c => c.Level == World.Terrain[c.X,c.Z].Level - 1).Select(c => (c.X,c.Z)).ToHashSet();
        _mapTerrain = World.Terrain; _mapRevision = World.Terrain.Revision;
        if (!full && changed!.Count == 0) { QueueRedraw(); return; }
        using var timing = World.Probe?.Measure(full ? "view.overview-full-refresh" : "view.overview-patch") ?? default;
        if (full) _mapImage = Image.CreateEmpty(TerrainMap.Size, TerrainMap.Size, false, Image.Format.Rgba8);
        void Pixel(int x, int z)
        {
            var g = World.Terrain[x,z];
            var c = TerrainView.ColorOf(g) * Math.Clamp(.72f + (g.Level-TerrainMap.SeaLevel)*.025f,.5f,1.15f);
            if (g.Tree) c = new Color("35472f");
            if (World.Terrain.Excavated.Contains(new Cell(x,z,g.Level-1))) c = new Color("514d46");
            c.A = 1; _mapImage!.SetPixel(x,z,c);
        }
        if (full)
        {
            for (int x=0;x<TerrainMap.Size;x++) for (int z=0;z<TerrainMap.Size;z++) Pixel(x,z);
            _texture = ImageTexture.CreateFromImage(_mapImage!);
        }
        else
        {
            foreach (var (x,z) in changed!) Pixel(x,z);
            _texture.Update(_mapImage!);
        }
        QueueRedraw();
    }
    public override void _Draw()
    {
        if (_texture is null) return;
        DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);
        Vector2 Scale(float x, float z) => new(x / TerrainMap.Size * Size.X, z / TerrainMap.Size * Size.Y);
        foreach (var b in World.Buildings.Keys.Where(c=>Slice.Visible(World,c))) DrawRect(new Rect2(Scale(b.X, b.Z), Vector2.One * 2), new Color("d5c9a3"));
        foreach (var w in World.Workers.Where(w=>Slice.Visible(World,w.Tile))) DrawCircle(Scale((float)w.X, (float)w.Z), 2, new Color("f6df8d"));
        var center = Scale(CameraCell.X, CameraCell.Y); float radius = ViewSize / TerrainMap.Size * Size.X * .5f;
        var points = new[] { center + new Vector2(0, -radius * .7f), center + new Vector2(radius, 0), center + new Vector2(0, radius * .7f), center + new Vector2(-radius, 0), center + new Vector2(0, -radius * .7f) };
        DrawPolyline(points, new Color("e1d4b5"), 1, true);
    }
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
        { Navigate?.Invoke(mouse.Position / Size * TerrainMap.Size); AcceptEvent(); }
    }
}
