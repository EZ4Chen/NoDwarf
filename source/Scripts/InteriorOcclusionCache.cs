using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private (DemoWorld,long,int)? _interiorCacheEpoch;
    private readonly Dictionary<(int Floor,bool Mining),(InteriorTopology Data,LinkedListNode<(int Floor,bool Mining)> Age)> _interiorTopologies=new();
    private readonly LinkedList<(int Floor,bool Mining)> _topologyLru=new();
    private sealed class InteriorFootprint : IEquatable<InteriorFootprint>
    {
        public readonly (int X,int Z)[] Offsets;
        private readonly int _hash;
        public InteriorFootprint(Vector3 back,float reach)
        {
            var offsets=new HashSet<(int,int)>();
            foreach(float ox in OcclusionSamples)foreach(float oz in OcclusionSamples)
                for(float distance=0;distance<=reach;distance+=.2f)
                    offsets.Add(((int)Math.Floor(ox+back.X*distance+.5f),(int)Math.Floor(oz+back.Z*distance+.5f)));
            Offsets=offsets.OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).ToArray();
            var hash=new HashCode();foreach(var offset in Offsets)hash.Add(offset);_hash=hash.ToHashCode();
        }
        public bool Equals(InteriorFootprint? other)=>ReferenceEquals(this,other)||other is not null&&_hash==other._hash&&Offsets.AsSpan().SequenceEqual(other.Offsets);
        public override bool Equals(object? other)=>other is InteriorFootprint footprint&&Equals(footprint);
        public override int GetHashCode()=>_hash;
    }
    private readonly record struct InteriorCutKey(int Floor,int Height,InteriorFootprint Footprint,bool Mining);
    private readonly Dictionary<InteriorCutKey,(Dictionary<Cell,float> Caps,LinkedListNode<InteriorCutKey> Age)> _interiorCuts=new();
    private readonly LinkedList<InteriorCutKey> _cutLru=new();
    private int _cachedInteriorCaps,_interiorOcclusionBuilds,_interiorCacheHits;
    private const int InteriorCutLimit=96,InteriorCapLimit=1_000_000;
    private readonly InteriorOcclusionScratch _interiorSyncScratch=new();
    private InteriorOcclusionInput? _interiorSnapshot;
    private TerrainMap? _interiorTerrainSource,_interiorTerrainSnapshot;
    private int _interiorTerrainRevision=-1;
    private long _interiorGeneration;
    // Keep the near facade down even at steep pitch. A pure obstruction ray gets
    // shorter than one tile there and makes walls rise while inspecting a room.
    private InteriorCutKey InteriorKey(int floor,int height)=>new(floor,height,new InteriorFootprint(CameraBack,
        Math.Max(_interiorView?1.5f:0,(height-1+.5f)/Mathf.Tan(_cameraPitch))),MiningCutaway&&!_interiorView);
    private InteriorOcclusionInput CaptureInteriorInput()
    {
        if(_interiorSnapshot is not null)return _interiorSnapshot;
        using var trace=DiagnosticTrace.Span("interior.snapshot",revision:_interiorGeneration);
        using var timing=_world.Probe?.Measure("view.interior-snapshot")??default;
        if(_interiorTerrainSource!=_world.Terrain||_interiorTerrainRevision!=_world.Terrain.Revision)
        {
            _interiorTerrainSource=_world.Terrain;_interiorTerrainRevision=_world.Terrain.Revision;
            _interiorTerrainSnapshot=_world.Terrain.CaptureMeshSnapshot();
        }
        var walls=new HashSet<Cell>();var doors=new HashSet<Cell>();
        foreach(var b in _world.Buildings.Values)
        {if(BlockCatalog.IsWall(b.Kind))walls.Add(b.Cell);if(b.Kind==BuildKind.Door)doors.Add(b.Cell);}
        return _interiorSnapshot=new(_interiorTerrainSnapshot!,walls,doors,_world.Buildings.Keys.ToArray());
    }

    private void PrepareInteriorCache()
    {
        var epoch=(_world,_world.Navigation.Version,_world.Terrain.Revision);
        if(_interiorCacheEpoch==epoch)return;
        _interiorCacheEpoch=epoch;_interiorGeneration++;_interiorSnapshot=null;CancelInteriorWork();
        _interiorTopologies.Clear();_topologyLru.Clear();
        _interiorCuts.Clear();_cutLru.Clear();_cachedInteriorCaps=0;
    }
    private InteriorTopology InteriorRooms(int floor,bool mining=false)
    {
        if(_interiorTopologies.TryGetValue((floor,mining),out var cached))
        {_topologyLru.Remove(cached.Age);_topologyLru.AddLast(cached.Age);return cached.Data;}
        var topology=mining?InteriorOcclusionBuilder.ExcavatedFloors(CaptureInteriorInput(),floor):InteriorOcclusionBuilder.Rooms(CaptureInteriorInput(),floor);
        StoreInteriorTopology(floor,topology,mining);return topology;
    }
    private void StoreInteriorTopology(int floor,InteriorTopology topology,bool mining=false)
    {
        if(_interiorTopologies.ContainsKey((floor,mining)))return;
        _roomTopologyBuilds++;_world.Probe?.Count("view.interior-visited-cells",topology.Visits);
        _interiorTopologies[(floor,mining)]=(topology,_topologyLru.AddLast((floor,mining)));
        if(_topologyLru.Count>8){var oldest=_topologyLru.First!.Value;_topologyLru.RemoveFirst();_interiorTopologies.Remove(oldest);}
    }
    private bool TryInteriorCaps(InteriorCutKey key,out Dictionary<Cell,float> caps)
    {
        if(_interiorCuts.TryGetValue(key,out var cached))
        {
            _cutLru.Remove(cached.Age);_cutLru.AddLast(cached.Age);_interiorCacheHits++;
            _world.Probe?.Count("view.interior-cache-hit");caps=cached.Caps;return true;
        }
        caps=null!;return false;
    }
    private Dictionary<Cell,float> InteriorCaps(int floor,int height)
    {
        PrepareInteriorCache();
        var key=InteriorKey(floor,height);
        if(TryInteriorCaps(key,out var cached))return cached;
        _interiorOcclusionBuilds++;_world.Probe?.Count("view.interior-cache-miss");
        var topology=InteriorRooms(floor,key.Mining);
        var caps=InteriorOcclusionBuilder.Caps(CaptureInteriorInput(),topology,floor,height,key.Footprint.Offsets,_interiorSyncScratch,rimHeight:key.Mining?.1f:.2f);
        _world.Probe?.Count("view.interior-columns-tested",_interiorSyncScratch.Touched.Count);
        StoreInteriorCaps(key,caps);return caps;
    }
    private void StoreInteriorCaps(InteriorCutKey key,Dictionary<Cell,float> caps)
    {
        if(_interiorCuts.ContainsKey(key))return;
        if(caps.Count<=InteriorCapLimit)
        {
            _interiorCuts[key]=(caps,_cutLru.AddLast(key));_cachedInteriorCaps+=caps.Count;
            while(_interiorCuts.Count>InteriorCutLimit||_cachedInteriorCaps>InteriorCapLimit)
            {var first=_cutLru.First!.Value;_cutLru.RemoveFirst();_cachedInteriorCaps-=_interiorCuts[first].Caps.Count;_interiorCuts.Remove(first);}
        }
    }
}
