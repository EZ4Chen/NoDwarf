using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class CropClimateChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld w,int seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
    public static DemoWorld AtTime(DemoWorld w,double time)
    {
        var json=JsonNode.Parse(w.SaveJson())!;json["SimulationTime"]=time;json["FarmElapsed"]=0;
        return DemoWorld.LoadJson(json.ToJsonString());
    }
    public static void Run()
    {
        var w=new DemoWorld();w.DeveloperResidents(false);w.Items.Clear();w.Stockpiles.Clear();
        var c=FarmingChecks.SurfacePlot(w);Check(w.SetFarm(c,c,"grain"),"Climate test field");
        w.Farms[c].Planted=true;w.Farms[c].Prepared=true;w.Farms[c].Growth=10;
        var day=AtTime(w,100);Advance(day,20);Check(day.Farms[c].Growth>25,"Daylight and good temperature produce growth");
        var night=AtTime(w,300);Advance(night,20);Check(night.Farms[c].Growth==10&&night.FarmStatus(night.Farms[c]).Contains("光照不足"),"Night halts growth with an explicit reason");
        var dawn=AtTime(night,580);Advance(dawn,20);Check(dawn.Farms[c].Growth>10,"Sunrise restores growth without replanting");
        var winter=AtTime(w,90*600+100);Advance(winter,20);Check(winter.Farms[c].Growth==10&&winter.FarmStatus(winter.Farms[c]).Contains("温度过低"),"Winter cold prevents growth despite daylight");
        var summer=AtTime(w,30*600+150);Advance(summer,20);Check(summer.Farms[c].Growth==10&&summer.FarmStatus(summer.Farms[c]).Contains("温度过高"),"Hot summer afternoon pauses grain growth");
        Check(ClimateRules.TemperatureFactor("grain",5)<ClimateRules.TemperatureFactor("grain",20)&&ClimateRules.TemperatureFactor("grain",31)<1,"Cold and hot margins reduce rate");
        winter.Farms[c].Planted=false;winter.Farms[c].Growth=0;winter.SpawnItem("grain_seed",1,c);Advance(winter,3);
        Check(!winter.Jobs.Any(j=>j.Sow)&&winter.Available("grain_seed")==1,"Cold soil does not reserve or consume new seed");
        var mature=AtTime(w,90*600+100);mature.Farms[c].Growth=DemoWorld.GrowTime("grain");Advance(mature,2);
        Check(mature.Jobs.Any(j=>j.Harvest),"Mature crop can still be harvested in winter");
        var p=c+new Cell(0,0,-12);
        for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)for(int y=0;y<2;y++)w.Terrain.Excavate(p+new Cell(x,z,y));
        Check(w.SetFarm(p,p,"mushroom"),"Deep cave field");w.Farms[p].Planted=true;w.Farms[p].Prepared=true;w.Farms[p].Growth=10;
        var cave=AtTime(w,90*600+100);var climate=cave.FarmClimate(cave.Farms[p]);
        Check(climate.Light==0&&climate.Temperature>10&&climate.Rate>.9,"Deep sheltered cave buffers winter cold and supports dark growth");Advance(cave,20);
        Check(cave.Farms[p].Growth>25,"Cave mushrooms grow through winter");
        var restored=DemoWorld.LoadJson(cave.SaveJson());Check(restored.FarmClimate(restored.Farms[p])==cave.FarmClimate(cave.Farms[p])&&restored.Farms[p].Growth==cave.Farms[p].Growth,"Climate and growth round-trip deterministically");
        for(int y=p.Level+1;y<=TerrainMap.MaxLevel;y++)cave.Terrain.Excavate(new Cell(p.X,p.Z,y));
        Check(cave.FarmClimate(cave.Farms[p]).Rate==0,"Opening a shaft invalidates cave growing conditions immediately");
        Console.WriteLine("CROP_CLIMATE_OK: daylight/night, sunrise, winter cold, summer heat, slow margins, seeds, mature harvest, cave buffering and save/load");
    }
}
