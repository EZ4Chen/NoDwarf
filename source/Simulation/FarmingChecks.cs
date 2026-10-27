namespace NoDwarf.Simulation;
public static class FarmingChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld w,int seconds){for(int i=0;i<seconds*30;i++)w.Update(1d/30);}
    private static void Until(DemoWorld w,Func<bool> done,int seconds=900)
    {for(int i=0;i<seconds*30&&!done();i++)w.Update(1d/30);Check(done(),"Farming timed out: "+string.Join(";",w.Farms.Values.Select(w.FarmStatus)));}
    public static Cell SurfacePlot(DemoWorld w)
    {
        for(int x=-6;x<=6;x++)for(int z=-6;z<=6;z++)
        {
            var c=w.Camp+new Cell(x,z);
            if(Enumerable.Range(-1,5).All(dx=>Enumerable.Range(-1,4).All(dz=>w.Walkable(c+new Cell(dx,dz))&&!w.NaturalRamp(c+new Cell(dx,dz))&&w.BlockAt(c+new Cell(dx,dz,-1)) is "nodwarf:soil" or "nodwarf:grass")))return c;
        }
        throw new InvalidOperationException("No suitable surface test field");
    }
    public static void Run()
    {
        CheckGrowingZones();
        var w=new DemoWorld();w.Items.Clear();w.Stockpiles.Clear();w.Workers.RemoveRange(1,w.Workers.Count-1);
        var c=SurfacePlot(w);var worker=w.Workers[0];worker.X=c.X-1;worker.Z=c.Z;worker.Level=c.Level;
        Check(w.SetFarm(c,c,"grain"),"Surface soil supports grain");Advance(w,5);
        Check(!w.Farms[c].Planted&&!w.Jobs.Any(j=>j.Sow),"No seeds means no planting");
        var seed=w.SpawnItem("grain_seed",1,c+new Cell(-2,0));
        Advance(w,1);Check(w.Jobs.Count(j=>j.Sow)==1&&w.Available("grain_seed")==0,"One seed reserves exactly one sowing job");
        var saved=DemoWorld.LoadJson(w.SaveJson());Check(saved.Jobs.Count(j=>j.Sow)==1,"Pending sowing survives load");
        Until(saved,()=>saved.Farms[c].Planted);Check(saved.Total("grain_seed")==0,"Planting consumes physical seed");
        saved=DemoWorld.LoadJson(saved.SaveJson());
        var roof=c+new Cell(0,0,2);saved.Buildings[roof]=new(roof,BuildKind.Planks);
        double growth=saved.Farms[c].Growth;Advance(saved,10);Check(saved.Farms[c].Growth==growth,"Roof pauses sunlight crop");saved.Buildings.Remove(roof);
        Until(saved,()=>saved.Farms[c].Harvests==1);Check(saved.Total("wheat")==3&&saved.Total("grain_seed")==1,"Harvest yields food and exactly one replacement seed");
        var matureRound=DemoWorld.LoadJson(saved.SaveJson());
        Until(matureRound,()=>matureRound.Farms[c].Harvests==2);
        Check(matureRound.Total("wheat")==6&&matureRound.Total("grain_seed")==1,"Two full cycles conserve seeds and never repeat harvest on load");
        matureRound.SetFarm(c,c,"grain",true);int food=matureRound.Total("wheat");Advance(matureRound,10);
        Check(!matureRound.Farms.Any()&&!matureRound.Jobs.Any(j=>j.Farming)&&matureRound.Total("wheat")==food,"Removing plot stops automatic replant and harvest");
        var cave=new DemoWorld(flat:true);cave.Items.Clear();cave.Stockpiles.Clear();cave.Workers.RemoveRange(1,cave.Workers.Count-1);
        var p=new Cell(20,20,-5);
        for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)for(int y=0;y<2;y++)cave.Terrain.Excavate(p+new Cell(x,z,y));
        var dwarf=cave.Workers[0];dwarf.X=19;dwarf.Z=20;dwarf.Level=-5;
        Check(!cave.SetFarm(p,p,"grain"),"Underground rock cannot grow sunlight crop");
        Check(!cave.SetFarm(new Cell(8,10),new Cell(8,10),"mushroom"),"Open surface cannot grow cave fungus");
        Check(cave.SetFarm(p,p,"mushroom"),"Excavated cave supports a future soil bed");cave.SpawnItem("mushroom_spawn",1,p+new Cell(-1,0));Advance(cave,5);
        Check(!cave.Jobs.Any(j=>j.Sow),"Mushroom bed requires physical soil on first planting");
        cave.SpawnItem("soil",1,p+new Cell(-1,0));Until(cave,()=>cave.Farms[p].Planted);
        Check(cave.Total("soil")==0&&cave.Total("mushroom_spawn")==0,"Bed soil and spawn both consumed");
        cave=DemoWorld.LoadJson(cave.SaveJson());Until(cave,()=>cave.Farms[p].Harvests==2,300);
        Check(cave.Total("mushroom")==6&&cave.Total("mushroom_spawn")==1&&cave.Farms[p].Prepared,"Cave mushrooms repeat with retained bed and harvested spawn");
        var noPath=new DemoWorld();noPath.Items.Clear();noPath.Stockpiles.Clear();var source=c+new Cell(5,5);
        for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)if(Math.Abs(x)==2||Math.Abs(z)==2)
        {var wall=source+new Cell(x,z);noPath.Buildings[wall]=new(wall,BuildKind.Wall);}
        noPath.SpawnItem("grain_seed",1,source);Check(noPath.SetFarm(c,c,"grain"),"Unreachable-seed farm created");Advance(noPath,20);
        Check(!noPath.Farms[c].Planted&&noPath.Total("grain_seed")==1,"Sealed seeds cannot be consumed remotely");
        noPath.SetFarm(c,c,"grain",true);Check(noPath.Available("grain_seed")==1,"Removing unplanted farm releases reserved seeds");
        Console.WriteLine("FARMING_OK: physical seeds, reservations, two harvest cycles, sunlight, cave soil beds, save/load, unreachable stock and plot removal");
    }
    private static void CheckGrowingZones()
    {
        var w=new DemoWorld();w.Stockpiles.Clear();var c=SurfacePlot(w);
        Check(w.DesignateGrowingZone(c,c+new Cell(1,0)),"Create a two-cell growing zone");
        Check(w.DesignateGrowingZone(c+new Cell(2,0),c+new Cell(2,0)),"Adjacent designation remains a separate zone");
        int id=w.GrowingZoneAt(c)!.Id,other=w.GrowingZoneAt(c+new Cell(2,0))!.Id;
        Check(id!=other&&w.GrowingZoneAt(c+new Cell(1,0))!.Id==id,"Zone membership follows designation");
        w.DeveloperResidents(false);Advance(w,2);
        int seeds=w.Total("grain_seed");Check(w.Jobs.Any(j=>j.Sow&&j.Cell==c),"Pending seed reservation exists");
        Check(w.ConfigureGrowingZone(id,"mushroom",false),"Set zone crop and disable sowing");
        Check(!w.Jobs.Any(j=>j.Sow&&w.GrowingZoneAt(j.Cell)?.Id==id)&&w.Total("grain_seed")==seeds,"Crop edit releases reservations without losing seeds");
        Check(w.Farms[c].Crop=="mushroom"&&w.Farms[c+new Cell(1,0)].Crop=="mushroom"&&w.Farms[c+new Cell(2,0)].Crop=="grain","Settings affect only the chosen zone");
        w=DemoWorld.LoadJson(w.SaveJson());Check(!w.GrowingZones[id].AllowSowing&&w.GrowingZones[id].Crop=="mushroom","Zone settings persist");
        w.ConfigureGrowingZone(id,"grain",true);w.DeveloperResidents(true);
        Until(w,()=>w.Farms[c].Planted&&w.Farms[c+new Cell(1,0)].Planted);
        w.ConfigureGrowingZone(id,"mushroom",false);double growth=w.Farms[c].Growth;
        Check(w.Farms[c].Crop=="grain","Crop edit preserves growing plants");
        w=DemoWorld.LoadJson(w.SaveJson());Until(w,()=>w.Farms[c].Harvests>=1);
        Check(!w.Farms[c].Planted&&w.Farms[c].Crop=="mushroom"&&!w.Jobs.Any(j=>j.Sow&&j.Cell==c),"Old crop harvests, new sowing stays disabled");
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!;legacy["Version"]=5;legacy.AsObject().Remove("GrowingZones");
        foreach(var p in legacy["Farms"]!.AsArray())p!.AsObject().Remove("ZoneId");
        var migrated=DemoWorld.LoadJson(legacy.ToJsonString());Check(migrated.Farms.Values.All(p=>migrated.GrowingZones.ContainsKey(p.ZoneId)),"Legacy farms gain editable zones");
        var invalid=System.Text.Json.Nodes.JsonNode.Parse(w.SaveJson())!;invalid["Farms"]![0]!["ZoneId"]=999;
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}Check(rejected,"Reject orphaned zone references");
        w.SetFarm(c,c,"grain",true);Check(w.GrowingZones.ContainsKey(id),"Partial erase retains zone settings");
        w.SetFarm(c+new Cell(1,0),c+new Cell(1,0),"grain",true);Check(!w.GrowingZones.ContainsKey(id)&&w.GrowingZones.ContainsKey(other),"Empty zones removed without affecting neighbors");
        var trees=new DemoWorld();trees.Stockpiles.Clear();var treeCell=SurfacePlot(trees);trees.DesignateGrowingZone(treeCell,treeCell);
        int treeZone=trees.GrowingZoneAt(treeCell)!.Id;trees.ConfigureGrowingZone(treeZone,"sapling",true);trees.SpawnItem("sapling",1,treeCell+new Cell(-1,0));
        Until(trees,()=>trees.Trees.ContainsKey(treeCell));trees=DemoWorld.LoadJson(trees.SaveJson());
        Check(trees.GrowingZones[treeZone].Crop=="sapling"&&trees.Trees.ContainsKey(treeCell),"Tree planting uses zone settings and persists");
        trees.SetFarm(treeCell,treeCell,"grain",true);Check(trees.Trees.ContainsKey(treeCell),"Removing tree zone preserves existing trees");
        Console.WriteLine("GROWING_ZONES_OK: selection scope, seed cancellation, delayed crop changes, sowing switch, trees, migration, validation and partial removal");
    }
}
