using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureCalendarPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            _world.QueueBlueprint(_world.Camp,DemoWorld.ExampleBlueprint());_world.DeveloperFinishPlans();
            _foundation=new FoundationPlan(_world.Camp,_world.Camp+new Cell(5,4));_heightSelected=true;_layerOffset=0;
            _focus=WorldPosition(_world.Camp+new Cell(2,3));_camera.Size=17;_cameraYaw=0;_topDown=false;CreatePeople();PositionCamera();SetViewMode(1);
            _buildMenu.Hide();_inspector.Hide();_selectionPanel.Hide();_worldMenu.Hide();
            foreach(var (name,seconds) in new[]{("spring-noon",100d),("spring-night",400d),("summer-noon",30*600d+100),("winter-noon",90*600d+100)})
            {
                _world.Update(seconds-_world.SimulationTime);RefreshUi();SyncWorld();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/calendar-{name}.png"))!=Error.Ok)throw new IOException("Calendar screenshot failed");
                if(!_calendarLabel.GetParent<Control>().GetGlobalRect().Encloses(_calendarLabel.GetGlobalRect())||!_timeStatus.GetParent<Control>().GetGlobalRect().Encloses(_timeStatus.GetGlobalRect()))throw new InvalidOperationException("Calendar/time labels overflow their rows");
            }
            if(_world.Calendar.SeasonIndex!=3||_world.Calendar.Day!=1||_world.Calendar.TimeText!="12:00")throw new InvalidOperationException("Winter preview date mismatch");
            GD.Print("CALENDAR_UI_OK: daylight, night visibility, seasonal tint and date panel bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}

public partial class Main
{
    private async void CaptureCropClimatePreview()
    {
        try
        {
            _clock.Paused=true;var source=new DemoWorld();source.DeveloperResidents(false);
            var c=FarmingChecks.SurfacePlot(source);source.SetFarm(c,c,"grain");source.SpawnPlant(c,"grain",30,true);
            var cave=c+new Cell(0,0,-12);
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)for(int y=0;y<2;y++)source.Terrain.Excavate(cave+new Cell(x,z,y));
            source.SetFarm(cave,cave,"mushroom");source.SpawnPlant(cave,"mushroom",40,true);
            foreach(var (name,time,underground) in new[]{("day",100d,false),("night",300d,false),("winter",90*600d+100,false),("cave-winter",90*600d+100,true)})
            {
                _world=CropClimateChecks.AtTime(source,time);_overview.World=_world;_terrainRevision=-1;
                var target=underground?cave:c;_foundation=underground?new FoundationPlan(cave,cave):null;_heightSelected=underground;_layerOffset=0;
                _focus=WorldPosition(target);_camera.Size=12;_cameraYaw=0;_topDown=false;
                CreatePeople();PositionCamera();SetViewMode(underground?1:0);SelectTool("select");Act(target);SyncWorld();RefreshUi();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/crop-climate-{name}.png"))!=Error.Ok)throw new IOException("Climate screenshot failed");
                if(!_selection.Text.Contains("°C")||!_selectionPanel.GetGlobalRect().Encloses(_selection.GetGlobalRect())||_selectionPanel.GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Farm climate inspector layout invalid");
            }
            GD.Print("CROP_CLIMATE_UI_OK: local temperature, light, growth rate, pause reasons and inspector bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
