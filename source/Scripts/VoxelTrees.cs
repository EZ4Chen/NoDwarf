using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class TerrainView
{
    private static readonly Dictionary<string,Mesh> TreeMeshes=new();
    private DemoWorld? _fallWorld;
    private float _fallTop=float.NaN;
    private readonly List<(Node3D Root,Vector3 Axis,double Age)> _falls=new();
    private static string TreeMaterial(TreeState tree,bool leaf)=>"nodwarf:tree_"+(tree.Species=="acacia_sapling"?"acacia":tree.Species=="jungle_sapling"?"jungle":tree.Species=="birch_sapling"?"birch":tree.Species=="pine_sapling"?"spruce":"oak")+(leaf?"_leaves":"_log");
    private static Mesh TreeMesh(string id)
    {
        if(TreeMeshes.TryGetValue(id,out var mesh))return mesh;
        var holder=new Node3D();BlockTextures.Box(holder,Vector3.Zero,Vector3.One,id);
        mesh=holder.GetChild<MeshInstance3D>(0).Mesh;TreeMeshes[id]=mesh;holder.Free();return mesh;
    }
    private static MultiMesh TreeInstances(Node parent,string id,List<Transform3D> transforms)
    {
        var mesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=TreeMesh(id),InstanceCount=transforms.Count};
        for(int i=0;i<transforms.Count;i++)mesh.SetInstanceTransform(i,transforms[i]);
        parent.AddChild(new MultiMeshInstance3D{Multimesh=mesh});return mesh;
    }
    private void BuildVoxelTrees(DemoWorld world,float clip)
    {
        _treeSlices.Clear();_treeBodies.Clear();_treeClip=float.NaN;
        var batches=new Dictionary<string,List<Transform3D>>();
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
            if(!batches.TryGetValue(cell.Value.Id,out var batch))batches[cell.Value.Id]=batch=new();
            var c=cell.Key;float height=1;batch.Add(new(Basis.Identity.Scaled(new Vector3(1,height,1)),Point(c.X,c.Level+height/2,c.Z)));
        }
        foreach(var batch in batches)
        {var mesh=TreeInstances(_trees!,batch.Key,batch.Value);_treeSlices.Add(new(mesh,batch.Value.Select(t=>t.Origin.Y+.5f).ToArray()));}
        UpdateTreeClip(clip);
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
