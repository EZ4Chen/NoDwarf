using NoDwarf.Simulation;

internal static class OptimizationChecks
{
    private static readonly Cell[] Directions = { new(1,0),new(-1,0),new(0,1),new(0,-1) };
    private static (bool Room, bool Ready) LegacyRooms(DemoWorld w)
    {
        var Buildings = w.Buildings;
        bool HasRoom = false;
        Cell? _forgeFurnace = null, _forgeAnvil = null;
        bool Inside(Cell c) => DemoWorld.Inside(c);
        bool NaturalSolid(Cell c) => w.NaturalSolid(c);
        bool NaturalRamp(Cell c) => w.NaturalRamp(c);
        bool Solid(Cell c) => w.Solid(c);
        bool Walkable(Cell c) => w.Walkable(c);
        bool Boundary(Cell c) => NaturalSolid(c) || Buildings.TryGetValue(c,out var b) && (BlockCatalog.IsWall(b.Kind)||b.Kind==BuildKind.Door);
        const int Width = DemoWorld.Width, Depth = DemoWorld.Depth;
        // Room boundaries are walls and doors. Equipment stays part of the room area.
        var visited = new HashSet<Cell>();
        foreach (var start in Buildings.Keys.SelectMany(c => Directions.Select(d => c + d)).Distinct())
        {
            if (visited.Contains(start) || NaturalSolid(start+new Cell(0,0,1)) || Boundary(start) || !Solid(start + new Cell(0, 0, -1)) || NaturalSolid(start)) continue;
            var region = new HashSet<Cell>();
            var queue = new Queue<Cell>(); queue.Enqueue(start); visited.Add(start);
            bool outside = false, door = false;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue(); region.Add(c);
                if (c.X == 0 || c.X == Width - 1 || c.Z == 0 || c.Z == Depth - 1) outside = true;
                foreach (var d in Directions)
                {
                    var next = c + d;
                    if (!Inside(next)) continue;
                    if (!Solid(next + new Cell(0, 0, -1)) || NaturalRamp(next)) { outside = true; continue; }
                    if (Boundary(next))
                    { if (Buildings.TryGetValue(next, out var boundary) && boundary.Kind == BuildKind.Door) door = true; continue; }
                    if(NaturalSolid(next+new Cell(0,0,1)))continue; // A low sealed recess is not an opening to outdoors.
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
            if (outside || !door || region.Count < 4) continue;
            HasRoom = true;
            var furnace = Buildings.Values.FirstOrDefault(b => b.Kind == BuildKind.Furnace && region.Contains(b.Cell));
            var anvil = Buildings.Values.FirstOrDefault(b => b.Kind == BuildKind.Anvil && region.Contains(b.Cell));
            if (furnace is null || anvil is null) continue;
            if (!Directions.Any(d => region.Contains(anvil.Cell + d) && Walkable(anvil.Cell + d))) continue;
            if (!Directions.Any(d => region.Contains(furnace.Cell + d) && Walkable(furnace.Cell + d))) continue;
            _forgeAnvil = anvil.Cell; _forgeFurnace = furnace.Cell;
        }
        return (HasRoom, HasRoom && _forgeAnvil is not null);
    }
    public static void Run()
    {
        void Compare(DemoWorld world, string label)
        {
            world.ValidateWorkshop(); var expected = LegacyRooms(world);
            if (world.HasRoom != expected.Room || world.WorkshopReady != expected.Ready)
                throw new InvalidOperationException("Room cache differs from original: " + label);
            world.ValidateWorkshop();
            if (world.HasRoom != expected.Room || world.WorkshopReady != expected.Ready)
                throw new InvalidOperationException("Repeated room query differs: " + label);
        }
        foreach (int floor in new[] { 0, -3, 3 })
        {
            var w = new DemoWorld(flat:true);
            if (floor < 0) for(int x=39;x<=47;x++)for(int z=39;z<=47;z++)for(int y=floor;y<=floor+1;y++)w.Terrain.Excavate(new Cell(x,z,y));
            if (floor > 0) for(int x=39;x<=47;x++)for(int z=39;z<=47;z++){var c=new Cell(x,z,floor-1);w.Buildings[c]=new(c,BuildKind.Planks);}
            var wall = new Cell(40,43,floor);var door = new Cell(43,46,floor);
            for(int i=40;i<=46;i++)foreach(var c in new[]{new Cell(i,40,floor),new Cell(i,46,floor),new Cell(40,i,floor),new Cell(46,i,floor)})w.Buildings[c]=new(c,BuildKind.Wall);
            w.Buildings[door]=new(door,BuildKind.Door);
            var f=new Cell(41,41,floor);var a=new Cell(45,41,floor);
            w.Buildings[f]=new(f,BuildKind.Furnace);w.Buildings[a]=new(a,BuildKind.Anvil);
            Compare(w,"closed workshop");
            var probe = new PerformanceProbe(); w.Probe = probe;
            var remote = new Cell(200,200,floor); w.Buildings[remote] = new(remote,BuildKind.Wall);
            Compare(w,"remote edit preserves cached rooms");
            using (var report = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(probe.Report())))
                if (report.RootElement.GetProperty("counters").TryGetProperty("rooms.visited-cells", out var visited) && visited.GetInt64() != 0)
                    throw new InvalidOperationException("Remote edit unnecessarily rescanned room regions");
            w.Buildings.Remove(wall);Compare(w,"opened wall");
            w.Buildings[wall]=new(wall,BuildKind.Wall);Compare(w,"wall restored");
            w.Buildings.Remove(door);Compare(w,"door removed");
            w.Buildings[door]=new(door,BuildKind.Door);Compare(w,"door restored");
            var support = new Cell(42,42,floor-1);
            if(floor<=0)w.Terrain.Excavate(support);else w.Buildings.Remove(support);
            Compare(w,"floor hole");
            w.Buildings[support]=new(support,BuildKind.Planks);Compare(w,"floor repaired");
            var rng = new Random(829+floor);
            for(int i=0;i<35;i++)
            {
                var c=new Cell(rng.Next(39,48),rng.Next(39,48),floor+rng.Next(-1,2));
                if(rng.Next(2)==0)w.Buildings.Remove(c);else w.Buildings[c]=new(c,rng.Next(4)==0?BuildKind.Door:BuildKind.Wall);
                Compare(w,"random edit "+i);
            }
            w.Buildings.Clear();Compare(w,"clear buildings");
        }
        Console.WriteLine("OPTIMIZATION_CHECKS_OK: room cache matches original across doors, breaches, floors, headroom and edits");
    }
}
