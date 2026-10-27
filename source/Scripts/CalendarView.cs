using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Godot.Environment _dayEnvironment=null!;
    private DirectionalLight3D _sun=null!;
    private Label _calendarLabel=null!;
    private (DemoWorld? World,long Minute,string Weather) _lastSky;
    private void RefreshCalendar()
    {
        var date=_world.Calendar;var weather=_world.Weather;
        _timeStatus.Text=date.TimeText;
        _calendarLabel.Text=date.DateText+$" · {weather.Name} · {_world.OutdoorTemperature:F1}°C";
        string Hour(double h)=>$"{(int)h:00}:{(int)((h%1)*60):00}";
        string daylight=date.DaylightHours<.01?"极夜":date.DaylightHours>23.99?"极昼":$"{Hour(date.Sunrise)} 日出，{Hour(date.Sunset)} 日落";
        _calendarLabel.TooltipText=$"每季 30 天，一年 120 天。1× 约 10 分钟一天。\n纬度 {_world.Latitude:F1}° · {daylight} · 昼长 {date.DaylightHours:F1} 小时\n天气影响露天温度和作物光照。暂停停止日期与天气推进。";
        var skyKey=(_world,date.TotalMinutes,weather.Id);
        if(_lastSky==skyKey)return;_lastSky=skyKey;
        float light=(float)date.Sunlight;
        var solar=_world.Sun;var direction=new Vector3((float)solar.East,(float)solar.Up,-(float)solar.North).Normalized();
        _sun.LookAt(_sun.GlobalPosition-direction,Math.Abs(direction.Y)>.999f?Vector3.Forward:Vector3.Up);
        _sun.LightColor=new Color("ffbd85").Lerp(new Color("fff0db"),Math.Clamp(light*2,0,1));
        _sun.LightEnergy=solar.Up<=0?0:.8f*light*(float)weather.Light;
        _dayEnvironment.AmbientLightColor=new Color("9eafd0").Lerp(new Color("e1edda"),light);
        _dayEnvironment.AmbientLightEnergy=.32f+.12f*light*(float)weather.Light;
        if(_weatherSky is not null)
        {
            var haze=new Color("879297");
            _weatherSky.SkyTopColor=new Color("111c36").Lerp(new Color("5c9cc6").Lerp(haze,(float)(1-weather.Light)),light);
            _weatherSky.SkyHorizonColor=new Color("293245").Lerp(new Color("b5ccdb").Lerp(haze,(float)(1-weather.Light)),light);
            _weatherSky.GroundBottomColor=new Color("20272c");_weatherSky.GroundHorizonColor=_weatherSky.SkyHorizonColor;
        }
    }
}

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
            var c=FarmingChecks.SurfacePlot(source);source.SetFarm(c,c,"grain");source.Farms[c].Prepared=true;source.Farms[c].Planted=true;source.Farms[c].Growth=30;
            var cave=c+new Cell(0,0,-12);
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)for(int y=0;y<2;y++)source.Terrain.Excavate(cave+new Cell(x,z,y));
            source.SetFarm(cave,cave,"mushroom");source.Farms[cave].Prepared=true;source.Farms[cave].Planted=true;source.Farms[cave].Growth=40;
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
