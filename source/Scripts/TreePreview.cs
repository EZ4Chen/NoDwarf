using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureVoxelTreePreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;CreatePeople();
            var tree=_world.Trees.Values.Where(t=>t.Species=="sapling").OrderBy(t=>Math.Abs(t.Cell.X-127)+Math.Abs(t.Cell.Z-127)).First();
            _foundation=null;_heightSelected=false;_layerOffset=0;_focus=WorldPosition(tree.Cell)+Vector3.Up*3;_camera.Size=20;_cameraYaw=0;PositionCamera();SetViewMode(0);SyncWorld();RefreshUi();
            async Task Snap(string name)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+name+".png"))!=Error.Ok)throw new IOException("Tree screenshot");
            }
            await Snap("voxel-trees-standing");
            var screen=_camera.UnprojectPosition(WorldPosition(tree.Cell)+Vector3.Up*.4f);
            if(PickTarget(screen)!=tree.Cell||DigPick(screen)!=tree.Cell)throw new InvalidOperationException("Tree base picking");
            int wood=_world.Total("wood");SelectTool("dig");Act(tree.Cell);SyncWorld();
            if(_world.Trees.ContainsKey(tree.Cell)||_world.Total("wood")!=wood+tree.Height*2)throw new InvalidOperationException("Creative base cut did not fell tree");
            await ToSignal(GetTree().CreateTimer(.7),SceneTreeTimer.SignalName.Timeout);await Snap("voxel-tree-falling");
            await ToSignal(GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);await Snap("voxel-tree-drops");
            if(_world.Total("wood")!=wood+tree.Height*2)throw new InvalidOperationException("Animation duplicated drops");
            GD.Print("VOXEL_TREE_UI_OK: textured trees, base selection/mining, falling animation and single drops");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
