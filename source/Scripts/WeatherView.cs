using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private ProceduralSkyMaterial _weatherSky=null!;
    private MultiMeshInstance3D _rain=null!;
    private DemoWorld? _rainWorld;
    private long _rainVersion=-1;
    private readonly Dictionary<(int,int),int> _rainFloor=new();
    private double _rainElapsed;
    private void BuildWeatherVisuals()
    {
        _weatherSky=new ProceduralSkyMaterial();
        _dayEnvironment.Sky=new Sky{SkyMaterial=_weatherSky};
        _dayEnvironment.BackgroundMode=Godot.Environment.BGMode.Sky;
        var material=new StandardMaterial3D{AlbedoColor=new Color(.65f,.8f,.9f,.55f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
        _rain=new MultiMeshInstance3D{CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,Multimesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=new BoxMesh{Size=new Vector3(.018f,.5f,.018f),Material=material},InstanceCount=256}};
        AddChild(_rain);_rain.Hide();
    }
    private bool WeatherHidden()
    {
        int x=Math.Clamp((int)Math.Round(_focus.X+(TerrainMap.Size-1)/2f),0,TerrainMap.Size-1),z=Math.Clamp((int)Math.Round(_focus.Z+(TerrainMap.Size-1)/2f),0,TerrainMap.Size-1);
        return _interiorView||_focus.Y<_world.Terrain[x,z].Level-.1f;
    }
    // Cache exposed column tops; rain never reaches below a roof or solid terrain.
    private int RainFloor(int x,int z)
    {
        if(_rainWorld!=_world||_rainVersion!=_world.Navigation.Version){_rainWorld=_world;_rainVersion=_world.Navigation.Version;_rainFloor.Clear();}
        if(_rainFloor.TryGetValue((x,z),out int top))return top;
        top=TerrainMap.MinLevel;
        for(int y=TerrainMap.MaxLevel;y>=TerrainMap.MinLevel;y--)if(_world.Solid(new Cell(x,z,y))){top=y+1;break;}
        if(_rainFloor.Count>=4096)_rainFloor.Clear();
        _rainFloor[(x,z)]=top;return top;
    }
    private void RefreshWeatherVisuals(double delta)
    {
        if(_rain is null)return;
        var weather=_world.Weather;bool hidden=WeatherHidden();
        _dayEnvironment.FogEnabled=!hidden&&weather.Fog>0;
        _dayEnvironment.FogMode=Godot.Environment.FogModeEnum.Depth;
        _dayEnvironment.FogDensity=(float)weather.Fog;
        _dayEnvironment.FogDepthBegin=35;_dayEnvironment.FogDepthEnd=105;
        _dayEnvironment.FogLightColor=new Color("243047").Lerp(new Color("8f9fa6"),(float)_world.Calendar.Sunlight);
        _rain.Visible=!hidden&&weather.Id=="rain";
        _rainElapsed+=delta;if(!_rain.Visible||_rainElapsed<.05)return;_rainElapsed=0;
        float radius=Math.Min(45,_camera.Size*.7f);
        for(int i=0;i<256;i++)
        {
            float x=_focus.X+(((i*73)%257)/256f-.5f)*radius*2,z=_focus.Z+(((i*137)%263)/262f-.5f)*radius*2;
            int cx=(int)Math.Round(x+(TerrainMap.Size-1)/2f),cz=(int)Math.Round(z+(TerrainMap.Size-1)/2f);
            if(cx<0||cz<0||cx>=TerrainMap.Size||cz>=TerrainMap.Size){_rain.Multimesh.SetInstanceTransform(i,new Transform3D(Basis.Identity.Scaled(Vector3.Zero),Vector3.Zero));continue;}
            float phase=(float)((_world.SimulationTime*1.6+i*.61803398875)%1);
            float y=RainFloor(cx,cz)+.3f+(1-phase)*8;
            _rain.Multimesh.SetInstanceTransform(i,new Transform3D(Basis.Identity,new Vector3(x,y,z)));
        }
    }
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
