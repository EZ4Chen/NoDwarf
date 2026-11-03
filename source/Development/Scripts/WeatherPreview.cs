using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureWeatherPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld(region:"temperate");_world.DeveloperResidents(false);_world.Update(100);
            _overview.World=_world;_terrainRevision=-1;_focus=WorldPosition(_world.Camp);_camera.Size=25;CreatePeople();PositionCamera();
            _buildMenu.Hide();_inspector.Hide();_selectionPanel.Hide();_worldMenu.Hide();
            foreach(string kind in new[]{"clear","rain","fog","night"})
            {
                _world.SetWeather(kind=="night"?"clear":kind);if(kind=="night")_world.Update(300);
                SyncWorld();RefreshUi();RefreshWeatherVisuals(.1);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/weather-{kind}.png"))!=Error.Ok)throw new IOException("Weather screenshot failed");
                if(!_calendarLabel.GetParent<Control>().GetGlobalRect().Encloses(_calendarLabel.GetGlobalRect()))throw new InvalidOperationException("Weather label overflow");
                if(kind=="rain"&&!_rain.Visible||kind=="fog"&&!_dayEnvironment.FogEnabled||kind=="night"&&_sun.LightEnergy!=0)throw new InvalidOperationException("Weather presentation mismatch");
            }
            var roof=_world.Camp+new Cell(0,0,3);int bare=RainFloor(roof.X,roof.Z);
            _world.Buildings[roof]=new(roof,BuildKind.Planks);
            if(RainFloor(roof.X,roof.Z)!=roof.Level+1)throw new InvalidOperationException("Roof fails to stop rain");
            _world.Buildings.Remove(roof);if(RainFloor(roof.X,roof.Z)!=bare)throw new InvalidOperationException("Roof removal leaves stale rain cache");
            _world.SetWeather("rain");_interiorView=true;RefreshWeatherVisuals(.1);
            if(_rain.Visible||_dayEnvironment.FogEnabled)throw new InvalidOperationException("Interior weather leakage");
            GD.Print("WEATHER_UI_OK: sky, rain, fog, night, interior suppression and label bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
