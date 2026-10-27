using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureFluidPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            var origin=_world.Camp+new Cell(0,0,-5);
            for(int x=0;x<9;x++)for(int z=0;z<5;z++)for(int y=0;y<3;y++)
                if(x!=3)_world.Terrain.Excavate(origin+new Cell(x,z,y));
            var gate=origin+new Cell(3,2);_world.Terrain.Excavate(gate);_world.Buildings[gate]=new(gate,BuildKind.Floodgate);
            var source=origin+new Cell(-1,2);_world.Fluids.AddSource(new Aquifer{Cell=source,Capacity=80000,Stored=80000,Head=origin.Level+3,Rate=2500,Temperature=20});
            _foundation=new FoundationPlan(origin,origin+new Cell(8,4));_heightSelected=true;_layerOffset=0;_placement=0;
            _focus=WorldPosition(origin+new Cell(4,2));_camera.Size=17;_cameraYaw=0;_topDown=false;CreatePeople();PositionCamera();SetViewMode(1);
            _buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();_selectionPanel.Hide();
            void Advance(int ticks){for(int i=0;i<ticks;i++)_world.Fluids.Advance(.2);}
            async System.Threading.Tasks.Task Snap(string name)
            {
                SyncWorld();RefreshUi();RefreshFluidVisuals();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/fluid-{name}.png"))!=Error.Ok)throw new IOException("Fluid screenshot failed");
                if(_selectionPanel.Visible&&(!_selectionPanel.GetGlobalRect().Encloses(_selection.GetGlobalRect())||!_selectionPanel.GetGlobalRect().Encloses(_gateAction.GetGlobalRect())))throw new InvalidOperationException("Water inspector overflow");
                if(!_world.Fluids.Conserved||_waterMesh.Mesh is null)throw new InvalidOperationException("Fluid preview state invalid");
            }
            Advance(120);await Snap("closed");
            if(_world.Fluids.Cells.Keys.Any(c=>c.X>gate.X))throw new InvalidOperationException("Closed gate leaks");
            var screen=_camera.UnprojectPosition(WorldPosition(gate)+Vector3.Up*.5f);
            var picked=PickTarget(screen);if(picked!=gate)throw new InvalidOperationException($"Gate pick mismatch: {picked}");
            Act(gate);RefreshUi();if(!_gateAction.Visible)throw new InvalidOperationException("Gate selection action missing");
            _gateAction.EmitSignal(Godot.Button.SignalName.Pressed);if(!_world.GateOpen(gate))throw new InvalidOperationException("Gate UI does not open");
            Advance(100);await Snap("flooding");
            if(!_world.Fluids.Cells.Keys.Any(c=>c.X>gate.X))throw new InvalidOperationException("Open gate failed to flood room");
            _gateAction.EmitSignal(Godot.Button.SignalName.Pressed);Advance(30);await Snap("resealed");
            if(_world.GateOpen(gate))throw new InvalidOperationException("Gate UI does not close");
            long mass=_world.Fluids.Mass;_world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;_terrainRevision=-1;await Snap("reloaded");
            if(_world.Fluids.Mass!=mass)throw new InvalidOperationException("Reload lost water");
            _layerOffset=-4;_focus.Y=origin.Level-4;PositionCamera();SyncWorld();RefreshFluidVisuals();
            if(_waterMesh.Mesh is not null)throw new InvalidOperationException("Water leaks through lower floor cutaway");
            GD.Print("FLUID_UI_OK: underground water, closed/open/resealed gate, actual picking and button, depth inspector, save/load and floor clipping");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private void EditSelectedWater(bool drain)
    {
        if(!_developerMode||_selectedCell is not Cell c){RecordWorldEvent("先开启开发者模式并选中一个地格。");return;}
        if(_world.Solid(c))c+=new Cell(0,0,1);
        RecordWorldEvent(drain?$"开发排水 {_world.Fluids.Drain(c)} L。":$"开发注水 {_world.Fluids.Add(c,1000)} L。");RefreshUi();RefreshFluidVisuals();
    }
}
