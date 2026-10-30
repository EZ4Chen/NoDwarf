using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private readonly HashSet<Cell> _previewSolids=new();
    private DemoWorld? _previewWorld;
    private void SetPreviewSolids(IEnumerable<Cell> cells)
    {
        var next=cells.Where(c=>DemoWorld.Inside(c)&&VisibleAt(c)).ToHashSet();
        if(_previewWorld==_world&&_previewSolids.SetEquals(next))return;
        _previewWorld=_world;_previewSolids.Clear();_previewSolids.UnionWith(next);
        // Only visual masks change. No jobs, material reservations or navigation edits.
        if(_visualWorld==_world)SyncStructuresAndPlans();
    }
    private void ClearHover()
    {Clear(_hover);SetPreviewSolids(Array.Empty<Cell>());}
    private void DrawBlueprintPreview(Cell origin)
    {
        var parts=_world.Blueprint.Where(p=>DemoWorld.Inside(origin+p.Offset)).ToArray();
        SetPreviewSolids(parts.Where(p=>_world.Content.Block(_world.BlueprintDefinition(p).Block).Shape==BlockShape.Solid).Select(p=>origin+p.Offset));
        var solids=SolidPlanCells();
        foreach(var part in parts)
        {
            var cell=origin+part.Offset;var definition=_world.BlueprintDefinition(part);
            var shape=new Node3D();_hover.AddChild(shape);
            if(_world.Content.Block(definition.Block).Shape==BlockShape.Solid)
                DrawPlanBlock(shape,cell,_world.Content.Block(definition.Block),BlockExposure.PlanFaces(_world,CurrentView,cell,solids));
            else DrawBuilding(shape,cell,part.Kind,facing:part.Facing,fullHeight:true,definitionId:definition.Id);
            BoxSurfaceCulling.Apply(shape);
            foreach(var mesh in shape.GetChildren().OfType<MeshInstance3D>())
            {mesh.MaterialOverride=PreviewMaterial(false);mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;}
        }
    }
}
