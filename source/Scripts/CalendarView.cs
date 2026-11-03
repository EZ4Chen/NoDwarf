using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Godot.Environment _dayEnvironment=null!;
    private DirectionalLight3D _sun=null!;
    private Label _calendarLabel=null!;
    private (DemoWorld? World,long Minute,string Weather,bool Underground) _lastSky;
    private void RefreshCalendar()
    {
        var date=_world.Calendar;var weather=_world.Weather;
        _temperatureStatus.Text=$"室外 {_world.OutdoorTemperature:F0}°C";
        _temperatureStatus.TooltipText=$"室外温度 {_world.OutdoorTemperature:F1}°C";
        _weatherStatus.Text=weather.Name;
        _timeStatus.Text=$"{date.Hour}时";_timeStatus.TooltipText=date.TimeText;
        _calendarLabel.Text=$"第 {date.Year} 年 · 第 {date.DayIndex%GameCalendar.DaysPerYear+1} 天";
        _seasonStatus.Text=date.Season;
        string Hour(double h)=>$"{(int)h:00}:{(int)((h%1)*60):00}";
        string daylight=date.DaylightHours<.01?"极夜":date.DaylightHours>23.99?"极昼":$"{Hour(date.Sunrise)} 日出，{Hour(date.Sunset)} 日落";
        _calendarLabel.TooltipText=$"每季 30 天，一年 120 天。1× 约 10 分钟一天。\n纬度 {_world.Latitude:F1}° · {daylight} · 昼长 {date.DaylightHours:F1} 小时\n天气影响露天温度和作物光照。暂停停止日期与天气推进。";
        var skyKey=(_world,date.TotalMinutes,weather.Id,UndergroundView);
        if(_lastSky==skyKey)return;_lastSky=skyKey;
        float light=(float)date.Sunlight;
        var solar=_world.Sun;var direction=new Vector3((float)solar.East,(float)solar.Up,-(float)solar.North).Normalized();
        _sun.LookAt(_sun.GlobalPosition-direction,Math.Abs(direction.Y)>.999f?Vector3.Forward:Vector3.Up);
        _sun.LightColor=new Color("ffbd85").Lerp(new Color("fff0db"),Math.Clamp(light*2,0,1));
        _sun.LightEnergy=solar.Up<=0?0:.8f*light*(float)weather.Light;
        RefreshDepthLighting(light,(float)weather.Light);
        if(_weatherSky is not null)
        {
            var haze=new Color("879297");
            _weatherSky.SkyTopColor=new Color("111c36").Lerp(new Color("5c9cc6").Lerp(haze,(float)(1-weather.Light)),light);
            _weatherSky.SkyHorizonColor=new Color("293245").Lerp(new Color("b5ccdb").Lerp(haze,(float)(1-weather.Light)),light);
            _weatherSky.GroundBottomColor=new Color("20272c");_weatherSky.GroundHorizonColor=_weatherSky.SkyHorizonColor;
        }
    }
}
