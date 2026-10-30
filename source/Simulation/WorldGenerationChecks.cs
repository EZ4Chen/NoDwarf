namespace NoDwarf.Simulation;
public static class WorldGenerationChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static void Run()
    {
        var settings=new WorldGenerationSettings{Seed=23519}.WithRecommendedSite();settings.Validate();
        var tile=settings.Site;var warm=settings with {Temperature=2};var wet=settings with {Moisture=2};var high=settings with {Elevation=2};
        Check(warm.Site.Temperature>tile.Temperature+13,"Global temperature changes site climate");
        Check(wet.Site.Wetness>tile.Wetness,"Global humidity changes sites");
        Check(high.Site.Height>tile.Height,"Global altitude changes site height");
        for(int x=0;x<settings.Width;x++)for(int y=0;y<settings.Height;y++)Check(settings.Tile(x,y)==(settings with {}).Tile(x,y),"Atlas is deterministic");
        var world=new DemoWorld(settings.LocalSeed,region:tile.Region,generation:settings);
        var same=new TerrainMap(settings.LocalSeed,region:tile.Region,generation:settings);
        Check(world.Camp.Level==tile.Height&&world.Walkable(world.Camp),"Chosen site creates a supported camp");
        for(int x=0;x<255;x+=7)for(int z=0;z<255;z+=7)
            Check(world.Terrain[x,z]==same[x,z]&&world.Terrain[x,z].Level is >=60 and <=112,"Terrain is deterministic and within vertical bounds");
        var warmer=new DemoWorld(warm.LocalSeed,region:warm.Site.Region,generation:warm);
        Check(warmer.OutdoorTemperature>world.OutdoorTemperature+10,"World climate changes actual colony temperature");
        var loaded=DemoWorld.LoadJson(world.SaveJson());Check(loaded.Terrain.Generation==settings&&loaded.Terrain.GeneratorVersion==TerrainMap.CurrentGeneratorVersion&&loaded.Terrain[27,56]==world.Terrain[27,56]&&loaded.OutdoorTemperature==world.OutdoorTemperature,"Generation settings survive save/load");
        var invalid=System.Text.Json.Nodes.JsonNode.Parse(world.SaveJson())!;invalid["Generation"]!["Moisture"]=99;
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}Check(rejected,"Invalid generation options rejected");
        int scenarios=0;
        foreach(int seed in new[]{0,-23519,int.MaxValue})
        foreach(int extreme in new[]{-2,0,2})
        {
            var setting=new WorldGenerationSettings{Seed=seed,Temperature=extreme,Moisture=-extreme,Elevation=extreme}.WithRecommendedSite();
            var generated=new DemoWorld(setting.LocalSeed,region:setting.Site.Region,generation:setting);
            Check(generated.Workers.All(p=>generated.Walkable(p.Tile)),"Extreme settings produce supported resident spawns");
            Check(generated.Terrain.Generation==setting&&generated.Terrain.Region!.Id==setting.Site.Region,"Atlas/local region agreement");
            Check(generated.Fluids.Sources.All(a=>TerrainMap.InBounds(a.Cell)&&a.Stored<=a.Capacity&&a.CurrentHead<=TerrainMap.MaxLevel),"Generated aquifers bounded");
            var save=DemoWorld.LoadJson(generated.SaveJson());
            foreach(var at in new[]{new Cell(0,0),new Cell(254,254),new Cell(127,127)})
                Check(generated.Terrain[at.X,at.Z]==save.Terrain[at.X,at.Z],"Extreme settings and corners survive save");
            scenarios++;
        }
        Console.WriteLine($"WORLD_GENERATION_EXTREMES_OK: {scenarios} seeds/climate/moisture/elevation scenarios, spawn support, atlas/local agreement, aquifers and save corners");
        GeographyChecks.Run();
        Console.WriteLine("WORLD_GENERATION_OK: deterministic atlas, climate/moisture/elevation effects, safe camp, current-format save validation");
    }
}
