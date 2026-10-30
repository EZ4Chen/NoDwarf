using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class TerrainView
{
    private static readonly Dictionary<string,Mesh> TreeMeshes=new();
    public int TreeBuildCount {get;private set;}
    private DemoWorld? _fallWorld;
    private float _fallTop=float.NaN;
    private readonly List<(Node3D Root,Vector3 Axis,double Age)> _falls=new();
    private static string TreeMaterial(TreeState tree,bool leaf)=>"nodwarf:tree_"+(tree.Species=="acacia_sapling"?"acacia":tree.Species=="jungle_sapling"?"jungle":tree.Species=="birch_sapling"?"birch":tree.Species=="pine_sapling"?"spruce":"oak")+(leaf?"_leaves":"_log");
    private static Mesh TreeMesh(string id,BlockFaces faces=BlockFaces.All)
    {
        string cacheKey=id+":"+(int)faces;
        if(TreeMeshes.TryGetValue(cacheKey,out var mesh))return mesh;
        var holder=new Node3D();BlockTextures.Box(holder,Vector3.Zero,Vector3.One,id,faces);
        var original=holder.GetChild<MeshInstance3D>(0).Mesh;
        // Identical leaf faces used to be six separate draw surfaces. Keep the
        // pack UVs/normals, but merge faces which share a material.
        var surfaces=new Dictionary<ulong,(SurfaceTool Tool,StandardMaterial3D Material)>();
        for(int i=0;i<original.GetSurfaceCount();i++)
        {
            var source=(StandardMaterial3D)original.SurfaceGetMaterial(i);ulong key=source.GetInstanceId();
            if(!surfaces.TryGetValue(key,out var surface))
            {
                var material=(StandardMaterial3D)source.Duplicate();
                material.CullMode=BaseMaterial3D.CullModeEnum.Back;
                material.TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
                var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetMaterial(material);
                surfaces[key]=surface=(tool,material);
            }
            surface.Tool.AppendFrom(original,i,Transform3D.Identity);
        }
        var merged=new ArrayMesh();foreach(var surface in surfaces.Values){surface.Tool.Commit(merged);surface.Tool.Dispose();}
        mesh=merged;TreeMeshes[cacheKey]=mesh;holder.Free();return mesh;
    }
    private static MultiMesh TreeInstances(Node parent,string id,List<Transform3D> transforms,BlockFaces faces=BlockFaces.All)
    {
        var mesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=TreeMesh(id,faces),InstanceCount=transforms.Count};
        for(int i=0;i<transforms.Count;i++)mesh.SetInstanceTransform(i,transforms[i]);
        parent.AddChild(new MultiMeshInstance3D{Multimesh=mesh});return mesh;
    }
    public sealed record ForestBatch(string Id,bool Leaf,bool Top,bool Cap,float[] Buffer,float[] Heights);
    public sealed record ForestData(int TreeRevision,int TerrainRevision,long NavigationVersion,Cell[] Roots,ForestBatch[] Batches,Cell[] Contacts);
    private (DemoWorld World,ForestData Data)? _preparedForest;
    public void UsePreparedForest(DemoWorld world,ForestData data)
    {
        if(_lastTerrain==world.Terrain&&_lastTrees==world.TreeRevision&&_trees is not null)return;
        _preparedForest=(world,data);
    }
    // Called while the loaded world is owned exclusively by the loader. No Godot
    // objects or scene nodes are created here; only managed arrays and math structs.
    public static ForestData PrepareForest(DemoWorld world,WorldView view=default)
    {
        // MultiMesh culling is per batch, not per voxel. Spatial batches let the
        // renderer discard off-screen forest, including its shadow work.
        var batches=new Dictionary<(string Id,int X,int Z),List<Transform3D>>();
        var logs=new Dictionary<(string Id,bool Top,int X,int Z,int Cap),List<(Transform3D Transform,float Top)>>();
        var cells=new Dictionary<Cell,(string Id,bool Leaf)>();
        foreach(var tree in world.Trees.Values)
        {
            foreach(var voxel in TreeShape.Generate(world.Terrain.Seed,tree))
            {
                var c=tree.Cell+voxel.Offset;
                // A canopy never draws through later player construction or buried terrain.
                if(world.Buildings.ContainsKey(c)||world.NaturalSolid(c))continue;
                string id=TreeMaterial(tree,voxel.Leaf);
                if(!cells.TryGetValue(c,out var old)||old.Leaf&&!voxel.Leaf)cells[c]=(id,voxel.Leaf);
            }

        }
        foreach(var cell in cells.OrderBy(p=>p.Key.Level))
        {
            var c=cell.Key;
            if(cell.Value.Leaf)
            {
                var key=(cell.Value.Id,c.X/32,c.Z/32);
                if(!batches.TryGetValue(key,out var batch))batches[key]=batch=new();
                batch.Add(new(Basis.Identity,Point(c.X,c.Level+.5f,c.Z)));continue;
            }
            var faces=BlockExposure.Faces(c,c.Level+1,n=>
            {
                if(cells.TryGetValue(n,out var neighbor)&&!neighbor.Leaf)return n.Level+1;
                bool opaque=world.NaturalSolid(n)&&!world.NaturalRamp(n)||world.Buildings.TryGetValue(n,out var building)&&BlockCatalog.IsWall(building.Kind);
                // Only local wall cuts matter here; ordinary floor clipping uses reusable cap batches.
                float cap=view.WallCaps is not null&&view.WallCaps.TryGetValue(n,out var top)?top:float.PositiveInfinity;
                return opaque&&cap>n.Level?Math.Min(n.Level+1,cap):float.NegativeInfinity;
            });
            void AddFace(BlockFaces face,Cell delta,int cap=int.MinValue)
            {
                var key=(cell.Value.Id,face==BlockFaces.Top,c.X/32,c.Z/32,cap);
                if(!logs.TryGetValue(key,out var batch))logs[key]=batch=new();
                var basis=face switch
                {
                    BlockFaces.Top=>new Basis(new Vector3(0,0,1),new(-1,0,0),new(0,-1,0)),
                    BlockFaces.Bottom=>new Basis(new Vector3(0,0,-1),new(-1,0,0),new(0,1,0)),
                    BlockFaces.South=>new Basis(new Vector3(-1,0,0),new(0,1,0),new(0,0,-1)),
                    BlockFaces.East=>new Basis(new Vector3(0,0,1),new(0,1,0),new(-1,0,0)),
                    BlockFaces.West=>new Basis(new Vector3(0,0,-1),new(0,1,0),new(1,0,0)),
                    _=>Basis.Identity
                };
                batch.Add((new(basis,Point(c.X+delta.X*.5f,c.Level+.5f+delta.Level*.5f,c.Z+delta.Z*.5f)),c.Level+1));
            }
            foreach(var (face,delta) in BlockExposure.Neighbors)if((faces&face)!=0)AddFace(face,delta);
            if((faces&BlockFaces.Top)==0)AddFace(BlockFaces.Top,new(0,0,1),c.Level+1);
        }
        var output=new List<ForestBatch>();
        foreach(var batch in batches)
            output.Add(new(batch.Key.Id,true,false,false,PackTreeTransforms(batch.Value),batch.Value.Select(t=>t.Origin.Y+.5f).ToArray()));
        foreach(var batch in logs)
        {
            var sorted=batch.Value.OrderBy(t=>t.Top).ToArray();
            output.Add(new(batch.Key.Id,false,batch.Key.Top,batch.Key.Cap!=int.MinValue,PackTreeTransforms(sorted.Select(t=>t.Transform).ToArray()),sorted.Select(t=>t.Top).ToArray()));
        }
        return new(world.TreeRevision,world.Terrain.Revision,world.Navigation.Version,world.Trees.Keys.ToArray(),output.ToArray(),cells.Where(p=>!p.Value.Leaf).SelectMany(p=>BlockExposure.Neighbors.Select(n=>p.Key+n.Delta)).Distinct().ToArray());
    }
    internal static float[] PackTreeTransforms(IReadOnlyList<Transform3D> transforms)
    {
        var buffer=new float[transforms.Count*12];
        for(int i=0;i<transforms.Count;i++)
        {
            var t=transforms[i];int p=i*12;
            buffer[p]=t.Basis.X.X;buffer[p+1]=t.Basis.Y.X;buffer[p+2]=t.Basis.Z.X;buffer[p+3]=t.Origin.X;
            buffer[p+4]=t.Basis.X.Y;buffer[p+5]=t.Basis.Y.Y;buffer[p+6]=t.Basis.Z.Y;buffer[p+7]=t.Origin.Y;
            buffer[p+8]=t.Basis.X.Z;buffer[p+9]=t.Basis.Y.Z;buffer[p+10]=t.Basis.Z.Z;buffer[p+11]=t.Origin.Z;
        }
        return buffer;
    }
    private readonly HashSet<Cell> _treeContacts=new();
    private void BuildVoxelTrees(DemoWorld world,WorldView view)
    {
        using var timing=world.Probe?.Measure("view.forest-build")??default;
        TreeBuildCount++;
        _treeSlices.Clear();_treeBodies.Clear();_treeClip=float.NaN;
        var prepared=_preparedForest;_preparedForest=null;
        bool ready=prepared is {} p&&p.World==world&&p.Data.TreeRevision==world.TreeRevision&&p.Data.TerrainRevision==world.Terrain.Revision&&p.Data.NavigationVersion==world.Navigation.Version&&(view.WallCaps is null||view.WallCaps.Count==0);
        var data=ready?prepared!.Value.Data:PrepareForest(world,view);
        _treeContacts.Clear();_treeContacts.UnionWith(data.Contacts);
        _forestUpload=new(_trees!,data);
        MaxForestUploadMs=0;ForestUploadFrames=0;
        UpdateTreeClip(view.Top);
        _trees!.Visible=!OS.GetCmdlineUserArgs().Contains("--diag-hide-forest");
        PumpForestUploads(!ready||SynchronousMeshing);
    }
    private sealed class ForestUpload(Node3D root,ForestData data)
    {public Node3D Root=root;public ForestData Data=data;public int Roots,Batches;}
    private ForestUpload? _forestUpload;
    public int PendingForestUploads=>_forestUpload is {} work?work.Data.Roots.Length-work.Roots+work.Data.Batches.Length-work.Batches:0;
    public int ForestUploadFrames {get;private set;}
    public double MaxForestUploadMs {get;private set;}
    private ulong _forestUploadFrame=ulong.MaxValue;
    private void PumpForestUploads(bool flush=false)
    {
        if(_forestUpload is not {} work)return;
        if(work.Root!=_trees||!IsInstanceValid(work.Root)){_forestUpload=null;return;}
        ulong frame=Engine.GetProcessFrames();if(!flush&&frame==_forestUploadFrame)return;
        _forestUploadFrame=frame;ForestUploadFrames++;
        double budget=Math.Clamp(ProjectSettings.GetSetting("performance/threads/forest_upload_budget_ms",3.0).AsDouble(),.25,16);
        long start=System.Diagnostics.Stopwatch.GetTimestamp();
        do
        {
            if(work.Roots<work.Data.Roots.Length)
            {
                var cell=work.Data.Roots[work.Roots++];
                var body=new StaticBody3D{CollisionLayer=cell.Level<_treeClip?1u:0u,CollisionMask=0,Position=Point(cell.X,cell.Level+.5f,cell.Z)};
                body.SetMeta("cell",new Vector3I(cell.X,cell.Level,cell.Z));body.SetMeta("tree_root",true);
                using(var collisionShape=new BoxShape3D{Size=Vector3.One})body.AddChild(new CollisionShape3D{Shape=collisionShape});work.Root.AddChild(body);_treeBodies.Add((body,cell.Level));
            }
            else if(work.Batches<work.Data.Batches.Length)
            {
                var batch=work.Data.Batches[work.Batches++];
                var mm=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=batch.Leaf?TreeMesh(batch.Id):TreeFaceMesh(batch.Id,batch.Top),InstanceCount=batch.Heights.Length};
                RenderingServer.MultimeshSetBuffer(mm.GetRid(),batch.Buffer);
                var root=new MultiMeshInstance3D{Multimesh=mm};work.Root.AddChild(root);
                var slice=new HeightBatch(mm,batch.Heights,batch.Cap,root);_treeSlices.Add(slice);slice.ShowBelow(_treeClip);
            }
            else{_forestUpload=null;break;}
        }while(flush||System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds<budget);
        MaxForestUploadMs=Math.Max(MaxForestUploadMs,System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    // A single quad is instanced in different orientations: exposed-face masks
    // must not create a separate draw batch for every possible mask combination.
    private static Mesh TreeFaceMesh(string id,bool top)
    {
        string key="face:"+id+":"+top;if(TreeMeshes.TryGetValue(key,out var found))return found;
        var material=(StandardMaterial3D)BlockTextures.Material(id,top).Duplicate();
        material.CullMode=BaseMaterial3D.CullModeEnum.Back;material.TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps;
        using var tool=new SurfaceTool();tool.Begin(Mesh.PrimitiveType.Triangles);tool.SetMaterial(material);
        BlockTextures.Quad(tool,new(-.5f,.5f,0),new(.5f,.5f,0),new(.5f,-.5f,0),new(-.5f,-.5f,0));
        tool.GenerateNormals();return TreeMeshes[key]=tool.Commit();
    }
    private void CaptureTreeFalls(DemoWorld world,WorldView view)
    {
        if(_fallWorld!=world||_fallTop!=view.Top){foreach(var f in _falls)if(IsInstanceValid(f.Root))f.Root.QueueFree();_falls.Clear();_fallWorld=world;_fallTop=view.Top;}
        while(world.TreeFalls.TryDequeue(out var fall))
        {
            if(!view.Visible(world,fall.Tree.Cell))continue;
            var root=new Node3D{Position=Point(fall.Tree.Cell.X,fall.Tree.Cell.Level,fall.Tree.Cell.Z)};AddChild(root);
            var groups=TreeShape.Generate(fall.Seed,fall.Tree).Where(v=>fall.Tree.Cell.Level+v.Offset.Level<view.Top).GroupBy(v=>v.Leaf);
            foreach(var group in groups)TreeInstances(root,TreeMaterial(fall.Tree,group.Key),group.Select(v=>new Transform3D(Basis.Identity,new Vector3(v.Offset.X,v.Offset.Level+.5f,v.Offset.Z))).ToList());
            float angle=(TreeShape.Seed(fall.Seed,fall.Tree.Cell)%360)*Mathf.Pi/180;
            var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
            if(fall.Tree.Cell.X<16||fall.Tree.Cell.X>238||fall.Tree.Cell.Z<16||fall.Tree.Cell.Z>238)direction=new Vector3(127-fall.Tree.Cell.X,0,127-fall.Tree.Cell.Z).Normalized();
            _falls.Add((root,Vector3.Up.Cross(direction).Normalized(),0));
        }
    }
    public override void _Process(double delta)
    {
        PumpMeshes();PumpForestUploads();
        for(int i=_falls.Count-1;i>=0;i--)
        {
            var f=_falls[i];if(!IsInstanceValid(f.Root)){_falls.RemoveAt(i);continue;}
            double age=f.Age+delta;float t=(float)Math.Clamp(age/1.15,0,1);
            f.Root.Basis=new Basis(f.Axis,t*t*Mathf.Pi*.5f);
            if(age>1.15)f.Root.Scale=Vector3.One*(float)Math.Max(.01,1-(age-1.15)/.3);
            if(age>=1.45){f.Root.QueueFree();_falls.RemoveAt(i);}else _falls[i]=(f.Root,f.Axis,age);
        }
    }
}
