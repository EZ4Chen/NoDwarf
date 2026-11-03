using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private void CheckConstructionOutlines()
    {
        var solids=VisibleOutlineSolids();
        if(_constructionOutlineBatches.Values.Sum(n=>n.Multimesh.InstanceCount)!=_constructionVisuals.Values.Count(v=>v.OutlineMask!=0))
            throw new InvalidOperationException("Construction outlines retained a removed/hidden plan");
        foreach(var visual in _constructionVisuals.Values)
        {
            if(visual.OutlineMask!=ConstructionOutlineMask(visual,solids))throw new InvalidOperationException("Construction outline contains a shared edge or lost an exterior edge");
            if(visual.OutlineMask==0)
            {if(visual.OutlineBatch is not null)throw new InvalidOperationException("Interior plan retained an outline");continue;}
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
