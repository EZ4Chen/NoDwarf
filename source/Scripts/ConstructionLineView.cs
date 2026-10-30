using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private bool LineBuildTool(string tool)=>ToolDefinition(tool) is {} d&&_world.Content.Block(d.Block).Shape==BlockShape.Solid;
    private Cell? LineEndPick(Vector2 screen,Cell anchor)
    {
        // Floor placement selects through the floor's upper plane, while its voxel is one level below.
        int plane=(_foundation is not null||_heightSelected)&&_placement<0?ActiveLevel:anchor.Level;
        return PlanePick(screen,plane) is Cell end?end with{Level=anchor.Level}:null;
    }
    private HashSet<Cell> SolidPlanCells()=>_world.Jobs.Where(j=>j.BuildingWork&&_world.BuildBlock(j).Shape==BlockShape.Solid).Select(j=>j.Cell).Concat(_previewWorld==_world?_previewSolids:Enumerable.Empty<Cell>()).ToHashSet();
    private void DrawPlanBlock(Node parent,Cell cell,BlockDefinition block,BlockFaces faces)
    {
        float height=Math.Clamp(CurrentView.CellTop(cell)-cell.Level,0,1);
        bool core=_world.Content.Sources.TryGetValue("spatial/Blocks/"+block.Id,out var source)&&source.Pack=="base";
        BlockTextures.Box(parent,WorldPosition(cell)+Vector3.Up*height/2,new(1,height,1),block.Id,faces,core?null:Mat(block.Color));
    }
    private void CommitBuildLine(Cell from,Cell to)
    {
        var definition=ToolDefinition(_tool)!;var cells=ConstructionLine.Cells(from,to);int count=0;
        foreach(var cell in cells)
        {
            if(_developerMode&&_creative)
            {
                if(!_world.DeveloperPlace(cell,definition.Handler,_facing))continue;
                _world.Buildings[cell]=new(cell,definition.Handler,_facing){DefinitionId=definition.Id};count++;
            }
            else if(_world.QueueBuild(cell,definition.Id,announce:false,facing:_facing))count++;
        }
        _world.Log($"{definition.Name}：已{(_developerMode&&_creative?"放置":"规划")} {count}/{cells.Length} 格"+
            (count<cells.Length?"；跳过已有规划、无法放置或材料不足的位置。":"。"));
        SyncWorld();RefreshUi();
    }
    private void DrawBuildLine(Cell from,Cell to)
    {
        var definition=ToolDefinition(_leftTool)!;var cells=ConstructionLine.Cells(from,to);
        SetPreviewSolids(cells);var plans=SolidPlanCells();
        var accepted=new HashSet<Cell>();
        var stock=definition.Cost.ToDictionary(p=>p.Key,p=>_world.Available(p.Key));int validCount=0;
        foreach(var cell in cells)
        {
            bool valid=_developerMode&&_creative?_world.CanDeveloperPlace(cell,definition.Handler,_facing)
                :_world.CanPlace(cell,definition.Handler,_facing,accepted)&&definition.Cost.All(p=>stock[p.Key]>=p.Value);
            if(valid){accepted.Add(cell);validCount++;if(!(_developerMode&&_creative))foreach(var cost in definition.Cost)stock[cost.Key]-=cost.Value;}
            var shape=new Node3D();_hover.AddChild(shape);
            DrawPlanBlock(shape,cell,_world.Content.Block(definition.Block),BlockExposure.PlanFaces(_world,CurrentView,cell,plans));
            foreach(var mesh in shape.GetChildren().OfType<MeshInstance3D>())
            {mesh.MaterialOverride=PreviewMaterial(!valid);mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;}
        }
        _hint.Text=$"{definition.Name} · {cells.Length} 格，可{(_developerMode&&_creative?"放置":"规划")} {validCount} 格 · 松开确认；右键或 Esc 取消";
    }
}
