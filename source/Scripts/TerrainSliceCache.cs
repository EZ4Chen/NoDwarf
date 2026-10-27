using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class TerrainView
{
    public Vector2 PriorityCell {get;set;}=new(127,127);
    public int SliceCacheHits {get;private set;}
    private readonly record struct SliceKey((int,int) Chunk,float Top,bool Reveal);
    private sealed record CachedSlice(Node3D Node,int Vertices);
    private readonly Dictionary<SliceKey,CachedSlice> _sliceCache=new();
    private readonly record struct ChunkState(int Generation,float Top,bool Reveal,bool Cacheable,int Vertices);
    private readonly Dictionary<(int,int),ChunkState> _chunkState=new();
    private TerrainMap? _cacheTerrain;
    private HashSet<Cell> _cacheWalls=new();
    private int _sliceGeneration,_cachedVertices;
    private double ChunkDistance((int,int) key)=>Math.Pow(key.Item1+8-PriorityCell.X,2)+Math.Pow(key.Item2+8-PriorityCell.Y,2);
    private void PrepareSliceCache(TerrainMeshSnapshot snapshot)
    {
        if(_cacheTerrain==snapshot.Terrain&&_cacheWalls.SetEquals(snapshot.Walls))return;
        ClearSliceCache();_cacheTerrain=snapshot.Terrain;_cacheWalls=snapshot.Walls;
    }
    private void ClearSliceCache()
    {
        foreach(var entry in _sliceCache.Values)entry.Node.Free();
        _sliceCache.Clear();_cachedVertices=0;_cacheTerrain=null;_sliceGeneration++;
    }
    private static void EnableChunk(Node3D node,bool enabled)
    {
        node.Visible=enabled;
        foreach(var body in node.GetChildren().OfType<StaticBody3D>())body.CollisionLayer=enabled?1u:0u;
    }
    private bool RestoreSlice(MeshRequest request)
    {
        var view=request.Snapshot.View;
        if(view.WallCaps is not null)return false;
        if(_chunks.TryGetValue(request.Key,out var current)&&_chunkState.TryGetValue(request.Key,out var state)
            &&state.Generation==_sliceGeneration&&state.Cacheable
            &&state.Top==view.Top&&state.Reveal==view.RevealAll)
        {EnableChunk(current,true);SliceCacheHits++;return true;}
        var key=new SliceKey(request.Key,view.Top,view.RevealAll);
        if(!_sliceCache.Remove(key,out var saved))return false;
        _cachedVertices-=saved.Vertices;
        RetireChunk(request.Key);
        AddChild(saved.Node);EnableChunk(saved.Node,true);_chunks[request.Key]=saved.Node;
        _chunkState[request.Key]=new(request.CacheGeneration,view.Top,view.RevealAll,true,saved.Vertices);SliceCacheHits++;return true;
    }
    private void RetireChunk((int,int) key)
    {
        if(!_chunks.Remove(key,out var old))return;
        RemoveChild(old);EnableChunk(old,false);
        if(_chunkState.Remove(key,out var state)&&state.Generation==_sliceGeneration
            &&state.Cacheable&&ChunkDistance(key)<64*64&&state.Vertices<=600_000)
        {
            var cacheKey=new SliceKey(key,state.Top,state.Reveal);
            if(_sliceCache.Remove(cacheKey,out var duplicate)){_cachedVertices-=duplicate.Vertices;duplicate.Node.Free();}
            _sliceCache[cacheKey]=new(old,state.Vertices);_cachedVertices+=state.Vertices;
            // Bounded CPU/GPU geometry storage, restricted to chunks around the camera.
            while(_sliceCache.Count>128||_cachedVertices>600_000)
            {var first=_sliceCache.First();_sliceCache.Remove(first.Key);_cachedVertices-=first.Value.Vertices;first.Value.Node.Free();}
        }
        else old.QueueFree();
    }
}
