namespace NoDwarf.Simulation;

public readonly record struct SolarPosition(double East,double Up,double North,double Daylight);
public static class SolarRules
{
    public static SolarPosition At(CalendarDate date,double latitude)
    {
        double lat=Math.Clamp(latitude,-89.9,89.9)*Math.PI/180;
        double day=date.DayIndex%GameCalendar.DaysPerYear+(date.Hour+date.Minute/60d)/24;
        double decl=23.44*Math.PI/180*Math.Sin(2*Math.PI*day/GameCalendar.DaysPerYear);
        double threshold=-Math.Tan(lat)*Math.Tan(decl);
        double hours=threshold>=1?0:threshold<=-1?24:24*Math.Acos(threshold)/Math.PI;
        double angle=(date.Hour+date.Minute/60d-12)*Math.PI/12;
        return new(-Math.Cos(decl)*Math.Sin(angle),Math.Sin(lat)*Math.Sin(decl)+Math.Cos(lat)*Math.Cos(decl)*Math.Cos(angle),
            Math.Cos(lat)*Math.Sin(decl)-Math.Sin(lat)*Math.Cos(decl)*Math.Cos(angle),hours);
    }
}
public sealed record WeatherState(string Id,string Name,double Light,double Temperature,double Fog)
{
    public static readonly WeatherState[] All={new("clear","晴",1,0,0),new("cloudy","阴",.78,-1,.08),new("rain","雨",.55,-2,.22),new("fog","雾",.65,-1,.65)};
    public static WeatherState Get(string id)=>All.FirstOrDefault(w=>w.Id==id)??throw new ArgumentException("天气应为 auto / clear / cloudy / rain / fog。");
}
public static class WeatherRules
{
    // One deterministic decision per four game hours; no per-person or per-block weather simulation.
    public static WeatherState At(int seed,CalendarDate date,double humidity,double temperature)
    {
        uint h=unchecked((uint)seed^(uint)(date.TotalMinutes/240)*747796405u);h=unchecked((h^(h>>16))*2246822519u);h^=h>>13;
        double roll=h/(double)uint.MaxValue,wet=Math.Clamp(humidity,0,1);
        string kind=roll<wet*.38?(temperature>0?"rain":"cloudy"):roll<wet*.38+.04+wet*.1?"fog":roll<.35+wet*.4?"cloudy":"clear";
        return WeatherState.Get(kind);
    }
}
public sealed partial class DemoWorld
{
    public int ClimateVersion {get;private set;}
    public string? WeatherOverride {get;private set;}
    public double Latitude=>Terrain.Generation is {} g?(1-2*(g.SiteY+.5)/WorldGenerationSettings.AtlasHeight)*75:
        Terrain.Region?.Id switch {"rainforest"=>5,"savanna"=>15,"desert"=>25,"arid"=>30,"boreal"=>58,"tundra"=>70,"alpine"=>60,_=>42};
    public SolarPosition Sun=>SolarRules.At(Calendar,Latitude);
    private long _weatherMinute=-1;
    private WeatherState _cachedWeather=WeatherState.Get("clear");
    public WeatherState Weather
    {
        get
        {
            if(WeatherOverride is not null)return WeatherState.Get(WeatherOverride);
            if(ClimateVersion==0)return WeatherState.Get("clear");
            var date=Calendar;
            if(_weatherMinute!=date.TotalMinutes)
            {
                _weatherMinute=date.TotalMinutes;
                _cachedWeather=WeatherRules.At(Terrain.Seed,date,Terrain.Region?.Humidity??.55,
                    ClimateRules.OutdoorTemperature(date,Terrain[Camp.X,Camp.Z].Level,Terrain.Region));
            }
            return _cachedWeather;
        }
    }
    public void SetWeather(string id)
    {
        if(id!="auto")WeatherState.Get(id);
        _weatherMinute=-1;ClimateVersion=1;WeatherOverride=id=="auto"?null:id;Revision++;
        Log("天气："+(WeatherOverride is null?"自动变化":Weather.Name+"（开发者固定）"));
    }
}
