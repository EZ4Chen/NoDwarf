using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class TerrainView
{
    public Vector2 PriorityCell {get;set;}=new(127,127);
    public int SliceCacheHits {get;private set;}
    public int CachedSliceCount=>_sliceCache.Count;
    public long CachedSliceBytes=>_cachedBytes;
    // Conservative geometry estimate (render attributes, collision and driver copies),
    // not a measurement of the renderer/physics allocator's total memory usage.
    private const long SliceCacheBudget=256L*1024*1024;
    private const int SliceCacheLimit=2048;
    private sealed class CapProfile : IEquatable<CapProfile>
    {
        public static readonly CapProfile Empty=new(Array.Empty<KeyValuePair<Cell,float>>());
        private readonly KeyValuePair<Cell,float>[] _values;
        private readonly int _hash;
        public CapProfile(IEnumerable<KeyValuePair<Cell,float>> values)
        {
            _values=values.OrderBy(p=>p.Key.X).ThenBy(p=>p.Key.Z).ThenBy(p=>p.Key.Level).ToArray();
            var hash=new HashCode();foreach(var pair in _values){hash.Add(pair.Key);hash.Add(pair.Value);}_hash=hash.ToHashCode();
        }
        public bool Equals(CapProfile? other)=>ReferenceEquals(this,other)||other is not null&&_hash==other._hash&&_values.AsSpan().SequenceEqual(other._values);
        public override bool Equals(object? other)=>other is CapProfile profile&&Equals(profile);
        public override int GetHashCode()=>_hash;
    }
    private readonly record struct SliceKey((int,int) Chunk,float Top,bool Reveal,CapProfile Caps);
    private sealed record CachedSlice(Node3D Node,long Bytes,LinkedListNode<SliceKey> Age);
    private readonly Dictionary<SliceKey,CachedSlice> _sliceCache=new();
    private readonly LinkedList<SliceKey> _sliceLru=new();
    private readonly record struct ChunkState(int Generation,SliceKey Slice,long Bytes);
    private readonly Dictionary<(int,int),ChunkState> _chunkState=new();
    private readonly Dictionary<(int,int),int> _chunkGenerations=new();
    private TerrainMap? _cacheTerrain;
    private HashSet<Cell> _cacheWalls=new();
    private int _cacheRevision=-1;
    private long _cachedBytes;
    private double ChunkDistance((int,int) key)=>Math.Pow(key.Item1+8-PriorityCell.X,2)+Math.Pow(key.Item2+8-PriorityCell.Y,2);
    private int ChunkGeneration((int,int) key)=>_chunkGenerations.GetValueOrDefault(key);
    // Meshes read adjacent columns for discovery, exposed faces, ramps and contact
    // shading. Invalidate every cached height in that footprint, never the whole map.
    private static IEnumerable<(int,int)> AffectedChunks(Cell cell)
    {
        int x0=Math.Max(0,cell.X-1)/16*16,x1=Math.Min(TerrainMap.Size-1,cell.X+1)/16*16;
        int z0=Math.Max(0,cell.Z-1)/16*16,z1=Math.Min(TerrainMap.Size-1,cell.Z+1)/16*16;
        for(int x=x0;x<=x1;x+=16)for(int z=z0;z<=z1;z+=16)yield return (x,z);
    }
    private static Dictionary<(int,int),CapProfile> SliceCaps(WorldView view)
    {
        var groups=new Dictionary<(int,int),List<KeyValuePair<Cell,float>>>();
        if(view.WallCaps is not null)foreach(var pair in view.WallCaps)
            foreach(var chunk in AffectedChunks(pair.Key))
            {if(!groups.TryGetValue(chunk,out var values))groups[chunk]=values=new();values.Add(pair);}
        return groups.ToDictionary(p=>p.Key,p=>new CapProfile(p.Value));
    }
    private void PrepareSliceCache(DemoWorld world,HashSet<Cell> walls)
    {
        if(_cacheTerrain!=world.Terrain)
        {
            ClearSliceCache();_cacheTerrain=world.Terrain;_cacheRevision=world.Terrain.Revision;_cacheWalls=walls;
            return;
        }
        var changed=new HashSet<(int,int)>();
        foreach(var cell in world.Terrain.ChangesSince(_cacheRevision))changed.UnionWith(AffectedChunks(cell));
        foreach(var cell in _cacheWalls.Except(walls).Concat(walls.Except(_cacheWalls)))changed.UnionWith(AffectedChunks(cell));
        foreach(var chunk in changed)_chunkGenerations[chunk]=ChunkGeneration(chunk)+1;
        foreach(var key in _sliceCache.Keys.Where(k=>changed.Contains(k.Chunk)).ToArray())EvictSlice(key);
        _cacheRevision=world.Terrain.Revision;_cacheWalls=walls;
    }
    private void ClearSliceCache()
    {
        foreach(var entry in _sliceCache.Values)entry.Node.Free();
        _sliceCache.Clear();_sliceLru.Clear();_cachedBytes=0;_cacheTerrain=null;_cacheRevision=-1;
        _cacheWalls=new();_chunkGenerations.Clear();_chunkState.Clear();
    }
    private void EvictSlice(SliceKey key)
    {
        var entry=_sliceCache[key];_sliceCache.Remove(key);_sliceLru.Remove(entry.Age);
        _cachedBytes-=entry.Bytes;entry.Node.QueueFree();
    }
    private static void EnableChunk(Node3D node,bool enabled)
    {
        node.Visible=enabled;
        foreach(var body in node.GetChildren().OfType<StaticBody3D>())
        {body.CollisionLayer=enabled?1u:0u;body.CollisionMask=enabled?1u:0u;}
    }
    private bool RestoreSlice((int,int) key,int generation,SliceKey slice)
    {
        if(_chunks.TryGetValue(key,out var current)&&_chunkState.TryGetValue(key,out var state)
            &&state.Generation==generation&&state.Slice==slice)
        {EnableChunk(current,true);SliceCacheHits++;return true;}
        if(!_sliceCache.Remove(slice,out var saved))return false;
        _sliceLru.Remove(saved.Age);_cachedBytes-=saved.Bytes;
        RetireChunk(key);
        EnableChunk(saved.Node,true);_chunks[key]=saved.Node;
        _chunkState[key]=new(generation,slice,saved.Bytes);SliceCacheHits++;return true;
    }
    private void RetireChunk((int,int) key)
    {
        if(!_chunks.Remove(key,out var old))return;
        EnableChunk(old,false);
        if(_chunkState.Remove(key,out var state)&&state.Generation==ChunkGeneration(key)&&state.Bytes<=SliceCacheBudget)
        {
            if(_sliceCache.ContainsKey(state.Slice))EvictSlice(state.Slice);
            _sliceCache[state.Slice]=new(old,state.Bytes,_sliceLru.AddLast(state.Slice));_cachedBytes+=state.Bytes;
            // Keep inactive nodes attached, hidden and non-colliding. Restoring a slice
            // then only toggles state, with no upload or physics-body reconstruction.
            while(_sliceCache.Count>SliceCacheLimit||_cachedBytes>SliceCacheBudget)EvictSlice(_sliceLru.First!.Value);
        }
        else old.QueueFree();
    }
}
