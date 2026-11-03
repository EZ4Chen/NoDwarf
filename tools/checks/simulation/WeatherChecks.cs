namespace NoDwarf.Simulation;
public static class WeatherChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static FarmPlot CreateWeatherPlot(DemoWorld world)
    {
        // Weather only needs one real outdoor plot, not the 5×4 work area used by farming tests.
        // Let the game validate height, soil, access and exposure, including regional soil types.
        for(int x=-6;x<=6;x++)for(int z=-6;z<=6;z++)
        {
            var c=world.Surface(world.Camp.X+x,world.Camp.Z+z);
            if(world.SetFarm(c,c,"grain"))return world.Farms[c];
        }
        throw new InvalidOperationException($"Weather test setup: no plantable outdoor cell within 6 tiles of camp; seed={world.Terrain.Seed}, region={world.Terrain.Region?.Id??"basic"}, camp={world.Camp}");
    }
    private static void CheckRainOnPlot(DemoWorld world,FarmPlot plot)
    {
        world.SetWeather("clear");var clear=world.FarmClimate(plot);
        world.SetWeather("rain");var rain=world.FarmClimate(plot);
        Check(clear.Light>0&&clear.Issue==""&&clear.Rate>0,"Weather fixture must support actual crop growth in clear daylight");
        Check(rain.Light<clear.Light&&rain.Temperature<clear.Temperature,"Rain changes crop climate");
        Check(Math.Abs(rain.Light-clear.Light*WeatherState.Get("rain").Light)<.000001&&Math.Abs(rain.Temperature-clear.Temperature-WeatherState.Get("rain").Temperature)<.000001,"Weather effects match configured sunlight and temperature modifiers");
    }
    public static void Run()
    {
        CalendarDate Date(int day,int hour=12,double lat=45)=>new(day*1440L+hour*60,lat);
        Check(Math.Abs(Date(0,0).DaylightHours-12)<.001,"Equinox twelve hour day");
        Check(Date(30).DaylightHours>Date(90).DaylightHours&&Date(30,12,-45).DaylightHours<Date(90,12,-45).DaylightHours,"Hemisphere day lengths reversed");
        Check(Date(30,12,-45).SeasonIndex==3&&Date(90,12,-45).SeasonIndex==1,"Southern local seasons");
        Check(Date(30,12,80).DaylightHours==24&&Date(90,12,80).DaylightHours==0,"Polar day and night");
        for(int day=0;day<120;day++)Check(Math.Abs(Date(day,12,0).DaylightHours-12)<.001,"Equatorial days");
        Check(SolarRules.At(Date(0,8),45).East>0&&SolarRules.At(Date(0,16),45).East<0,"Sun rises east and sets west");
        int wet=0,dry=0;var seen=new HashSet<string>();
        for(int hour=0;hour<120*24;hour+=4)
        {
            var d=new CalendarDate(hour*60);
            var weather=WeatherRules.At(74219,d,.85,20);seen.Add(weather.Id);
            Check(weather==WeatherRules.At(74219,d,.85,20),"Seeded weather deterministic");
            if(weather.Id=="rain")wet++;if(WeatherRules.At(74219,d,.1,20).Id=="rain")dry++;
            Check(WeatherRules.At(74219,d,.85,-10).Id!="rain","Freezing auto weather does not rain");
        }
        Check(wet>dry*3&&seen.Count==4,"Humidity influences rain and all weather occurs");
        var atlas=new WorldGenerationSettings().WithRecommendedSite();
        var selected=new DemoWorld(atlas.LocalSeed,region:atlas.Site.Region,generation:atlas);Check(selected.ClimateVersion==1&&selected.Calendar.Latitude==selected.Latitude,"Atlas start enables weather and astronomy");
        var world=new DemoWorld();world.DeveloperResidents(false);world.SetWeather("clear");world.Update(100);
        var farm=CreateWeatherPlot(world);CheckRainOnPlot(world,farm);
        var loaded=DemoWorld.LoadJson(world.SaveJson());Check(loaded.Weather==world.Weather&&loaded.Calendar==world.Calendar,"Weather override and solar clock saved");
        world.SetWeather("auto");loaded=DemoWorld.LoadJson(world.SaveJson());Check(loaded.WeatherOverride is null&&loaded.Weather==world.Weather,"Automatic weather survives reload");
        string bad=world.SaveJson().Replace("\"WeatherOverride\": null","\"WeatherOverride\": \"invalid\"");
        Check(bad!=world.SaveJson(),"Invalid-weather fixture");
        try{DemoWorld.LoadJson(bad);throw new Exception("Invalid weather accepted");}catch(InvalidDataException){}
        foreach(var (region,soil) in new[]{("boreal","nodwarf:humus"),("savanna","nodwarf:dry_grass")})
        {
            var regional=new DemoWorld(74219,region:region);regional.DeveloperResidents(false);regional.Update(100);
            var plot=CreateWeatherPlot(regional);
            Check(regional.BlockAt(plot.Cell+new Cell(0,0,-1))==soil,"Regional weather fixture must cover its supported soil: "+soil);
            regional.SetWeather("clear");var clear=regional.FarmClimate(plot);regional.SetWeather("rain");var rain=regional.FarmClimate(plot);
            Check(clear.Light>0&&rain.Light<clear.Light&&rain.Temperature<clear.Temperature,"Weather affects valid regional plots even outside crop temperature range");
        }
        var narrow=new DemoWorld();narrow.DeveloperResidents(false);narrow.Update(100);
        var only=CreateWeatherPlot(narrow).Cell;narrow.SetFarm(only,only,"grain",remove:true);
        for(int x=-9;x<=9;x++)for(int z=-9;z<=9;z++)
        {
            var c=narrow.Surface(narrow.Camp.X+x,narrow.Camp.Z+z);
            if(c==only)continue;
            var floor=c+new Cell(0,0,-1);narrow.Buildings[floor]=new(floor,BuildKind.Planks);
        }
        var single=CreateWeatherPlot(narrow);Check(single.Cell==only&&narrow.Farms.Count==1,"One valid tile suffices; neighboring paved terrain must not invalidate the weather test");CheckRainOnPlot(narrow,single);
        narrow.SetFarm(only,only,"grain",remove:true);var lastFloor=only+new Cell(0,0,-1);narrow.Buildings[lastFloor]=new(lastFloor,BuildKind.Planks);
        bool noSoil=false;try{CreateWeatherPlot(narrow);}catch(InvalidOperationException e){noSoil=e.Message.Contains("seed=74219");}
        Check(noSoil,"An entirely unplantable fixture fails explicitly with map diagnostics, never skips weather assertions");
        Console.WriteLine("WEATHER_FARM_FIXTURE_OK: real planting validation, humus, dry grass, single-tile farmland and explicit no-soil failure");
        Console.WriteLine("WEATHER_OK: hemispheres, polar limits, solar direction, deterministic climate, crop effects, save/load validation");
    }
}
