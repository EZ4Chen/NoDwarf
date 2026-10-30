namespace NoDwarf.Simulation;

public readonly record struct CropClimate(double Temperature,double Light,double Rate,string Issue,double Fertility=1,double TemperatureFactor=1,double LightFactor=1,double FertilityFactor=1,bool Resting=false);
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
        return PlantGrowthRules.Default.Crop(CropCatalog.IsTree(crop)?"grain":crop).TemperatureFactor(temperature);
    }
}
public sealed partial class DemoWorld
{
    private readonly Dictionary<Cell,(long Version,bool Covered)> _farmExposure=new();
    private bool FarmCovered(Cell c)
    {
        if(_farmExposure.TryGetValue(c,out var cached)&&cached.Version==FarmGeometryVersion(c))return cached.Covered;
        bool covered=false;
        for(int y=c.Level+1;y<=TerrainMap.MaxLevel;y++)if(Solid(new Cell(c.X,c.Z,y))){covered=true;break;}
        _farmExposure[c]=(FarmGeometryVersion(c),covered);return covered;
    }
    public double OutdoorTemperature=>ClimateRules.OutdoorTemperature(Calendar,Terrain[Camp.X,Camp.Z].Level,Terrain.Region)+Weather.Temperature;
    public CropClimate FarmClimate(PlantSite p)
    {
        bool covered=FarmCovered(p.Cell);var date=Calendar;
        double temperature=TemperatureAt(p.Cell);
        double light=covered?0:date.Sunlight*Weather.Light;
        var rule=PlantGrowthRules.Default.Crop(CropCatalog.IsTree(p.Crop)?"grain":p.Crop);
        double thermal=rule.TemperatureFactor(temperature),lightFactor=rule.LightFactor(light);
        double fertility=p.Crop=="mushroom"&&p.Prepared?1:PlantGrowthRules.Default.Fertility(BlockAt(p.Cell+new Cell(0,0,-1)));
        double fertile=rule.FertilityFactor(fertility);bool resting=rule.Resting(date.Hour);
        string issue=FarmEnvironment(p);
        if(issue=="")issue=thermal==0?(temperature<=rule.MinTemperature?"温度过低":"温度过高"):fertile==0?"土壤肥力不足":lightFactor<.001?(!rule.Dark?"光照不足（夜间或遮挡）":"光照过强"):resting?"植物休眠":"";
        return new(temperature,light,issue==""?thermal*lightFactor*fertile:0,issue,fertility,thermal,lightFactor,fertile,resting);
    }
}
