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
                _terrainView.FlushMeshes();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+name+".png"))!=Error.Ok)throw new IOException("Tree screenshot");
            }
            await Snap("voxel-trees-standing");
            int builds=_terrainView.TreeBuildCount;
            for(int i=1;i<=30;i++)
            {
                _camera.Size=Mathf.Lerp(20,95,i/30f);PositionCamera();SyncWorld();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            await Snap("voxel-trees-distant");
            for(int i=1;i<=30;i++)
            {
                _camera.Size=Mathf.Lerp(95,20,i/30f);PositionCamera();SyncWorld();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            if(_terrainView.TreeBuildCount!=builds)throw new InvalidOperationException("Zoom rebuilt forest meshes");
            await Snap("voxel-trees-returned");
            _foundation=new FoundationPlan(tree.Cell,tree.Cell);SetViewMode(2);SyncWorld();
            await Snap("voxel-trees-cut");
            _foundation=null;_heightSelected=false;SetViewMode(0);SyncWorld();
            if(_terrainView.TreeBuildCount!=builds)throw new InvalidOperationException("Height clipping rebuilt forest meshes");
            var screen=_camera.UnprojectPosition(WorldPosition(tree.Cell)+Vector3.Up*.4f);
            if(PickTarget(screen)!=tree.Cell||DigPick(screen)!=tree.Cell)throw new InvalidOperationException("Tree base picking");
            int wood=_world.Total("wood");SelectTool("dig");Act(tree.Cell);SyncWorld();
            if(_world.Trees.ContainsKey(tree.Cell)||_world.Total("wood")!=wood+tree.Height*2)throw new InvalidOperationException("Creative base cut did not fell tree");
            await ToSignal(GetTree().CreateTimer(.7),SceneTreeTimer.SignalName.Timeout);await Snap("voxel-tree-falling");
            await ToSignal(GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);await Snap("voxel-tree-drops");
            if(_world.Total("wood")!=wood+tree.Height*2)throw new InvalidOperationException("Animation duplicated drops");
            GD.Print("VOXEL_TREE_UI_OK: textured trees, zoom/height changes without rebuilding, base selection/mining, falling animation and single drops");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
