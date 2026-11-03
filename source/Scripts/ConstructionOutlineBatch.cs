using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    // Cache the 15 non-empty perimeter variants. Adjacent cells share no internal
    // bars; remaining bars meet exactly at cell boundaries, including chunk edges.
    private static readonly Dictionary<int,ArrayMesh> ConstructionOutlineMeshes=new();
    private readonly Dictionary<(int X,int Z,int Mask),MultiMeshInstance3D> _constructionOutlineBatches=new();
    private bool _constructionOutlinesDirty;
    private static readonly Cell[] OutlineNeighbors=CellBoundary.Neighbors;

    private static ArrayMesh ConstructionOutlineMesh(int mask)
    {
        if(ConstructionOutlineMeshes.TryGetValue(mask,out var mesh))return mesh;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,VertexColorUseAsAlbedo=true});
        using var xBar=new BoxMesh{Size=new(.025f,.025f,1.025f)};
        using var zBar=new BoxMesh{Size=new(1.025f,.025f,.025f)};
        for(int side=0;side<4;side++)
        {
            if((mask&(1<<side))==0)continue;
            var delta=OutlineNeighbors[side];
            surface.AppendFrom(side<2?xBar:zBar,0,new Transform3D(Basis.Identity,new Vector3(delta.X*.5f,0,delta.Z*.5f)));
        }
        return ConstructionOutlineMeshes[mask]=surface.Commit();
    }
    private int ConstructionOutlineMask(ConstructionVisual visual,IReadOnlySet<Cell> solids)
    {
        if(visual.Selected||!solids.Contains(visual.Job.Cell))return 15;
        return CellBoundary.Mask(visual.Job.Cell,solids.Contains);
    }
    private HashSet<Cell> VisibleOutlineSolids()=>_constructionVisuals.Values
        .Where(v=>_world.BuildBlock(v.Job).Shape==BlockShape.Solid).Select(v=>v.Job.Cell).ToHashSet();
    private Color ConstructionOutlineColor(ConstructionVisual visual)=>visual.Selected?Accent:new Color("6ab7d6");
    private void RemoveConstructionVisual(int id)
    {
        if(_constructionVisuals.Remove(id))_constructionOutlinesDirty=true;
    }
    private void ResetConstructionOutlines()
    {
        // The batch nodes are children of _ghosts and are freed by its reset.
        _constructionOutlineBatches.Clear();_constructionOutlinesDirty=true;
    }
    private void SyncConstructionOutlines()
    {
        if(!_constructionOutlinesDirty)return;
        _constructionOutlinesDirty=false;
        var solids=VisibleOutlineSolids();
        foreach(var visual in _constructionVisuals.Values)
        {
            visual.OutlineMask=ConstructionOutlineMask(visual,solids);
            visual.OutlineBatch=null;visual.OutlineIndex=-1;
        }
        var groups=_constructionVisuals.Values.Where(v=>v.OutlineMask!=0)
            .GroupBy(v=>(v.Job.Cell.X/16,v.Job.Cell.Z/16,v.OutlineMask)).ToDictionary(g=>g.Key,g=>g.ToArray());
        foreach(var key in _constructionOutlineBatches.Keys.ToArray())
            if(!groups.ContainsKey(key))
            {RemoveVisual(_constructionOutlineBatches[key]);_constructionOutlineBatches.Remove(key);}
        foreach(var (key,visuals) in groups)
        {
            if(!_constructionOutlineBatches.TryGetValue(key,out var node))
            {
                node=new MultiMeshInstance3D{Name="ConstructionOutlines",Multimesh=new MultiMesh{
                    TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=ConstructionOutlineMesh(key.Item3)}};
                _ghosts.AddChild(node);_constructionOutlineBatches.Add(key,node);
            }
            var batch=node.Multimesh;
            if(batch.InstanceCount!=visuals.Length)batch.InstanceCount=visuals.Length;
            for(int i=0;i<visuals.Length;i++)
            {
                var visual=visuals[i];visual.OutlineBatch=batch;visual.OutlineIndex=i;
                batch.SetInstanceTransform(i,new Transform3D(Basis.Identity,WorldPosition(visual.Job.Cell)+Vector3.Up*.025f));
                batch.SetInstanceColor(i,ConstructionOutlineColor(visual));
            }
        }
    }
}
