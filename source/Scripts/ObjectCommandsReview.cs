using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewObjectCommands()
    {
        try
        {
            void Check(bool value,string why){if(!value)throw new Exception("OBJECT_COMMANDS: "+why);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Click(Button button)
            {
                await Frame();Check(button.IsVisibleInTree(),"button hidden: "+button.Text);
                var p=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);
                foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=down},true);
                await Frame();
            }
            async Task Snap(string name)
            {
                await Frame();RefreshUi();await Frame();
                Check(!_objectCommands.Visible||GetViewport().GetVisibleRect().Encloses(_objectCommands.GetGlobalRect()),"commands outside viewport");
                if(DisplayServer.GetName()=="headless")return;
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/commands-"+name+".png"));
            }
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;_heightSelected=false;_cutaway=false;_layerOffset=0;
            _interiorView=false;_floorWalls=false;_camera.Size=16;_cameraYaw=0;_orbitPitch=DefaultCameraPitch;
            var rock=_world.Camp+new Cell(3,3,-1);var wall=rock+new Cell(3,0,1);var plan=wall+new Cell(0,3);var tree=wall+new Cell(-5,4);
            Check(_world.DeveloperPlace(wall,BuildKind.Wall),"wall fixture");
            _world.Trees[tree]=new(){Cell=tree,Age=600,MaximumHeight=6};_world.Terrain.SetTree(tree,true);
            _focus=WorldPosition(rock+new Cell(1,1,1));PositionCamera();CreatePeople();SelectTool("select");SyncWorld();_terrainView.FlushMeshes();await Frame();
            Act(rock);Check(_mineSelectionAction.Visible&&!_demolishAction.Visible&&!_locateSelectionAction.Visible,"rock actions");
            await Snap("rock");await Click(_mineSelectionAction);
            Check(_tool=="select"&&_world.Jobs.Count(j=>j.Dig&&!j.Demolish&&j.Cell==rock)==1&&!_mineSelectionAction.Visible&&_cancelDigRegionAction.Visible,"direct mining did not replace action with cancellation");
            await Click(_cancelDigRegionAction);Check(!_world.Jobs.Any(j=>j.Cell==rock)&&_world.NaturalSolid(rock),"mining cancellation changed terrain");
            Act(wall);Check(_demolishAction.Visible&&!_mineSelectionAction.Visible,"building actions");await Snap("wall");await Click(_demolishAction);
            OrderSelectedObject("erase");Check(_world.Jobs.Count(j=>j.Demolish&&j.Cell==wall)==1&&_world.Buildings.ContainsKey(wall),"demolition should queue once");
            Check(_cancelConstructionAction.Text=="取消拆除"&&!_demolishAction.Visible,"queued demolition actions");
            await Click(_cancelConstructionAction);Check(_world.Buildings.ContainsKey(wall)&&!_world.Jobs.Any(j=>j.Cell==wall),"cancel demolition");
            Check(_world.QueueBuild(plan,BuildKind.Wall),"blueprint fixture");SyncWorld();Act(plan);
            Check(_cancelConstructionAction.Visible&&!_demolishAction.Visible&&!_mineSelectionAction.Visible,"blueprint actions");await Snap("blueprint");
            await Click(_cancelConstructionAction);Check(!_world.Jobs.Any(j=>j.Cell==plan),"blueprint cancellation");
            Act(tree);Check(_chopSelectionAction.Visible&&!_mineSelectionAction.Visible&&!_demolishAction.Visible,"tree actions");await Click(_chopSelectionAction);
            Check(_cancelChopAction.Visible&&!_chopSelectionAction.Visible,"queued tree actions");await Click(_cancelChopAction);
            Check(_world.Trees.ContainsKey(tree)&&!_world.Jobs.Any(j=>j.Chop&&j.Cell==tree),"tree cancellation");
            Act(plan+new Cell(4,0));Check(!_objectCommands.Visible,"empty cell shows commands");
            var bed=wall+new Cell(5,0);Check(_world.DeveloperPlace(bed,BuildKind.Bed),"bed fixture");SyncWorld();Act(bed);
            Check(_medicalBedAction.Visible&&_demolishAction.Visible&&!_mineSelectionAction.Visible,"bed actions");
            GetWindow().ContentScaleSize=new(960,640);await Frame();await Frame();await Snap("bed-narrow");
            Check(!_selectionPanel.GetGlobalRect().Intersects(_objectCommands.GetGlobalRect()),"narrow layout overlaps information");
            Check(_objectCommands.GetGlobalRect().Encloses(_medicalBedAction.GetGlobalRect()),"medical action overflow");
            await Click(_medicalBedAction);Check(_world.IsMedicalBed(bed),"medical bed button lost callback");
            GetWindow().ContentScaleSize=new(1280,800);await Frame();await Frame();
            Act(rock);SelectTool("dig");Check(!_objectCommands.Visible,"orphan object command bar");
            SelectTool("select");ShowBuildPage("命令");_buildMenu.Show();await Snap("tools");
            Check(UiIcons.Get("erase")!=UiIcons.Get("cancel"),"demolish and cancel share icon");
            GD.Print("OBJECT_COMMANDS_OK: real clicks on rock/building/blueprint/tree, direct queue, cancellation, duplicate guards, selection state and contextual visibility");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
