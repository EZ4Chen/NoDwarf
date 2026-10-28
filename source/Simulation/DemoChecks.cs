namespace NoDwarf.Simulation;

public static class DemoChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Advance(DemoWorld world, int seconds)
    { for (int i = 0; i < seconds * 30; i++) world.Update(1.0 / 30); }

    public static void Run()
    {
        CombatChecks.Run();
        LadderChecks.Run();
        FallSafetyChecks.Run();
        ClimbingChecks.Run();
        BuildingGravityChecks.Run();
        BlockExposureChecks.Run();
        ResidentChecks.Run();
        LogisticsChecks.Run();
        ConstructionChecks.Run();
        RoomEnvironmentChecks.Run();
        SettlementChecks.Run();
        CropProductionChecks.Run();ProductionOrderChecks.Run();
        TreeShapeChecks.Run();
        RegionChecks.Run();
        WorldGenerationChecks.Run();
        FarmingChecks.Run();
        CalendarChecks.Run();
        WeatherChecks.Run();
        FluidChecks.Run();
        CropClimateChecks.Run();
        DeveloperChecks();
        CharacterChecks();
        var clock = new SimulationClock(); clock.Advance(1);
        Check(clock.Tick == 30, "Clock normal speed");
        clock.Paused = true; clock.Advance(1); Check(clock.Tick == 30, "Clock pause");
        clock.Step(); Check(clock.Tick == 31, "Clock single step");
        clock.Paused = false; clock.SetSpeed(3); clock.Advance(1); Check(clock.Tick == 121, "Clock acceleration");

        foreach (var kind in Enum.GetValues<GroundKind>())
            Check(BlockCatalog.Get(BlockCatalog.Id(kind)).Id == new Ground(0, kind).BlockId, "Terrain type has registered stable ID");
        foreach (var kind in Enum.GetValues<BuildKind>())
            Check(BlockCatalog.Get(BlockCatalog.Id(kind)).Id == new Building(new Cell(0, 0), kind).BlockId, "Building type has registered stable ID");
        Check(BlockCatalog.Id(GroundKind.Rock) == "nodwarf:stone", "Persistent ID independent of enum ordinal");
        var foundation = new FoundationPlan(new Cell(10, 10, 2), new Cell(14, 15, 2));
        Check(foundation.WorldLevel == 2, "Foundation top is local floor zero");
        foundation.Step(-1); Check(foundation.WorldLevel == -1, "Basement floor bookmark");
        foundation.SetFloorHeight(4); Check(foundation.WorldLevel == -2, "Floor height is floor-to-floor distance");
        foundation.Step(1); Check(foundation.WorldLevel == 2, "Changing floor spacing preserves zero datum");
        Check(TerrainMap.LayerHeight == 1f && WorldScale.DwarfHeightMeters == 1.4f, "World uses requested metric scale");
        UndergroundChecks();
        MultiLevelChecks();
        MaterialAndStairChecks();
        IndependentConstructionChecks();
        TerrainChecks();

        var world = new DemoWorld(flat: true);
        Check(!world.QueueSword() && world.Iron == 12, "Invalid order must not spend resources");
        var parts = DemoWorld.ExampleBlueprint();
        Check(world.QueueBlueprint(new Cell(5, 4), parts), "Example blueprint queue");
        int materials = world.Stone;
        Check(!world.QueueBlueprint(new Cell(5, 4), parts) && world.Stone == materials, "Duplicate blueprint must be atomic");
        Advance(world, 240);
        Check(world.Buildings.Count == parts.Count && world.Jobs.Count == 0, "All construction must complete");
        Check(world.WorkshopReady, "Enclosed workshop must be recognized");
        Check(world.FindPath(new Cell(7, 9), new Cell(7, 7)) is not null, "Door must be traversable");
        Check(!world.Walkable(new Cell(5, 4)), "Walls must block movement");
        Check(world.QueueSword() && world.QueueSword(), "Two production orders");
        Advance(world, 120);
        Check(world.Swords == 2 && world.Iron == 7 && world.Coal == 6, "Production resources and output");
        world.SaveBlueprint(); Check(world.Blueprint.Count == parts.Count, "Blueprint save");
        Check(world.QueueBlueprint(new Cell(11, 4), world.Blueprint), "Blueprint reuse");
        Advance(world, 240);
        Check(world.Buildings.Count == parts.Count * 2,
            "Second workshop construction: " + world.Buildings.Count + "; jobs=" +
            string.Join(" | ", world.Jobs.Select(j => $"{j.Cell} {j.Build} worker={j.Worker} progress={j.Progress}")) +
            "; workers=" + string.Join(" | ", world.Workers.Select(w => $"{w.Name} {w.Tile} {w.Status} path={w.Path.Count}")));
        world.Erase(new Cell(7, 8));
        Check(world.WorkshopReady, "Second workshop survives first room being opened");
        world.Erase(new Cell(13, 8)); Check(!world.WorkshopReady, "Open rooms must become invalid");
        int stone = world.Stone;
        Check(world.QueueBuild(new Cell(0, 0), BuildKind.Wall), "Single plan");
        world.Erase(new Cell(0, 0)); Check(world.Stone == stone, "Cancel returns reserved material");
        Check(world.Workers.All(w => world.Walkable(w.Tile)), "No resident trapped inside solid building");
    }
    private static void DeveloperChecks()
    {
        var world=new DemoWorld(flat:true);
        var appearances=world.Workers.Select(w=>w.Appearance).ToArray();
        Check(world.DeveloperResidents(false)&&world.Workers.Count==0,"Remove residents for creative building");
        int stone=world.Stone,wood=world.Wood;
        var c=new Cell(20,20,-3);
        var view=new WorldView(-3);
        Check(!view.Known(world,c),"Underground interiors remain unknown");
        Check(world.DeveloperDig(c,height:2),"Creative excavation without workers");
        Check(world.Total("deep_rubble")==2,"Creative excavation shares physical drop rules");
        Check(view.Known(world,c)&&view.Known(world,c+new Cell(1,0)),"Air and enclosing rock walls become visible");
        Check(!view.Known(world,c+new Cell(2,0)),"Rock beyond enclosing wall remains fogged");
        Check(view.CornerRim(world,c+new Cell(1,1))&&!view.Known(world,c+new Cell(1,1)),"Diagonal rock is a dark rim, not discovered contents");
        Check(!view.CornerRim(world,c+new Cell(2,2)),"Rim cannot spread deeper into unknown rock");
        Check(!view.Visible(world,c+new Cell(0,0,1)),"Slice hides cells above selected height");
        Check(new WorldView(-3,true).Known(world,c+new Cell(2,0)),"Developer reveal bypasses knowledge only");
        Check(world.NaturalSolid(c+new Cell(0,0,2)),"Viewing does not remove ceiling");
        Check(world.DeveloperPlace(c,BuildKind.Anvil),"Creative underground workstation");
        Check(world.DeveloperErase(c)&&world.DeveloperPlace(new Cell(20,20,8),BuildKind.StoneBricks),"Creative floating block");
        Check(world.Stone==stone&&world.Wood==wood&&world.Jobs.Count==0,"Creative placement does not spend resources or require jobs");
        Check(world.DeveloperResidents(true)&&world.Workers.Count==3,"Restore residents");
        Check(world.Workers.Select(w=>w.Appearance).SequenceEqual(appearances),"Restore original personalities and appearances");
        var pit=new Cell(25,25,-2);
        int priorStone=world.Stone;
        Check(world.QueueDig(pit,true),"Plan stair clearance only");
        Check(world.Stone==priorStone && world.Jobs.Single().Build is null && world.Jobs.Single().DigHeight==3,"Pit reserves no stair materials");
        world.DeveloperResidents(false);world.DeveloperFinishPlans();
        Check(!world.Buildings.ContainsKey(pit)&&!world.NaturalSolid(pit+new Cell(0,0,2)),"Excavation does not generate a staircase");
        Check(world.QueueBuild(pit,BuildKind.PlankStairs,facing:2),"Choose and install wood stairs after excavation");
        world.DeveloperFinishPlans();
        Check(world.Buildings[pit].Kind==BuildKind.PlankStairs,"Independent stair material honored");
        var clearance=new DemoWorld(flat:true);var blockedStair=new Cell(20,20,-2);
        clearance.Terrain.Excavate(blockedStair);
        Check(!clearance.CanPlace(blockedStair,BuildKind.PlankStairs)&&!clearance.CanDeveloperPlace(blockedStair,BuildKind.PlankStairs),"Both building modes reject blocked stair clearance");
        clearance.Terrain.Excavate(blockedStair+new Cell(0,0,1));
        Check(clearance.CanPlace(blockedStair,BuildKind.PlankStairs)&&clearance.CanDeveloperPlace(blockedStair,BuildKind.PlankStairs),"Both building modes accept clear stairs");
        var basement=new DemoWorld();basement.DeveloperResidents(false);
        Check(basement.StartUndergroundDemo(),"Queue basement without residents");
        Check(basement.DeveloperFinishPlans()>0&&basement.WorkshopReady&&basement.Jobs.Count==0,"Finish staged basement without residents");
        string folder=Path.Combine(Path.GetTempPath(),"nodwarf-log-check-"+Guid.NewGuid());
        try
        {
            string file;
            using(var log=new SessionLog(folder))
            {
                file=log.FilePath;
                for(int i=0;i<1005;i++)log.Write(i,"INFO","test","entry "+i);
                Check(log.Snapshot().Length==1000&&log.Version==1005,"Bounded console history");
                Check(log.WriteError is null,"Log file writable");
            }
            Check(File.ReadLines(file).Count()==1005,"Persistent log retains full session");
            using var json=System.Text.Json.JsonDocument.Parse(File.ReadLines(file).Last());
            Check(json.RootElement.GetProperty("Tick").GetInt64()==1004,"Structured log preserves tick");
        }
        finally {if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    private static void CharacterChecks()
    {
        var world = new DemoWorld(74219);
        var same = new DemoWorld(74219);
        Check(world.Workers.Select(w=>w.Appearance).SequenceEqual(same.Workers.Select(w=>w.Appearance)), "Seeded character appearance");
        Check(world.Workers.Select(w=>w.Appearance).Distinct().Count()==3, "Founders have distinct appearances");
        var appearances = Enumerable.Range(0, 100).Select(i=>CharacterAppearance.Randomize(new Random(i))).ToArray();
        Check(appearances.Distinct().Count()>90, "Random appearance diversity");
        Check(appearances.All(a=>a==a.Normalize()), "Random IDs valid");
        var original = world.Workers[0].Appearance;
        world.Workers[0].Appearance = original with { Body="broad", Hair="braid", Skin="deep", Beard="full" };
        string path = Path.Combine(Path.GetTempPath(), "nodwarf-characters-"+Guid.NewGuid()+".json");
        try
        {
            AppearanceStorage.Save(path, world.Workers);
            var restored = new DemoWorld(18);
            restored.Workers.Reverse();
            AppearanceStorage.Load(path, restored.Workers);
            Check(restored.Workers.Single(w=>w.Id==world.Workers[0].Id).Appearance==world.Workers[0].Appearance, "Persistent IDs survive reordering and new maps");
            Check(restored.Workers.All(w=>w.Job is null && w.Path.Count==0), "Cosmetic load does not change simulation");
            File.WriteAllText(path, "{broken");
            var before = restored.Workers.Select(w=>w.Appearance).ToArray();
            try { AppearanceStorage.Load(path, restored.Workers); throw new InvalidOperationException("Corrupt archive accepted"); }
            catch (System.Text.Json.JsonException) { }
            Check(before.SequenceEqual(restored.Workers.Select(w=>w.Appearance)), "Corrupt archive never partially applies");
            var migrated = AppearanceStorage.Deserialize("{\"Version\":1,\"Residents\":{\"azhen\":{\"Hair\":\"missing\",\"Skin\":null}}}");
            Check(migrated["azhen"].Hair=="crop" && migrated["azhen"].Skin=="warm", "Unknown and missing IDs fall back safely");
            try { AppearanceStorage.Deserialize("{\"Version\":99}"); throw new InvalidOperationException("Future version accepted"); }
            catch (InvalidDataException) { }
        }
        finally { if(File.Exists(path)) File.Delete(path); if(File.Exists(path+".tmp")) File.Delete(path+".tmp"); }
    }
    private static void UndergroundChecks()
    {
        foreach(bool flat in new[]{true,false})
        {
            var w=new DemoWorld(flat:flat);var o=w.Camp;var target=o+new Cell(3,2,-3);
            Check(w.NaturalSolid(target)&&!w.Walkable(target),"Underground starts solid");
            Check(w.StartUndergroundDemo(),"Underground sequence starts");
            bool below=false;for(int i=0;i<30*240;i++){w.Update(1.0/30);below|=w.Workers.Any(p=>p.Level==o.Level-3);}
            Check(w.Underground!.Finished&&w.Jobs.Count==0,"Underground excavation and construction complete: stage="+w.Underground.Stage+" jobs="+string.Join("|",w.Jobs.Select(j=>$"{j.Cell} dig={j.Dig} worker={j.Worker}"))+" workers="+string.Join("|",w.Workers.Select(p=>$"{p.Tile}:{p.Status}")));
            Check(below&&w.Walkable(target),"Workers actually descend into excavated room");
            Check(w.Workers.All(p=>w.Walkable(p.Tile)),"No worker is left floating or embedded after excavation");
            Check(w.NaturalSolid(target+new Cell(0,0,2)),"Excavating room preserves natural roof");
            Check(w.WorkshopReady&&w.QueueSword(),"Natural rock walls enclose underground workshop");Advance(w,30);Check(w.Swords==1,"Underground sword produced");
            var surface=o+new Cell(2,8);Check(w.FindPath(surface,target) is not null&&w.FindPath(target,surface)is not null,"Stairs connect surface and basement bidirectionally");
            var wall=o+new Cell(5,2,-3);
            Check(w.QueueDig(wall),"Plan lower wall voxel");Advance(w,30);
            Check(!w.NaturalSolid(wall)&&w.NaturalSolid(wall+new Cell(0,0,1)),"Single voxel mining preserves block above");
            Check(w.QueueDig(wall+new Cell(0,0,1)),"Plan upper wall voxel separately");Advance(w,30);
            Check(!w.NaturalSolid(wall+new Cell(0,0,1))&&w.NaturalSolid(wall+new Cell(0,0,2)),"Residents can mine upper voxel over excavated air");
            w.Erase(o+new Cell(2,6,-2));Check(w.FindPath(target,surface)is null,"Removing underground stair removes safe return route");
            Check(!w.QueueDig(target)&&!w.QueueDig(new Cell(10,10,TerrainMap.MinLevel)),"Empty space and bedrock cannot be mined twice");
            var remote=o+new Cell(10,0,-3);Check(w.QueueDig(remote),"Unreachable mining can be planned");Advance(w,5);Check(w.NaturalSolid(remote),"Residents cannot remotely mine sealed rock");w.Erase(remote);Check(!w.Jobs.Any(j=>j.Cell==remote),"Mining plans cancel safely");
        }
        Console.WriteLine("UNDERGROUND_OK: excavation, preserved ceiling, stairs, basement workshop, production, disconnected routes and cancellation");
    }

    private static void MultiLevelChecks()
    {
        var world=new DemoWorld(flat:true);
        var unrelated=new Cell(0,0);
        foreach(var c in new[]{new Cell(1,0),new Cell(0,1),new Cell(1,1)})world.Buildings[c]=new Building(c,BuildKind.Wall);
        Check(world.QueueBuild(unrelated,BuildKind.Wall),"Unreachable work at a different site");
        Check(world.StartTwoStoreyDemo(),"Two-storey sequence starts");
        bool visitedUpper=false;
        for(int i=0;i<30*900;i++) { world.Update(1.0/30);visitedUpper|=world.Workers.Any(w=>w.Level==3); }
        Check(world.Jobs.Count==1&&world.Jobs[0].Cell==unrelated,"Two-storey staging ignores unrelated blocked work: stage="+world.Demo!.Stage+" jobs="+string.Join("|",world.Jobs.Select(j=>$"{j.Cell}:{j.Build}:{j.Worker}:haul={j.Haul}:"+string.Join(",",j.Materials.Select(r=>$"{r.Item}/{r.Stage}")))));
        world.Erase(unrelated);
        Check(world.Demo!.Finished && world.Jobs.Count==0,"Two-storey construction completes: stage="+world.Demo.Stage+" jobs="+string.Join("|",world.Jobs.Select(j=>$"{j.Cell}:{j.Build}:{j.Worker}"))+" workers="+string.Join("|",world.Workers.Select(w=>$"{w.Tile}:{w.Status}")));
        Check(visitedUpper,"Workers actually climbed upstairs during construction");
        var ground=world.Camp+new Cell(2,8);var upper=world.Camp+new Cell(2,3,3);
        Check(world.FindPath(ground,upper) is not null && world.FindPath(upper,ground) is not null,"Continuous three-stair route ascends and descends");
        Check(world.WorkshopReady && world.QueueSword(),"Upper workshop accepts production"); Advance(world,60);
        Check(world.Swords==1,"Upstairs workers forge iron sword");
        var lowRoom=world.Camp+new Cell(2,2);
        Check(world.Walkable(lowRoom)&&world.Walkable(lowRoom+new Cell(0,0,3)),"Two traversable floors share same X/Z");
        Check(world.BlockAt(lowRoom+new Cell(0,0,2))=="nodwarf:planks","Slab is one voxel serving both floor and ceiling");
        // Put the resident on a known built floor; cheaper routes may now leave it outdoors.
        world.Workers[0].Path.Clear();world.Workers[0].TravelFrom=null;world.Workers[0].X=upper.X;world.Workers[0].Z=upper.Z;world.Workers[0].Level=upper.Level;
        var occupiedFloor=world.Workers[0].Tile+new Cell(0,0,-1);var savedStock=world.Wood;
        world.Erase(occupiedFloor);Check(world.Buildings.ContainsKey(occupiedFloor)&&world.Wood==savedStock,"Do not remove occupied floor beneath a resident");
        var returning=world.Workers[0];var descent=world.FindPath(returning.Tile,ground);
        Check(descent is not null,"Worker can return downstairs");foreach(var step in descent!)returning.Path.Enqueue(step);
        Advance(world,30);Check(returning.Tile==ground,"Resident actually walks downstairs");
        var stair=world.Camp+new Cell(2,6,1); world.Erase(stair);
        Check(world.FindPath(ground,upper) is null,"Removing middle stair blocks ascent even if a safe downward jump remains");
        Check(world.QueueBuild(stair,BuildKind.PlankStairs,facing:3),"Stair can be repaired"); Advance(world,60);
        Check(world.FindPath(ground,upper) is not null,"Repair restores upstairs access");
        // One metre headroom blocks a 1.4 m resident even when the floor still exists.
        var obstruct=world.Camp+new Cell(2,2,4);world.Buildings[obstruct]=new Building(obstruct,BuildKind.Planks);
        Check(!world.Walkable(lowRoom+new Cell(0,0,3)),"Low ceiling blocks resident body");
        Console.WriteLine("MULTILEVEL_OK: built two floors and roof, ascended, forged, descended, removed and repaired stairs; headroom checked");
    }

    private static void MaterialAndStairChecks()
    {
        foreach (var kind in new[] { BuildKind.Log, BuildKind.Planks, BuildKind.Cobblestone, BuildKind.StoneBricks })
        {
            var world = new DemoWorld(flat: true);
            var parts = DemoWorld.ExampleBlueprint().Select(p => p.Kind == BuildKind.Wall ? p with { Kind = kind } : p).ToList();
            Check(world.QueueBlueprint(new Cell(5, 4), parts), "Material room blueprint accepted");
            Advance(world, 120); Check(world.WorkshopReady, "New material encloses a functional workshop: " + kind);
            var built = new Cell(7, 4); int stone = world.Stone, wood = world.Wood;
            world.Erase(built); var cost = BlockCatalog.Get(kind).Cost;
            Check(world.Stone == stone + cost.Stone && world.Wood == wood + cost.Wood, "Material demolition refunds correct stock");
            Check(!world.WorkshopReady, "Opening material wall invalidates room");
        }
        var kinds = new[] { BuildKind.LogStairs, BuildKind.PlankStairs, BuildKind.CobbleStairs, BuildKind.BrickStairs };
        for (int facing = 0; facing < 4; facing++)
        {
            var world = new DemoWorld(); var kind = kinds[facing]; Cell? found = null;
            var direction = DemoWorld.StairDirection(facing);
            for (int x = world.Camp.X-35; x <= world.Camp.X+35 && found is null; x++)
            for (int z = world.Camp.Z-35; z <= world.Camp.Z+35 && found is null; z++)
            {
                var c = world.Surface(x, z);
                if (!world.CanPlace(c, kind, facing) || !world.Walkable(c+new Cell(direction.X,direction.Z,1)) || !world.Walkable(c+new Cell(-direction.X,-direction.Z))) continue;
                var low = c + new Cell(-direction.X, -direction.Z);
                var path = world.FindPath(world.Workers[0].Tile, low);
                if (path is not null && path.Count < 100) found = c;
            }
            Check(found.HasValue, "Reachable stair site exists for direction " + facing);
            var site = found!.Value;
            var high = site + new Cell(direction.X, direction.Z, 1);
            Check(world.Connected(site, high)&&world.TraversalCost(site,high)==3, "One-metre ledge can be climbed slowly before staircase");
            int stone = world.Stone, wood = world.Wood;
            world.Buildings[site]=new Building(site,kind,(facing+2)%4);
            Check(!world.Connected(site,high),"Reversed stairs cannot climb the cliff"); world.Buildings.Remove(site);
            Check(world.QueueBuild(site, kind, facing: facing), "Stair plan accepted");
            Advance(world, 180); Check(world.Buildings.ContainsKey(site), "Residents construct stairs");
            Check(world.Connected(site, high) && world.Connected(high, site), "Staircase allows ascent and descent");
            Check(world.FindPath(site, high)?.Count == 1 && world.FindPath(high, site)?.Count == 1, "A* uses stair link in both directions");
            Check(Math.Abs(world.StandingHeight(site.X + direction.X * .49, site.Z + direction.Z * .49) - high.Level * TerrainMap.LayerHeight) < .02, "Stair walking reaches upper platform height");
            world.SaveBlueprint(); Check(world.Blueprint.Single().Facing == facing, "Existing blueprint preserves stair orientation");
            var resident = world.Workers[0]; resident.X = site.X; resident.Z = site.Z; resident.Level = site.Level; resident.Path.Enqueue(high);
            world.Erase(site); world.Update(1.0 / 30);
            Check(world.Connected(site, high)&&world.TraversalCost(site,high)==3, "Removing stairs retains slower one-metre climbing route");
            var previousStep = resident.Tile;
            foreach (var step in resident.Path)
            { Check(world.Connected(previousStep, step), "Replacement route cannot use removed stair link"); previousStep = step; }
            Check(resident.Path.Count == 0 || previousStep == high, "Stair removal either stops or finds a valid detour");
        }
        var terrain = new TerrainMap(74219);
        var rampTypes = new HashSet<string>(); int sand = 0;
        for (int x = 0; x < TerrainMap.Size; x++)
        for (int z = 0; z < TerrainMap.Size; z++)
        { var g = terrain[x, z]; if (g.Kind == GroundKind.Sand) sand++; if (g.Ramp) rampTypes.Add(g.BlockId); }
        Check(sand > 0 && rampTypes.Contains("nodwarf:sand_ramp") && rampTypes.Contains("nodwarf:soil_ramp") && rampTypes.Contains("nodwarf:stone_ramp"), "All natural materials and ramp types appear");
        Console.WriteLine("MATERIAL_OK: four material rooms, four staircase directions, path invalidation and three natural ramp types");
    }

    private static void IndependentConstructionChecks()
    {
        var world = new DemoWorld(flat: true);
        // A corner wall can never be reached; it must not block a separate workshop.
        var blocked = new Cell(0, 0);
        world.Buildings[new Cell(1, 0)] = new Building(new Cell(1, 0), BuildKind.Wall);
        world.Buildings[new Cell(0, 1)] = new Building(new Cell(0, 1), BuildKind.Wall);
        world.Buildings[new Cell(1, 1)] = new Building(new Cell(1, 1), BuildKind.Wall);
        Check(world.QueueBuild(blocked, BuildKind.Wall), "Unreachable wall plan accepted");
        Check(world.QueueBlueprint(new Cell(5, 4), DemoWorld.ExampleBlueprint()), "Independent workshop queued");
        Advance(world, 120);
        Check(world.Jobs.Any(j => j.Cell == blocked), "Unreachable wall remains waiting");
        Check(world.WorkshopReady, "Remote unreachable wall must not block workshop furniture");
        Check(world.QueueSword(), "Independent workshop operates despite remote pending construction");
        Advance(world, 20); Check(world.Swords == 1, "Independent workshop produces sword");
        // Also cover individually planned furniture, without blueprint grouping.
        Check(world.QueueBuild(new Cell(20, 12), BuildKind.Furnace), "Independent manual furnace queued");
        Advance(world, 60);
        Check(world.Buildings.ContainsKey(new Cell(20, 12)), "Manual furniture is independent of remote shell");
    }

    private static void TerrainChecks()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var world = new DemoWorld(); var same = new TerrainMap(world.Terrain.Seed); var different = new TerrainMap(3841);
        Check(TerrainMap.Size==255 && TerrainMap.MaxLevel-TerrainMap.MinLevel+1==192 && TerrainMap.NormalMinLevel-TerrainMap.MinLevel==64 && TerrainMap.MaxLevel-TerrainMap.NormalMinLevel+1==128,"Expanded world dimensions");
        var flat=new DemoWorld(flat:true);
        Check(flat.Terrain.BlockAt(new Cell(10,10,-64))=="nodwarf:bedrock" && flat.Terrain.BlockAt(new Cell(10,10,-1))=="nodwarf:deepslate" && !TerrainMap.IsDeep(new Cell(10,10,0)),"Exact geological layer boundary");
        foreach(var outside in new[]{new Cell(-1,0),new Cell(255,0),new Cell(0,255),new Cell(0,-1),new Cell(10,10,-65),new Cell(10,10,128)})
        {
            Check(!DemoWorld.Inside(outside)&&!flat.Terrain.Solid(outside)&&flat.Terrain.BlockAt(outside)=="nodwarf:air","No natural block outside world");
            Check(!flat.Terrain.Excavate(outside)&&!flat.CanPlace(outside)&&!flat.CanDeveloperPlace(outside,BuildKind.Wall),"Edits respect all six boundaries");
            bool rejected=false;try{flat.Buildings[outside]=new Building(outside,BuildKind.Wall);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"Direct building writes also respect bounds");
        }
        var top=new Cell(254,254,TerrainMap.MaxLevel);
        Check(flat.CanDeveloperPlace(top,BuildKind.Wall)&&!flat.CanDeveloperPlace(top,BuildKind.Furnace)&&!flat.CanDeveloperPlace(top,BuildKind.PlankStairs),"Topmost block allowed, taller fixtures and stair clearance bounded");
        Check(!flat.CanDig(top,height:3),"Multi-voxel digging stays in world");
        var ore=world.Terrain.Ores;
        Check(ore.Count>1000 && ore.Keys.Any(c=>c.Level<-40),"Deep ore veins generated");
        Check(ore.Count==same.Ores.Count && ore.All(p=>same.Ores.TryGetValue(p.Key,out var k)&&k==p.Value),"Ore seed determinism");
        var adjacent=new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1),new Cell(0,0,1),new Cell(0,0,-1)};
        Check(ore.All(p=>adjacent.Any(d=>ore.TryGetValue(p.Key+d,out var k)&&k==p.Value)),"Ore forms connected clusters rather than isolated spots");
        Check(ore.All(p=>TerrainMap.InBounds(p.Key)&&p.Key.Level>TerrainMap.MinLevel&&world.Terrain.Solid(p.Key)),"All ore stays in solid in-bounds rock");
        Check(ore.Any(p=>TerrainMap.IsDeep(p.Key)&&p.Value==GroundKind.Iron)&&ore.Any(p=>!TerrainMap.IsDeep(p.Key)&&p.Value==GroundKind.Coal),"Both geological zones contain their ores");
        var sample=ore.First();
        Check(world.Terrain.BlockAt(sample.Key)==TerrainMap.OreId(sample.Key,sample.Value),"Ore block identity");
        Check(world.Terrain.Excavate(sample.Key)&&world.Terrain.BlockAt(sample.Key)=="nodwarf:air","Mined ore becomes air");
        Check(world.Terrain.Excavate(new Cell(254,254,-63))&&!world.Terrain.Excavate(new Cell(254,254,-64)),"Deep edge mining and bedrock boundary");
        int differentCount = 0, ramps = 0, cliffs = 0; var heights = new HashSet<int>();
        for (int x = 0; x < DemoWorld.Width; x++)
        for (int z = 0; z < DemoWorld.Depth; z++)
        {
            var c = world.Surface(x, z); var g = world.Terrain[x, z]; heights.Add(c.Level);
            Check(c.Level>=TerrainMap.SeaLevel && c.Level<=TerrainMap.MaxTerrainHeight,"Generated surface has positive absolute altitude");
            Check(g == same[x, z], "Seed must reproduce all terrain and vegetation");
            if (g != different[x, z]) differentCount++;
            if (g.Ramp)
            {
                ramps++;
                var high = world.Surface(x + g.RampX, z + g.RampZ);
                Check(world.Terrain.Connected(c, high) && world.Terrain.Connected(high, c), "Ramp is bidirectional");
                Check(!world.CanPlace(c), "Cannot build on ramp");
                var path = world.FindPath(c, high);
                Check(path is not null && path.Count == 1, "Resident can ascend explicit ramp");
            }
            foreach (var next in world.Neighbors(c))
                if (c.Level != next.Level && !world.Terrain.Connected(c, next)) cliffs++;
            if (g.Tree) Check(!world.Walkable(c) && !world.CanPlace(c), "Trees block movement and construction");
            if(!world.Terrain[c.X,c.Z].Ramp && !world.Neighbors(c).Any(n=>n.Level>c.Level&&world.NaturalSolid(n)))
                Check(!world.CanPlace(c with { Level = c.Level + 1 }), "Cannot place unsupported upper block");
        }
        Check(heights.Count >= 4 && ramps > 20 && cliffs > 20 && differentCount > 1000, "Meaningful generated terrain variation");
        var parts = DemoWorld.ExampleBlueprint();
        Check(world.QueueBlueprint(world.Camp, parts), "Camp provides a flat construction site");
        Advance(world, 120);
        Check(world.WorkshopReady && world.Jobs.Count == 0, "Generated map workshop builds successfully");
        Check(world.QueueSword(), "Generated map production order"); Advance(world, 30);
        Check(world.Swords == 1, "Generated map production completes");
        var uneven = Enumerable.Range(1, 90).SelectMany(x => Enumerable.Range(1, 90).Select(z => world.Surface(x, z)))
            .First(c => parts.Any(p => !world.Terrain.Contains(c + p.Offset)));
        int stone = world.Stone;
        Check(!world.QueueBlueprint(uneven, parts) && world.Stone == stone, "Uneven blueprint rejected without spending");
        Cell? elevated = null;
        for (int x = TerrainMap.Size/2-32; x < TerrainMap.Size/2+33 && elevated is null; x++)
        for (int z = TerrainMap.Size/2-32; z < TerrainMap.Size/2+33 && elevated is null; z++)
        {
            var c = world.Surface(x, z);
            if (c.Level == world.Camp.Level || !world.CanPlace(c)) continue;
            var stand = world.Neighbors(c).FirstOrDefault(n => n.Level == c.Level && world.Walkable(n) && !world.Terrain[n.X, n.Z].Ramp);
            if (!world.Terrain.Contains(stand)) continue;
            var route = world.FindPath(world.Workers[0].Tile, stand);
            if (route is null) continue;
            var previous = world.Workers[0].Tile;
            foreach (var step in route)
            {
                Check(world.Connected(previous, step), "Every path step obeys world traversal rules");
                int dx = step.X - previous.X, dz = step.Z - previous.Z;
                if(!world.ClimbEdge(previous,step))Check(Math.Abs(world.NodeHeight(previous,dx*.5,dz*.5)-world.NodeHeight(step,-dx*.5,-dz*.5))<.001f,"Walking and stair edges stay level; ledges use climbing motion");
                previous = step;
            }
            elevated = c;
        }
        Check(elevated is not null, "Camp must have reachable elevated construction terrain");
        Check(world.QueueBuild(elevated!.Value, BuildKind.Wall), "Elevated wall planned"); Advance(world, 120);
        Check(world.Buildings.ContainsKey(elevated.Value), "Residents traverse terrain to construct above camp level");
        Console.WriteLine($"TERRAIN_OK: {DemoWorld.Width}x{DemoWorld.Depth}, {heights.Count} levels, {ramps} ramps, {cliffs} cliff edges; checks {watch.ElapsedMilliseconds} ms");
    }

}
