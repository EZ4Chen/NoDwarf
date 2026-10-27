namespace NoDwarf.Simulation;
public static class WeatherChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Run()
    {
        CalendarDate Date(int day,int hour=12,double lat=45)=>new(day*1440L+hour*60,lat,true);
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
        var selected=new DemoWorld(atlas.LocalSeed,region:atlas.Site.Region,generation:atlas);Check(selected.ClimateVersion==1&&selected.Calendar.Astronomical,"Atlas start enables weather and astronomy");
        var old=System.Text.Json.Nodes.JsonNode.Parse(selected.SaveJson())!;old["Version"]=10;old.AsObject().Remove("ClimateVersion");old.AsObject().Remove("WeatherOverride");
        Check(DemoWorld.LoadJson(old.ToJsonString()).ClimateVersion==1,"Old regional saves adopt climate without regenerating terrain");
        var world=new DemoWorld();world.DeveloperResidents(false);world.SetWeather("clear");world.Update(100);
        var c=FarmingChecks.SurfacePlot(world);world.SetFarm(c,c,"grain");var farm=world.Farms[c];
        var clear=world.FarmClimate(farm);world.SetWeather("rain");var rain=world.FarmClimate(farm);
        Check(rain.Light<clear.Light&&rain.Temperature<clear.Temperature,"Rain changes crop climate");
        var loaded=DemoWorld.LoadJson(world.SaveJson());Check(loaded.Weather==world.Weather&&loaded.Calendar==world.Calendar,"Weather override and solar clock saved");
        world.SetWeather("auto");loaded=DemoWorld.LoadJson(world.SaveJson());Check(loaded.WeatherOverride is null&&loaded.Weather==world.Weather,"Automatic weather survives reload");
        string bad=world.SaveJson().Replace("\"WeatherOverride\": null","\"WeatherOverride\": \"invalid\"");
        Check(bad!=world.SaveJson(),"Invalid-weather fixture");
        try{DemoWorld.LoadJson(bad);throw new Exception("Invalid weather accepted");}catch(InvalidDataException){}
        Console.WriteLine("WEATHER_OK: hemispheres, polar limits, solar direction, deterministic climate, crop effects, save/load validation");
    }
}
