namespace NoDwarf.Simulation;

public readonly record struct CropClimate(double Temperature,double Light,double Rate,string Issue);
public static class ClimateRules
{
    public static double OutdoorTemperature(CalendarDate date,int elevation,RegionProfile? region=null)
    {
        double day=date.LocalDayIndex%GameCalendar.DaysPerYear+(date.Hour+date.Minute/60d)/24;
        double seasonal=(region?.Mean??12)+(region?.Seasonal??18)*Math.Sin(2*Math.PI*day/GameCalendar.DaysPerYear);
        double daily=(region?.Daily??4)*Math.Sin(2*Math.PI*(date.Hour+date.Minute/60d-8)/24);
        return seasonal+daily-(elevation-(region?.CampHeight??TerrainMap.SeaLevel))*.06;
    }
    public static double TemperatureFactor(string crop,double temperature)
    {
        var (min,low,high,max)=crop switch {"potato"=>(2d,10d,22d,30d),"cotton"=>(10d,18d,30d,38d),"mushroom"=>(4d,12d,20d,28d),_=>(4d,12d,24d,32d)};
        if(temperature<=min||temperature>=max)return 0;
        if(temperature<low)return (temperature-min)/(low-min);
        if(temperature>high)return (max-temperature)/(max-high);
        return 1;
    }
}
public sealed partial class DemoWorld
{
    private readonly Dictionary<Cell,(long Version,bool Covered)> _farmExposure=new();
    private bool FarmCovered(Cell c)
    {
        if(_farmExposure.TryGetValue(c,out var cached)&&cached.Version==Navigation.Version)return cached.Covered;
        bool covered=false;
        for(int y=c.Level+1;y<=TerrainMap.MaxLevel;y++)if(Solid(new Cell(c.X,c.Z,y))){covered=true;break;}
        _farmExposure[c]=(Navigation.Version,covered);return covered;
    }
    public double OutdoorTemperature=>ClimateRules.OutdoorTemperature(Calendar,Terrain[Camp.X,Camp.Z].Level,Terrain.Region)+Weather.Temperature;
    public CropClimate FarmClimate(FarmPlot p)
    {
        bool covered=FarmCovered(p.Cell);var date=Calendar;
        double outside=ClimateRules.OutdoorTemperature(date,Terrain[p.Cell.X,p.Cell.Z].Level,Terrain.Region)+Weather.Temperature;
        int depth=Math.Max(0,Terrain[p.Cell.X,p.Cell.Z].Level-p.Cell.Level);
        // A simple depth-based cave buffer, not a per-voxel heat simulation.
        double mean=Terrain.Region?.Mean??15;
        double temperature=covered&&depth>0?mean+(outside-mean)*Math.Exp(-depth/6d):outside;
        temperature+=HeatingAt(p.Cell);
        double light=covered?0:date.Sunlight*Weather.Light;
        double thermal=ClimateRules.TemperatureFactor(p.Crop,temperature);
        double lightFactor=p.Crop!="mushroom"?light:Math.Clamp(1-light/.2,0,1);
        string issue=FarmEnvironment(p);
        if(issue=="")issue=thermal==0?(temperature<=4?"温度过低":"温度过高"):lightFactor<.001?(p.Crop!="mushroom"?"光照不足（夜间或遮挡）":"光照过强"):"";
        return new(temperature,light,issue==""?thermal*lightFactor:0,issue);
    }
}
