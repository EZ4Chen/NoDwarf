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
    private async void ReviewSpatialContent()
    {
        try
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(!_toolButtons.TryGetValue("copper:chair",out var button)||!_toolButtons.ContainsKey("copper:furnace"))throw new InvalidOperationException("Mod buildings missing from architect menu");
            if(string.IsNullOrWhiteSpace(button.AccessibilityDescription)||!button.Text.Contains("铜椅"))throw new InvalidOperationException("Mod building label or accessible description missing");
            _world=new DemoWorld(flat:true,content:_world.Content);_world.Jobs.Clear();_world.SpawnItem("copper:ingot",2,new(10,10));
            _developerMode=false;_creative=false;_clock.Paused=true;
            button.EmitSignal(BaseButton.SignalName.Pressed);
            if(_tool!="copper:chair")throw new InvalidOperationException("Architect selected handler instead of definition ID");
            var target=new Cell(15,15);Act(target);
            var job=_world.Jobs.Single(j=>j.Cell==target);
            if(job.BuildDefinitionId!="copper:chair"||job.Materials.Any(m=>m.Item!="copper:ingot"))throw new InvalidOperationException("UI construction lost custom definition/cost");
            MintStatRows("copper:chair");
            var shape=new Node3D();AddChild(shape);DrawBuilding(shape,target,BuildKind.Chair,definitionId:"copper:chair");
            if(!shape.GetChildren().OfType<MeshInstance3D>().Any())throw new InvalidOperationException("Custom building meshes must remain available to selection and construction");shape.Free();
            _world=DemoWorld.LoadJson(_world.SaveJson(),content:_world.Content);
            if(_world.Jobs.Single(j=>j.Cell==target).BuildDefinitionId!="copper:chair")throw new InvalidOperationException("UI plan did not survive load");
            GD.Print("SPATIAL_UI_OK: scanned buildings, accessible menu, definition selection, copper costs, model and saved plan");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }

}
