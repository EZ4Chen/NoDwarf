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
    private void BuildVoxelTrees(DemoWorld world,float clip)
    {
        TreeBuildCount++;
        _treeSlices.Clear();_treeBodies.Clear();_treeClip=float.NaN;
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
            var body=new StaticBody3D{CollisionLayer=1,CollisionMask=0,Position=Point(tree.Cell.X,tree.Cell.Level+.5f,tree.Cell.Z)};
            body.SetMeta("cell",new Vector3I(tree.Cell.X,tree.Cell.Level,tree.Cell.Z));body.SetMeta("tree_root",true);
            body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=Vector3.One}});_trees!.AddChild(body);_treeBodies.Add((body,tree.Cell.Level));
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
                cells.TryGetValue(n,out var neighbor)&&!neighbor.Leaf?n.Level+1:float.NegativeInfinity);
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
        foreach(var batch in batches)
        {
            var mesh=TreeInstances(_trees!,batch.Key.Id,batch.Value);
            _treeSlices.Add(new(mesh,batch.Value.Select(t=>t.Origin.Y+.5f).ToArray(),Root:_trees!.GetChild<Node3D>(_trees.GetChildCount()-1)));
        }
        foreach(var batch in logs)
        {
            var sorted=batch.Value.OrderBy(t=>t.Top).ToArray();
            var mm=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=TreeFaceMesh(batch.Key.Id,batch.Key.Top),InstanceCount=sorted.Length};
            for(int i=0;i<sorted.Length;i++)mm.SetInstanceTransform(i,sorted[i].Transform);
            var root=new MultiMeshInstance3D{Multimesh=mm};_trees!.AddChild(root);
            _treeSlices.Add(new(mm,sorted.Select(t=>t.Top).ToArray(),batch.Key.Cap!=int.MinValue,root));
        }
        UpdateTreeClip(clip);
        _trees!.Visible=!OS.GetCmdlineUserArgs().Contains("--diag-hide-forest");
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
        PumpMeshes();
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
