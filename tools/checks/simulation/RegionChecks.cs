using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class RegionChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static void Run()
    {
        var fingerprints=new HashSet<string>();
        int forestTrees=0;
        foreach(var profile in RegionProfile.All)
        {
            var w=new DemoWorld(74219,region:profile.Id);w.DeveloperResidents(false);
            var same=new TerrainMap(74219,region:profile.Id);
            int min=128,max=-64,trees=w.Trees.Count;long hash=0;
            for(int x=0;x<TerrainMap.Size;x++)for(int z=0;z<TerrainMap.Size;z++)
            {
                var g=w.Terrain[x,z];Check(g==same[x,z],"Region generation is repeatable");
                min=Math.Min(min,g.Level);max=Math.Max(max,g.Level);hash=unchecked(hash*31+g.Level*17+(int)g.Kind);
                Check(g.Level>=64&&g.Level<=122,"Regional terrain stays in normal world bounds");
                Check(w.Terrain.BlockAt(new Cell(x,z,-64))=="nodwarf:bedrock"&&w.Terrain.BlockAt(new Cell(x,z,-1)) is "nodwarf:deepslate" or "nodwarf:deepslate_iron_ore" or "nodwarf:deepslate_coal_ore","Deep layer policy is unchanged");
                Check(BlockCatalog.Get(g.BlockId).Id==g.BlockId,"All generated ground/slope IDs registered");
            }
            Check(fingerprints.Add(hash.ToString()),"Profiles must produce distinct maps");
            Check(w.Camp.Level==profile.CampHeight&&w.Walkable(w.Camp),"Camp is safe at profile elevation");
            Check(w.Trees.All(t=>t.Key.Level+t.Value.Height<=128),"Trees respect ceiling");
            if(profile.Id=="boreal")Check(w.Trees.Values.Count(t=>t.Species=="pine_sapling")>trees*.7,"Boreal trees mostly conifers");
            if(profile.Id=="temperate")forestTrees=trees;
            if(profile.Id=="savanna")Check(trees<forestTrees/2,"Savanna must be visibly sparser than forest");
            if(profile.Id=="savanna")Check(w.Trees.Values.All(t=>t.Species=="acacia_sapling"&&t.Height<=6),"Savanna uses short acacias");
            // Mine an actual new rock, persist damage/excavation and check its physical drop.
            var c=w.Camp+new Cell(0,0,-8);string block=w.BlockAt(c);Check(BlockCatalog.IsRegionalRock(block),"Regional host rock generated");
            int count=w.Total("cobblestone");Check(w.DamageBlock(c,1000),"Regional rock can be mined");
            Check(w.Total("cobblestone")==count+1,"Regional rock drops usable stone");
            var loaded=DemoWorld.LoadJson(w.SaveJson());
            Check(loaded.Terrain.Region==profile&&loaded.Terrain.GeneratorVersion==TerrainMap.CurrentGeneratorVersion&&loaded.BlockAt(c)=="nodwarf:air"&&loaded.Total("cobblestone")==w.Total("cobblestone"),"Region and excavations survive reload");
            Check(loaded.Trees.Count==trees&&loaded.Terrain[17,29]==w.Terrain[17,29],"Reload keeps landscape and trees");
            Console.WriteLine($"REGION_SAMPLE: {profile.Id} height={min}..{max} trees={trees} fingerprint={hash}");
        }
        var winter=new CalendarDate(90*1440+12*60);
        var warm=RegionProfile.Get("savanna");var cold=RegionProfile.Get("alpine");
        double a=ClimateRules.OutdoorTemperature(winter,warm.CampHeight,warm),b=ClimateRules.OutdoorTemperature(winter,cold.CampHeight,cold);
        Check(a>15&&b<0&&ClimateRules.TemperatureFactor("cotton",a)>0&&ClimateRules.TemperatureFactor("cotton",b)==0,"Region temperatures change crop viability");
        foreach(string id in new[]{"humus","dry_grass","gravel","granite","limestone","sandstone"})
            foreach(string suffix in new[]{"","_ramp"})Check(ItemCatalog.Drop("nodwarf:"+id+suffix) is not null&&BlockCatalog.Get("nodwarf:"+id+suffix).Durability>0,"New materials have drops and durability");
        EcologyChecks.Run();
        Console.WriteLine("REGION_OK: ten reproducible regions, bounds, ecology, climate, drops and save roundtrip");
    }
}
