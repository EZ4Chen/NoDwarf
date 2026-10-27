using Godot;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Threading;

namespace NoDwarf;

public partial class TerrainView
{
    private sealed record MeshRequest((int,int) Key,long Version,TerrainMeshSnapshot Snapshot,int CacheGeneration);
    private sealed record MeshWork(MeshRequest Request,CancellationTokenSource Cancellation,Task<TerrainMeshResult> Task);
    private readonly Dictionary<(int,int),MeshRequest> _pendingMeshes=new();
    private readonly Dictionary<(int,int),long> _meshVersions=new();
    private readonly List<MeshWork> _meshWorkers=new();
    private readonly Dictionary<TerrainSurface,Material> _terrainMaterials=new();
    private TerrainMap? _snapshotSource,_snapshotTerrain;
    private int _snapshotRevision=-1;
    private long _nextMeshVersion;
    private ulong _submissionFrame=ulong.MaxValue;
    private double _submissionMs;
    private int _submissions;
    private bool _meshStopped;
    private PerformanceProbe? _meshProbe;
    public bool SynchronousMeshing {get;set;}
    public int PendingMeshCount=>_pendingMeshes.Count+_meshWorkers.Count;
    public int AppliedMeshCount {get;private set;}
    public int DiscardedMeshCount {get;private set;}
    public HashSet<int> MeshWorkerThreads {get;}=new();
    private const int MeshConcurrency=2;
    private TerrainMeshSnapshot CaptureMeshInput(DemoWorld world,WorldView view)
    {
        using var timing=world.Probe?.Measure("view.terrain-snapshot")??default;
        if(_snapshotSource!=world.Terrain||_snapshotRevision!=world.Terrain.Revision)
        {
            _snapshotSource=world.Terrain;_snapshotRevision=world.Terrain.Revision;
            _snapshotTerrain=world.Terrain.CaptureMeshSnapshot();
        }
        var frozenView=view with {WallCaps=view.WallCaps?.ToDictionary(p=>p.Key,p=>p.Value)};
        return new(_snapshotTerrain!,frozenView,world.Buildings.Values.Where(b=>BlockCatalog.IsWall(b.Kind)).Select(b=>b.Cell).ToHashSet());
    }
    private void QueueMeshes(DemoWorld world,WorldView view,HashSet<(int,int)> dirty,bool all)
    {
        _meshProbe=world.Probe;
        if(dirty.Count==0)return;
        var snapshot=CaptureMeshInput(world,view);
        PrepareSliceCache(snapshot);
        foreach(var key in dirty.OrderBy(ChunkDistance))
        {
            long version=++_nextMeshVersion;_meshVersions[key]=version;
            foreach(var work in _meshWorkers)if(work.Request.Key==key)work.Cancellation.Cancel();
            _pendingMeshes.Remove(key);
            var request=new MeshRequest(key,version,snapshot,_sliceGeneration);
            if(RestoreSlice(request))continue;
            _pendingMeshes[key]=request;
            // Old geometry must never expose a hidden layer or intercept a ray after
            // a terrain edit. Local visual meshes remain until their replacement arrives.
            if(_chunks.TryGetValue(key,out var old))
            {
                if(all)old.Visible=false;
                foreach(var child in old.GetChildren())if(child is StaticBody3D body)body.CollisionLayer=0;
            }
        }
        if(SynchronousMeshing)FlushMeshes();else PumpMeshes();
    }
    private void StartMeshWorkers()
    {
        while(!_meshStopped&&_meshWorkers.Count<MeshConcurrency&&_pendingMeshes.Count>0)
        {
            var request=_pendingMeshes.Values.MinBy(r=>ChunkDistance(r.Key))!;_pendingMeshes.Remove(request.Key);
            var cancellation=new CancellationTokenSource();var token=cancellation.Token;
            var task=Task.Run(()=>TerrainMeshBuilder.Build(request.Snapshot,request.Key,token),token);
            _meshWorkers.Add(new(request,cancellation,task));
        }
    }
    private bool Current(MeshRequest request)=>_meshVersions.GetValueOrDefault(request.Key)==request.Version;
    private void CompleteMesh(MeshWork work)
    {
        _meshWorkers.Remove(work);work.Cancellation.Dispose();
        if(work.Task.IsCanceled){DiscardedMeshCount++;return;}
        if(work.Task.IsFaulted)
        {
            var error=work.Task.Exception!; // Observe every exception, including obsolete work.
            if(Current(work.Request))throw new InvalidOperationException("Terrain mesh worker failed",error);
            DiscardedMeshCount++;return;
        }
        var result=work.Task.GetAwaiter().GetResult();MeshWorkerThreads.Add(result.WorkerThread);
        if(!Current(work.Request)){DiscardedMeshCount++;return;}
        ApplyMesh(work.Request,result);
    }
    // Called every rendered frame, even if the world itself has not changed.
    private void PumpMeshes()
    {
        if(_meshStopped)return;
        ulong frame=Engine.GetProcessFrames();
        if(_submissionFrame!=frame){_submissionFrame=frame;_submissionMs=0;_submissions=0;}
        for(int i=_meshWorkers.Count-1;i>=0;i--)
        {
            if(_submissions>=4||_submissionMs>=2)break;
            if(!_meshWorkers[i].Task.IsCompleted)continue;
            long start=Stopwatch.GetTimestamp();
            CompleteMesh(_meshWorkers[i]);
            _submissions++;_submissionMs+=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        StartMeshWorkers();
    }
    // Explicit synchronization for deterministic geometry checks / --terrain-sync A/B.
    // Normal gameplay never waits for a worker on the main thread.
    public void FlushMeshes()
    {
        while(PendingMeshCount>0)
        {
            for(int i=_meshWorkers.Count-1;i>=0;i--)
            {
                try{_meshWorkers[i].Task.GetAwaiter().GetResult();}catch(OperationCanceledException){}
                CompleteMesh(_meshWorkers[i]);
            }
            if(_pendingMeshes.Count>0)
            {
                var request=_pendingMeshes.Values.MinBy(r=>ChunkDistance(r.Key))!;_pendingMeshes.Remove(request.Key);
                ApplyMesh(request,TerrainMeshBuilder.Build(request.Snapshot,request.Key));
            }
        }
    }
    private static ArrayMesh Upload(TerrainMeshArrays data,Material material)
    {
        var mesh=new ArrayMesh();AddSurface(mesh,data,material);return mesh;
    }
    private static void AddSurface(ArrayMesh mesh,TerrainMeshArrays data,Material material)
    {
        using var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=data.Vertices;arrays[(int)Mesh.ArrayType.Normal]=data.Normals;arrays[(int)Mesh.ArrayType.TexUV]=data.Uvs;arrays[(int)Mesh.ArrayType.Color]=data.Colors;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(mesh.GetSurfaceCount()-1,material);
    }
    private Material TerrainMaterial(TerrainSurface key,RegionProfile? region)
    {
        if(_terrainMaterials.TryGetValue(key,out var cached))return cached;
        var material=BlockTextures.TerrainMaterial(key.Id,key.Top,region);
        if(key.Section)material=Fog;
        else if(key.Rim){material=(StandardMaterial3D)material.Duplicate();material.AlbedoColor=new(.26f,.28f,.30f,1);}
        else if(key.Cut){material=(StandardMaterial3D)material.Duplicate();material.AlbedoColor*=new Color(.62f,.65f,.68f,1);}
        if(key.Underground&&!key.Section)
        {
            material=(StandardMaterial3D)material.Duplicate();material.ShadingMode=BaseMaterial3D.ShadingModeEnum.PerPixel;
            material.AlbedoColor=key.Rim?new(.18f,.20f,.22f,1):key.Cut?new(.34f,.40f,.45f,1):key.Top?new(.94f,.96f,1,1):new(.70f,.75f,.82f,1);
        }
        return _terrainMaterials[key]=material;
    }
    private static readonly StandardMaterial3D ContactLineMaterial=new(){AlbedoColor=new("20272e"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
    private void ApplyMesh(MeshRequest request,TerrainMeshResult result)
    {
        using var timing=_meshProbe?.Measure("view.terrain-submit")??default;
        var holder=new Node3D();
        if(result.Edges.Vertices.Length>0)holder.AddChild(new MeshInstance3D{Mesh=Upload(result.Edges,ContactLineMaterial),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        if(result.Surfaces.Count>0)
        {
            var mesh=new ArrayMesh();
            foreach(var surface in result.Surfaces)AddSurface(mesh,surface.Value,TerrainMaterial(surface.Key,request.Snapshot.Terrain.Region));
            holder.AddChild(new MeshInstance3D{Mesh=mesh});
            using var collisionTiming=_meshProbe?.Measure("view.terrain-collision-create")??default;
            var shape=new ConcavePolygonShape3D();shape.SetFaces(result.Collision);
            var body=new StaticBody3D();body.AddChild(new CollisionShape3D{Shape=shape});holder.AddChild(body);
        }
        RetireChunk(request.Key);
        AddChild(holder);_chunks[request.Key]=holder;AppliedMeshCount++;
        _chunkState[request.Key]=new(request.CacheGeneration,request.Snapshot.View.Top,request.Snapshot.View.RevealAll,request.Snapshot.View.WallCaps is null,result.Collision.Length);
        _meshProbe?.Count("view.terrain-chunks-applied");
    }
    private void ResetMeshPipeline()
    {
        foreach(var work in _meshWorkers)work.Cancellation.Cancel();
        _pendingMeshes.Clear();_meshVersions.Clear();_terrainMaterials.Clear();
        ClearSliceCache();_chunkState.Clear();
        _snapshotSource=null;_snapshotTerrain=null;
    }
    public override void _ExitTree()
    {
        _meshStopped=true;ResetMeshPipeline();
        foreach(var work in _meshWorkers)
        {
            // Workers own only snapshots. They may finish after the scene is freed.
            _=work.Task.ContinueWith(t=>{_ = t.Exception;work.Cancellation.Dispose();},TaskScheduler.Default);
        }
        _meshWorkers.Clear();
    }
}
