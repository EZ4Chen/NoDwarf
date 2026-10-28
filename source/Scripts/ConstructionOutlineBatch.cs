using Godot;

namespace NoDwarf;

public partial class Main
{
    // One shared four-bar mesh, instanced by 16-cell chunk. Keep the original
    // geometry/shadows while avoiding four draw submissions per construction job.
    private static ArrayMesh? _constructionOutlineMesh;
    private readonly Dictionary<(int X,int Z),MultiMeshInstance3D> _constructionOutlineBatches=new();
    private bool _constructionOutlinesDirty;

    private static ArrayMesh ConstructionOutlineMesh()
    {
        if(_constructionOutlineMesh is not null)return _constructionOutlineMesh;
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,VertexColorUseAsAlbedo=true});
        using var xBar=new BoxMesh{Size=new(.025f,.025f,.98f)};
        using var zBar=new BoxMesh{Size=new(.98f,.025f,.025f)};
        foreach(float side in new[]{-.48f,.48f})
        {
            surface.AppendFrom(xBar,0,new Transform3D(Basis.Identity,new Vector3(side,0,0)));
            surface.AppendFrom(zBar,0,new Transform3D(Basis.Identity,new Vector3(0,0,side)));
        }
        return _constructionOutlineMesh=surface.Commit();
    }
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
        var groups=_constructionVisuals.Values.GroupBy(v=>(v.Job.Cell.X/16,v.Job.Cell.Z/16)).ToDictionary(g=>g.Key,g=>g.ToArray());
        foreach(var key in _constructionOutlineBatches.Keys.ToArray())
            if(!groups.ContainsKey(key))
            {RemoveVisual(_constructionOutlineBatches[key]);_constructionOutlineBatches.Remove(key);}
        foreach(var (key,visuals) in groups)
        {
            if(!_constructionOutlineBatches.TryGetValue(key,out var node))
            {
                node=new MultiMeshInstance3D{Name="ConstructionOutlines",Multimesh=new MultiMesh{
                    TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=ConstructionOutlineMesh()}};
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
    private void CheckConstructionOutlines()
    {
        if(_constructionOutlineBatches.Values.Sum(n=>n.Multimesh.InstanceCount)!=_constructionVisuals.Count)
            throw new InvalidOperationException("Construction outlines retained a removed/hidden plan");
        foreach(var visual in _constructionVisuals.Values)
        {
            var batch=visual.OutlineBatch??throw new InvalidOperationException("Construction outline missing");
            if(visual.OutlineIndex<0||visual.OutlineIndex>=batch.InstanceCount)
                throw new InvalidOperationException("Construction outline has an invalid batch slot");
            // Godot's headless dummy renderer returns identity/black for these
            // getters. Validate renderer state in the real-window preview test.
            if(DisplayServer.GetName()=="headless")continue;
            if(!batch.GetInstanceTransform(visual.OutlineIndex).Origin.IsEqualApprox(WorldPosition(visual.Job.Cell)+Vector3.Up*.025f))
                throw new InvalidOperationException("Construction outline moved away from its selectable plan");
            // Compatibility stores instance colours at reduced precision.
            var actual=batch.GetInstanceColor(visual.OutlineIndex);var expected=ConstructionOutlineColor(visual);
            if(Math.Abs(actual.R-expected.R)>1f/255||Math.Abs(actual.G-expected.G)>1f/255||Math.Abs(actual.B-expected.B)>1f/255||Math.Abs(actual.A-expected.A)>1f/255)
                throw new InvalidOperationException("Construction selection tint lost after batch update");
        }
    }
}
