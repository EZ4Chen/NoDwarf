using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private async void CaptureConstructionPreview()
    {
        try
        {
            ConstructionChecks.Run();
            CheckIncrementalVisuals();
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_world.Items.Clear();
            _world.SpawnItem("cobblestone",100,_world.Camp+new Cell(5,6));
            _world.SpawnItem("wood",100,_world.Camp+new Cell(5,6));
            _world.SpawnItem("iron",100,_world.Camp+new Cell(5,6));
            _overview.World=_world;_terrainRevision=-1;_clock.Paused=true;
            _developerMode=false;_creative=false;_foundation=null;_layerOffset=0;
            _heightSelected=false;_cutaway=false;_interiorView=false;_floorWalls=false;
            _topDown=false;_orbitPitch=DefaultCameraPitch;_cameraYaw=0;
            CreatePeople();SelectTool("select");_buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();
            var cell=_world.Camp+new Cell(3,3);
            var door=cell+new Cell(3,0);var anvil=cell+new Cell(0,3);
            foreach(var (c,kind) in new[]{(cell,BuildKind.Wall),(door,BuildKind.Door),(anvil,BuildKind.Anvil),(cell+new Cell(3,3),BuildKind.PlankStairs)})
                if(!_world.QueueBuild(c,kind))throw new InvalidOperationException("Preview plan rejected");
            var job=_world.Jobs.First(j=>j.Cell==cell);
            _focus=WorldPosition(cell+new Cell(1,1));_camera.Size=13;PositionCamera();SyncWorld();RefreshUi();
            async Task Frame()
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            void Click(Vector2 p)
            {
                GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p},true);
                foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=down},true);
            }
            async Task Snap(string name)
            {
                await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Screenshot failed");
            }
            await Frame();
            foreach(var (c,height) in new[]{(cell,.7f),(door,1.75f),(anvil,.7f)})
                if(PickTarget(_camera.UnprojectPosition(WorldPosition(c)+Vector3.Up*height))!=c)throw new InvalidOperationException("Blueprint geometry is not selectable: "+c);
            Click(_camera.UnprojectPosition(WorldPosition(cell)+Vector3.Up*.7f));await Frame();
            if(_selectedCell!=cell||!_cancelConstructionAction.IsVisibleInTree()||!_constructionProgress.IsVisibleInTree())throw new InvalidOperationException("Blueprint selection lacks cancellation or progress");
            if(_demolishAction.Visible)throw new InvalidOperationException("Blueprint must offer cancellation directly");
            ulong node=_planVisuals[job.Id].Root.GetInstanceId();
            var material=_constructionVisuals[job.Id].Materials[0].Material;float alpha=material.AlbedoColor.A;
            await Snap("construction-0");
            foreach(double progress in new[]{.5,.9})
            {
                foreach(var plan in _world.Jobs)plan.Progress=progress*DemoJob.BuildWorkRequired;
                UpdateConstructionVisuals();RefreshUi();await Snap($"construction-{progress*100:0}");
                if(material.AlbedoColor.A<=alpha||_planVisuals[job.Id].Root.GetInstanceId()!=node)throw new InvalidOperationException("Progress must change existing material without rebuilding the plan");
                if(Math.Abs(_constructionProgress.Value-progress)>.001||!_selection.Text.Contains("耐久"))throw new InvalidOperationException("Selection must show actual progress and durability");
                if(!_selectionPanel.GetGlobalRect().Encloses(_cancelConstructionAction.GetGlobalRect()))throw new InvalidOperationException("Cancel action overflows selection panel");
                alpha=material.AlbedoColor.A;
            }
            int stock=_world.Total("cobblestone");
            Click(_cancelConstructionAction.GetGlobalRect().GetCenter());await Frame();
            if(_world.Jobs.Contains(job)||_planVisuals.ContainsKey(job.Id)||_constructionVisuals.ContainsKey(job.Id)||_world.Total("cobblestone")!=stock||_selectionPanel.Visible)throw new InvalidOperationException("Cancel button did not remove the selected plan safely");
            if(PickObject(_camera.UnprojectPosition(WorldPosition(cell)+Vector3.Up*.7f),8).Count!=0)throw new InvalidOperationException("Cancelled plan retained collision");
            GD.Print("CONSTRUCTION_UI_OK: wall/door/anvil blueprint picking, progress opacity, retained geometry, panel, cancel button and collision cleanup");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
