using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private BuildingDefinition? ToolDefinition(string tool)
        =>_world.Content.Buildings.GetValueOrDefault(Enum.TryParse<BuildKind>(tool,out var kind)&&Enum.IsDefined(kind)?BlockCatalog.Id(kind):tool);
    private bool TryBuildTool(string tool,out BuildKind kind)
    {var d=ToolDefinition(tool);kind=d?.Handler??default;return d is not null;}
    private void DrawDefinitionBuilding(Node parent,Cell cell,BuildingDefinition definition,bool ghost,int facing,bool fullHeight)
    {
        int first=parent.GetChildCount();
        DrawBuilding(parent,cell,definition.Handler,ghost,facing,fullHeight);
        if(!ghost)
            foreach(var mesh in parent.GetChildren().Skip(first).OfType<MeshInstance3D>())
                mesh.MaterialOverride=Mat(_world.Content.Block(definition.Block).Color);
    }
}
