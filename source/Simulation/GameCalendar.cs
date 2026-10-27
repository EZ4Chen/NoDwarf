namespace NoDwarf.Simulation;

public readonly record struct CalendarDate(long TotalMinutes,double Latitude=42,bool Astronomical=false)
{
    private static readonly string[] SeasonNames={"春季","夏季","秋季","冬季"};
    public long DayIndex=>TotalMinutes/1440;
    public long Year=>DayIndex/GameCalendar.DaysPerYear+1;
    public long LocalDayIndex=>DayIndex+(Astronomical&&Latitude<0?60:0);
    public int SeasonIndex=>(int)(LocalDayIndex%GameCalendar.DaysPerYear/GameCalendar.DaysPerSeason);
    public string Season=>SeasonNames[SeasonIndex];
    public int Day=>(int)(DayIndex%GameCalendar.DaysPerSeason)+1;
    public int Hour=>(int)(TotalMinutes%1440/60);
    public int Minute=>(int)(TotalMinutes%60);
    public double DaylightHours=>Astronomical?SolarRules.At(this,Latitude).Daylight:SeasonIndex switch{1=>14,3=>10,_=>12};
    public double Sunrise=>(24-DaylightHours)/2;
    public double Sunset=>24-Sunrise;
    public double Sunlight=>Astronomical?Math.Max(0,SolarRules.At(this,Latitude).Up):Math.Max(0,Math.Sin(Math.Clamp((Hour+Minute/60d-Sunrise)/DaylightHours,0,1)*Math.PI));
    public bool IsDay=>Sunlight>0;
    public string DateText=>$"第 {Year} 年 · {Season} {Day} 日";
    public string TimeText=>$"{Hour:D2}:{Minute:D2}";
}
public static class GameCalendar
{
    public const int DaysPerSeason=30,DaysPerYear=120;
    public const double SecondsPerDay=600;
    public const int StartMinute=8*60;
    public static CalendarDate At(double simulationSeconds)
    {
        if(!double.IsFinite(simulationSeconds)||simulationSeconds<0||simulationSeconds>(long.MaxValue-1440d)/1440*SecondsPerDay)
            throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
        double minutes=simulationSeconds/SecondsPerDay*1440+StartMinute;
        if(minutes>=long.MaxValue)throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
        return new((long)Math.Floor(minutes+1e-7));
    }
}
public sealed partial class DemoWorld
{
    public CalendarDate Calendar=>ClimateVersion==0?GameCalendar.At(SimulationTime):GameCalendar.At(SimulationTime) with {Latitude=Latitude,Astronomical=true};
}
