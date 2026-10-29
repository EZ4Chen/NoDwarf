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
    private HashSet<Cell> _lastWallCells=new();
    private (DemoWorld,long)? _wallInputs;
    private Dictionary<Cell,float> _lastCaps=new();
    public int LastRebuiltChunks {get;private set;}
    private static readonly StandardMaterial3D Fog=new(){AlbedoColor=Colors.Black,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
    private Node3D? _trees;
    public void Generate(DemoWorld world,WorldView view)
    {
        // Retire last frame's completed work before coalescing a newer camera/edit
        // request, otherwise continuous rotation could starve the same chunk forever.
        PumpMeshes();
        var terrain=world.Terrain;float clip=view.Top;
        var wallInputs=(world,world.Navigation.Version);
        var wallCells=_wallInputs==wallInputs?_lastWallCells:world.Buildings.Values.Where(b=>BlockCatalog.IsWall(b.Kind)).Select(b=>b.Cell).ToHashSet();
        _wallInputs=wallInputs;
        bool wallsChanged=!_lastWallCells.SetEquals(wallCells);
        bool all=_lastTerrain!=terrain||_lastClip!=clip||_lastReveal!=view.RevealAll;
        if(!all&&!wallsChanged&&_lastRevision==terrain.Revision&&_lastStamp==view.Stamp){UpdateGroundCover(world,view);return;}
        using var timing = world.Probe?.Measure("view.terrain-remesh") ?? default;
        if(_lastTerrain!=terrain) { ResetMeshPipeline(); foreach(Node child in GetChildren()){RemoveChild(child);child.QueueFree();} _chunks.Clear();_trees=null; }
        var dirty=new HashSet<(int,int)>();
        if(all) for(int x=0;x<TerrainMap.Size;x+=16)for(int z=0;z<TerrainMap.Size;z+=16)dirty.Add((x,z));
        else foreach(var c in terrain.ChangesSince(_lastRevision)) foreach(var d in new[]{new Cell(0,0),new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1),new Cell(1,1),new Cell(1,-1),new Cell(-1,1),new Cell(-1,-1)})
            if(DemoWorld.Inside(c+d))dirty.Add((((c.X+d.X)/16)*16,((c.Z+d.Z)/16)*16));
        if(!all&&wallsChanged)
            foreach(var c in wallCells.Except(_lastWallCells).Concat(_lastWallCells.Except(wallCells)))
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                    if(DemoWorld.Inside(c+new Cell(dx,dz)))dirty.Add(((c.X+dx)/16*16,(c.Z+dz)/16*16));
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
        // Only exposed candidates need meshing; buried rock is implicit.
        QueueMeshes(world,view,dirty,all);
        if(_trees is null||_lastTrees!=world.TreeRevision)
        {
            _lastTrees=world.TreeRevision;
            if(_trees is not null){RemoveChild(_trees);_trees.QueueFree();}
            _trees=new Node3D();AddChild(_trees);
            BuildVoxelTrees(world,clip);
        }
        UpdateTreeClip(clip);
        CaptureTreeFalls(world,view);
        UpdateGroundCover(world,view);
        _lastTerrain=terrain;_lastRevision=terrain.Revision;_lastClip=clip;_lastReveal=view.RevealAll;_lastStamp=view.Stamp;
        _lastWallCells=wallCells;
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
    private long _mapFluidRevision=-1;
    private HashSet<(int X,int Z)> _mapWater=new();
    private Image? _mapImage;
    public const double RefreshIntervalSeconds = 5;
    private double _mapRefreshElapsed;
    internal int MapRefreshCount {get;private set;}
    internal Color MapPixel(int x,int z) => _mapImage!.GetPixel(x,z);
    public override void _Process(double delta) => AdvanceMapRefresh(delta,IsVisibleInTree());
    internal void AdvanceMapRefresh(double delta,bool visible=true)
    {
        _mapRefreshElapsed=Math.Min(RefreshIntervalSeconds,_mapRefreshElapsed+Math.Max(0,delta));
        if(World is null||!visible)return;
        if(_mapTerrain!=World.Terrain||_mapImage is null) {RenderMap();return;}
        if(_mapRefreshElapsed<RefreshIntervalSeconds)return;
        _mapRefreshElapsed=0;
        if(_mapRevision!=World.Terrain.Revision||_mapFluidRevision!=World.Fluids.Revision)RenderMap();
    }
    // Calls from edits only request refresh. A new world is drawn immediately;
    // later edits are coalesced on a wall-clock cadence, including while paused.
    public void RefreshMap()
    {
        if(_mapTerrain!=World.Terrain||_mapImage is null)RenderMap();
    }
    private void RenderMap()
    {
        bool full = _mapTerrain != World.Terrain || _mapImage is null;
        var changed = full ? null : World.Terrain.ChangesSince(_mapRevision)
            .Where(c => c.Level >= World.Terrain[c.X,c.Z].Level - 1).Select(c => (c.X,c.Z)).ToHashSet();
        if(full||_mapFluidRevision!=World.Fluids.Revision)
        {
            var water=World.Fluids.Cells.Values.Where(f=>f.Amount>=100&&f.Cell.Level>=World.Terrain[f.Cell.X,f.Cell.Z].Level).Select(f=>(f.Cell.X,f.Cell.Z)).ToHashSet();
            if(!full){var difference=new HashSet<(int X,int Z)>(_mapWater);difference.SymmetricExceptWith(water);changed!.UnionWith(difference);}
            _mapWater=water;_mapFluidRevision=World.Fluids.Revision;
        }
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
            if(_mapWater.Contains((x,z)))c=new Color("428fba");
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
        _mapRefreshElapsed=0;MapRefreshCount++;
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
