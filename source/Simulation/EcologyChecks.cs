using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;
public static class EcologyChecks
{
    private static void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
    public static void Run()
    {
        var w=new DemoWorld(region:"grassland");w.DeveloperResidents(false);
        var grass=(from x in Enumerable.Range(1,253) from z in Enumerable.Range(1,253) let c=w.Surface(x,z) where GroundCover.At(w,c)=="grass" select c).First();
        Check(GroundCover.At(w,grass)==GroundCover.At(DemoWorld.LoadJson(w.SaveJson()),grass),"Grass repeatability across load");
        w.Stockpiles.Add(grass);Check(GroundCover.At(w,grass) is null,"Stockpile suppresses grass");w.Stockpiles.Remove(grass);
        w.Terrain.Excavate(grass+new Cell(0,0,-1));Check(GroundCover.At(w,grass) is null,"Excavated ground cannot float grass");
        foreach(string species in new[]{"acacia_sapling","jungle_sapling"})
        {
            w=new DemoWorld(region:species=="acacia_sapling"?"savanna":"rainforest");w.DeveloperResidents(false);
            var tree=w.Trees.Values.First();var shape=TreeShape.Generate(w.Terrain.Seed,tree);
            Check(tree.Species==species&&shape.Any(v=>v.Leaf)&&shape.Any(v=>!v.Leaf),"New tree species has wood and leaves");
            Check(shape.All(v=>TerrainMap.InBounds(tree.Cell+v.Offset))&&shape.Select(v=>v.Offset).Distinct().Count()==shape.Count,"Tree voxels bounded and unique");
            Check(shape.SequenceEqual(TreeShape.Generate(w.Terrain.Seed,tree)),"Tree shape deterministic");
            if(species=="acacia_sapling")Check(shape.Any(v=>!v.Leaf&&(Math.Abs(v.Offset.X)>=2||Math.Abs(v.Offset.Z)>=2)),"Acacia branching differs from straight trunk");
            int seeds=w.Total(species);Check(w.FellTree(tree.Cell)&&w.Total(species)==seeds+2,"Felling returns species seedlings");
            var c=w.Camp+new Cell(2,5);w.Items.Clear();w.Stockpiles.Clear();w.DeveloperResidents(true);w.Workers.RemoveRange(1,2);
            var worker=w.Workers[0];worker.X=c.X-1;worker.Z=c.Z;worker.Level=c.Level;worker.Person.Schedule=Enumerable.Repeat("anything",24).ToArray();
            w.SpawnItem(species,1,c+new Cell(-1,0));Check(w.QueueTree(c,false,species),"Matching seedling can be replanted");
            for(int i=0;i<1800&&!w.Trees.ContainsKey(c);i++)w.Update(1d/30);
            Check(w.Trees[c].Species==species,"Planting preserves new species");
            Check(DemoWorld.LoadJson(w.SaveJson()).Trees[c].Species==species,"Seedling persists");
        }
        var desert=new DemoWorld(region:"desert");Check(desert.Trees.Count==0,"Desert has no trees");
        int dry=0;for(int x=0;x<255;x++)for(int z=0;z<255;z++){string? cover=GroundCover.At(desert,desert.Surface(x,z));Check(cover is null or "dead_bush","Desert must not spawn green grass");if(cover is not null)dry++;}
        Check(dry>0,"Desert contains sparse dead bushes");
        // Real old-style metadata: version 8 region map and version 9 atlas map lack ecology fields.
        foreach(bool atlas in new[]{false,true})
        {
            var gen=atlas?(new WorldGenerationSettings{EcologyVersion=1}).WithRecommendedSite():null;
            var oldWorld=gen is null?new DemoWorld(region:"savanna",ecologyVersion:1):new DemoWorld(gen.LocalSeed,region:gen.Site.Region,generation:gen);
            var node=JsonNode.Parse(oldWorld.SaveJson())!;node["Version"]=atlas?9:8;node.AsObject().Remove("EcologyVersion");
            if(atlas)node["Generation"]!.AsObject().Remove("EcologyVersion");
            var loaded=DemoWorld.LoadJson(node.ToJsonString());
            Check(loaded.Terrain.EcologyVersion==1&&loaded.Terrain.Region?.Modern==false,"Old ecology stays frozen");
            for(int x=0;x<255;x+=7)for(int z=0;z<255;z+=7)Check(loaded.Terrain[x,z]==oldWorld.Terrain[x,z],"Old terrain and slopes preserved");
            Check(loaded.Trees.Values.Select(t=>(t.Cell,t.Species,t.Height)).SequenceEqual(oldWorld.Trees.Values.Select(t=>(t.Cell,t.Species,t.Height))),"Old trees preserved");
        }
        var biomes=new HashSet<string>();
        foreach(int heat in new[]{-2,0,2})foreach(int moisture in new[]{-2,0,2})
        {var atlas=new WorldGenerationSettings{Temperature=heat,Moisture=moisture};for(int x=0;x<72;x++)for(int y=0;y<44;y++){var tile=atlas.Tile(x,y);if(tile.Land)biomes.Add(tile.Region);}}
        Check(RegionProfile.All.All(p=>biomes.Contains(p.Id)),"Atlas can generate every supported biome");
        Console.WriteLine("ECOLOGY_OK: grass occupancy, acacia/jungle lifecycle, desert cover, legacy ecology and atlas diversity");
    }
}
