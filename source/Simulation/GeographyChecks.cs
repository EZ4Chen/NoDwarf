namespace NoDwarf.Simulation;

public static class GeographyChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static IEnumerable<(int X,int Y,AtlasTile Tile)> Tiles(WorldGenerationSettings s)
    {for(int x=0;x<s.Width;x++)for(int y=0;y<s.Height;y++)yield return(x,y,s.Tile(x,y));}
    private static DemoWorld World(WorldGenerationSettings s)=>new(s.LocalSeed,region:s.Site.Region,generation:s);
    public static void Run()
    {
        var basic=new WorldGenerationSettings{Revision=2,WorldSize=65,Seed=23519};
        foreach(int size in new[]{65,129,257})foreach(int land in new[]{25,50,75})
        {
            var g=basic with {WorldSize=size,LandPercent=land};var tiles=Tiles(g).ToArray();
            double coverage=tiles.Count(t=>t.Tile.Land)/(double)tiles.Length;
            Check(Math.Abs(coverage-land/100d)<.001,"Land fraction matches independently selected world size");
            Check(g.Width==size&&g.Height==size&&g.Tile(size-1,size-1)==(g with {SiteX=size-1,SiteY=size-1}).Site,"Dimensions and selection cover last tile");
        }
        var wet=basic with {Moisture=2};var drained=wet with {Drainage=2};
        Check(Tiles(wet).Zip(Tiles(drained)).All(pair=>pair.First.Tile.Rainfall==pair.Second.Tile.Rainfall),"Drainage does not change rainfall field");
        Check(Tiles(drained).Average(t=>t.Tile.Wetness)<Tiles(wet).Average(t=>t.Tile.Wetness),"Drainage reduces retained moisture");
        Check(Tiles(basic with {Elevation=2}).Zip(Tiles(basic)).All(pair=>pair.First.Tile.Land==pair.Second.Tile.Land),"Altitude does not change land coverage");
        Check(Tiles(basic with {FeatureScale=2}).Zip(Tiles(basic with {FeatureScale=-2})).Count(p=>p.First.Tile.Land!=p.Second.Tile.Land)>500,"Terrain scale changes actual continental structure");
        Check(Tiles(basic with {Erosion=3}).Zip(Tiles(basic with {Erosion=0})).Count(p=>p.First.Tile.Height!=p.Second.Tile.Height)>100,"Erosion changes actual elevation");
        Check(Tiles(basic with {Rivers=0}).All(t=>t.Tile.RiverWidth==0),"River off removes river graph");
        Check(Tiles(basic with {Rivers=2}).Count(t=>t.Tile.RiverWidth>0)>Tiles(basic).Count(t=>t.Tile.RiverWidth>0),"River abundance increases catchments with rivers");
        var all=Tiles(wet).ToArray();Check(all.Any(t=>t.Tile.Lake),"Rainy atlas contains drainage basins with lakes");
        foreach(var entry in all.Where(t=>t.Tile.RiverWidth>0))
        {
            var seen=new HashSet<(int,int)>();var t=entry;
            while(t.Tile.Land&&t.Tile.DownstreamX>=0)
            {
                Check(seen.Add((t.X,t.Y)),"River graph must have no cycles");
                var next=wet.Tile(t.Tile.DownstreamX,t.Tile.DownstreamY);
                Check(!next.Land||next.Height<=t.Tile.Height,"River surface cannot climb uphill downstream");
                Check(!next.Land||next.Runoff+1e-8>=t.Tile.Runoff,"Runoff never disappears downstream");
                Check(Math.Abs(t.Tile.DownstreamX-t.X)+Math.Abs(t.Tile.DownstreamY-t.Y)==1,"Drainage connects adjacent regions");
                t=(t.Tile.DownstreamX,t.Tile.DownstreamY,next);
            }
        }
        var river=all.First(t=>t.Tile.RiverWidth>0&&t.X>3&&t.Y>3);
        var setting=wet with {SiteX=river.X,SiteY=river.Y,CavernLayers=3,CavernSize=2};
        var w=World(setting);
        Check(w.Terrain.GeneratorVersion==4&&w.Fluids.Cells.Count>0&&w.Terrain.RiverBoundaries.Any(b=>b.Inlet)&&w.Terrain.RiverBoundaries.Any(b=>!b.Inlet),"Atlas river creates local channel, inlet and outlet");
        Check(w.Workers.All(a=>w.Walkable(a.Tile)&&w.Fluids.Amount(a.Tile)==0)&&w.Walkable(w.Camp),"Generated water leaves supported dry resident spawns");
        Check(w.Terrain.InitialSurfaceWater.All(c=>w.Fluids.Amount(c)>0&&!w.Terrain.Solid(c)),"Surface water fits carved channel capacity");
        Check(w.Terrain.CavernFloors.Select(c=>c.Level).Distinct().Count()==3&&w.Terrain.Caverns.All(c=>c.Level>TerrainMap.MinLevel&&c.Level<w.Terrain[c.X,c.Z].Level-10),"Cavern layer count and surface/bedrock buffers");
        // A real block deck sits above the water, while the channel remains open below.
        var bed=w.Terrain.InitialSurfaceWater.Select(c=>(c.X,c.Z)).ToHashSet();
        var probe=new[]{(X:180,Z:56),(X:56,Z:180),(X:20,Z:56),(X:56,Z:20)}.First(bed.Contains);
        bool horizontal=probe.Z==56;int stage=setting.Site.Height-1;
        var deck=Enumerable.Range(-10,21).Select(offset=>new Cell(probe.X+(horizontal?0:offset),probe.Z+(horizontal?offset:0),stage)).Where(c=>bed.Contains((c.X,c.Z))).ToArray();
        Check(deck.Length>=3&&deck.All(c=>!w.Walkable(c+new Cell(0,0,1))),"Unbridged river cannot be walked across");
        foreach(var c in deck)Check(w.CanPlace(c,BuildKind.Planks)&&w.DeveloperPlace(c,BuildKind.Planks),"Bridge deck uses valid supported construction positions");
        Check(w.Navigation.FindPath(deck[0]+new Cell(0,0,1),deck[^1]+new Cell(0,0,1)) is not null&&deck.All(c=>w.Fluids.Amount(c+new Cell(0,0,-1))>0),"Bridge supports a path without replacing river water");
        var floor=w.Terrain.CavernFloors.First();var view=new WorldView(floor.Level);
        Check(!view.Known(w,floor),"Unopened natural caves remain hidden");
        var roof=w.Terrain.Caverns.Where(c=>c.X==floor.X&&c.Z==floor.Z).MaxBy(c=>c.Level)+new Cell(0,0,1);
        Check(w.Terrain.Excavate(roof)&&view.Known(w,floor),"Breaching cave discovers connected natural space");
        Check(w.Walkable(floor),"Natural cave floor supports resident navigation");
        var neighbor=w.Terrain.CavernFloors.First(c=>c.Level==floor.Level&&Math.Abs(c.X-floor.X)+Math.Abs(c.Z-floor.Z)>10);
        Check(w.Navigation.FindPath(floor,neighbor) is not null,"Natural cavern floor is present in navigation graph");
        for(int i=0;i<40;i++){w.Fluids.Advance(.2);Check(w.Fluids.Conserved,"River exchange must preserve mass ledger every tick");}
        Check(w.Fluids.RiverOut>0&&w.Fluids.RiverIn>0&&!w.Fluids.AtLimit, $"River flows through explicit bounded boundary exchange: in={w.Fluids.RiverIn}, out={w.Fluids.RiverOut}, cells={w.Fluids.Cells.Count}");
        var invalid=System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!;invalid["Generation"]!["WorldSize"]=100000;
        try{DemoWorld.LoadJson(invalid.ToJsonString());throw new Exception("Unbounded world size accepted");}catch(InvalidDataException){}
        var loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(loaded.Terrain.Generation==setting&&loaded.Terrain.GeneratorVersion==4&&loaded.Fluids.Mass==w.Fluids.Mass&&loaded.Fluids.RiverIn==w.Fluids.RiverIn&&loaded.Fluids.RiverOut==w.Fluids.RiverOut,"New parameters, water ledger and terrain version survive save");
        Check(view.Known(loaded,floor)&&loaded.Terrain.Caverns.ToHashSet().SetEquals(w.Terrain.Caverns),"Cave layout and discovery survive save without storing generated voids as player mining");
        for(int i=0;i<10;i++){w.Fluids.Advance(.2);loaded.Fluids.Advance(.2);}
        Check(w.Fluids.Save(Array.Empty<Cell>()).Cells.SequenceEqual(loaded.Fluids.Save(Array.Empty<Cell>()).Cells)&&w.Fluids.RiverIn==loaded.Fluids.RiverIn,"Saved fluid queue resumes exactly");
        var lake=all.First(t=>t.Tile.Lake);var lakeWorld=World(wet with {SiteX=lake.X,SiteY=lake.Y});
        Check(lakeWorld.Terrain.InitialSurfaceWater.Count()>1500&&lakeWorld.Fluids.Conserved,"Lake basin generates actual water");
        var site=basic.WithRecommendedSite();var poor=World(site with {Minerals=0,Aquifers=0,CavernLayers=0,SoilDepth=1});
        var rich=World(site with {Minerals=2,Aquifers=2,CavernLayers=1,SoilDepth=6});
        Check(rich.Terrain.Ores.Count>poor.Terrain.Ores.Count*2,"Mineral abundance changes ore volume");
        Check(poor.Fluids.Sources.Count==0&&rich.Fluids.Sources.Count>16&&poor.Terrain.Caverns.Count==0,"Aquifer and cavern off switches affect local terrain");
        var soil=rich.Camp+new Cell(0,0,-2);Check(rich.BlockAt(soil)=="nodwarf:soil"&&poor.BlockAt(soil)!="nodwarf:soil","Soil thickness changes actual blocks");
        var flat=new TerrainMap(site.LocalSeed,region:site.Site.Region,generation:site with {Ruggedness=-2});
        var rough=new TerrainMap(site.LocalSeed,region:site.Site.Region,generation:site with {Ruggedness=2});
        double Relief(TerrainMap t)=>Enumerable.Range(0,255).SelectMany(x=>Enumerable.Range(0,255).Select(z=>Math.Abs(t[x,z].Level-t.Region!.CampHeight))).Average();
        Check(Relief(rough)>Relief(flat),"Ruggedness changes local relief");
        foreach(int seed in new[]{0,-1,int.MaxValue})foreach(int extreme in new[]{-2,2})
        {
            var s=(basic with {Seed=seed,Temperature=extreme,Moisture=extreme,Elevation=extreme,Ruggedness=extreme,Drainage=-extreme,LandPercent=extreme<0?25:75}).WithRecommendedSite();
            var world=World(s);Check(world.Workers.All(a=>world.Walkable(a.Tile)&&world.Fluids.Amount(a.Tile)==0),"Extreme modern worlds have safe spawns");
        }
        var legacySettings=new WorldGenerationSettings{Seed=23519}.WithRecommendedSite();var legacy=World(legacySettings);
        var old=System.Text.Json.Nodes.JsonNode.Parse(legacy.SaveJson())!;old["Version"]=23;
        foreach(string field in new[]{"Revision","WorldSize","LandPercent","FeatureScale","Ruggedness","Drainage","Erosion","Rivers","Minerals","SoilDepth","Aquifers","CavernLayers","CavernSize"})old["Generation"]!.AsObject().Remove(field);
        old["Fluids"]!["Version"]=1;old["Fluids"]!.AsObject().Remove("RiverIn");old["Fluids"]!.AsObject().Remove("RiverOut");
        var restored=DemoWorld.LoadJson(old.ToJsonString());
        Check(restored.Terrain.GeneratorVersion==3&&restored.Terrain.Generation!.Revision==1&&restored.Terrain[81,92]==legacy.Terrain[81,92]&&restored.Terrain.Ores.OrderBy(p=>p.Key.X).SequenceEqual(legacy.Terrain.Ores.OrderBy(p=>p.Key.X))&&restored.Fluids.Mass==legacy.Fluids.Mass&&restored.Terrain.Caverns.Count==0,"Actual version 23 JSON without new fields preserves legacy terrain, ores and water");
        RiverBoundaryChecks();
        Console.WriteLine("GEOGRAPHY_OK: 9 size/land combinations; climate, erosion, drainage graph, local rivers/lakes, safe spawns, mineral/soil/caves, discovery, old/new saves, deterministic fluid resume");
    }
    private static void RiverBoundaryChecks()
    {
        var cells=Enumerable.Range(0,8).Select(x=>new Cell(x,0,10)).ToHashSet();var closed=new HashSet<Cell>();
        FluidSimulation Make()=>new(c=>cells.Contains(c)?1000:0,c=>cells.Contains(c)&&!closed.Contains(c),_=>{},_=>{});
        var f=Make();f.SetRiverBoundaries(new[]{new RiverBoundary(new(0,0,10),true),new RiverBoundary(new(7,0,10),false)},15);
        foreach(var c in cells)f.Add(c,1000);
        for(int i=0;i<30;i++)f.Advance(.2);
        Check(f.RiverIn>0&&f.RiverOut>0&&f.Conserved,"Short river inlet and outlet flow");
        closed.Add(new(3,0,10));f.GeometryChanged(new(3,0,10));
        for(int i=0;i<400;i++)f.Advance(.2);
        long stoppedIn=f.RiverIn,stoppedOut=f.RiverOut;
        for(int i=0;i<20;i++)f.Advance(.2);
        Check(f.RiverIn==stoppedIn&&f.RiverOut==stoppedOut&&f.Conserved,"Interior dam separates upstream from downstream after downstream drains");
        closed.Clear();f.GeometryChanged(new(3,0,10));
        for(int i=0;i<40;i++)f.Advance(.2);
        Check(f.RiverIn>stoppedIn&&f.RiverOut>stoppedOut,"Opening dam restores through-flow");
        closed.UnionWith(cells);long input=f.RiverIn,output=f.RiverOut;f.Advance(1);
        Check(f.RiverIn==input&&f.RiverOut==output&&f.Conserved,"Closed gates stop boundary exchange without deleting trapped water");
        closed.Clear();foreach(var c in cells)f.GeometryChanged(c);
        f.Advance(0);Check(f.RiverIn==input,"Paused simulation adds no boundary flow");
        var copy=Make();copy.SetRiverBoundaries(new[]{new RiverBoundary(new(0,0,10),true),new RiverBoundary(new(7,0,10),false)},15);copy.Restore(f.Save(Array.Empty<Cell>()));
        f.Advance(1.6);for(int i=0;i<8;i++)copy.Advance(.2);
        Check(f.Mass==copy.Mass&&f.RiverIn==copy.RiverIn&&f.RiverOut==copy.RiverOut&&f.Conserved&&copy.Conserved,"Speed batching preserves river accounting");
    }
}
