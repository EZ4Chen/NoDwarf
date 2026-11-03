namespace NoDwarf.Simulation;
public static class LadderChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("LADDER: "+message);}
    public static DemoWorld Fixture()
    {
        var w=new DemoWorld(flat:true){FallBreakChance=0};w.Workers.Clear();w.Items.Clear();w.Stockpiles.Clear();
        for(int y=0;y<4;y++){var wall=new Cell(61,60,y);w.Buildings[wall]=new(wall,BuildKind.StoneBricks);}
        return w;
    }
    public static void Run()
    {
        for(int facing=0;facing<4;facing++)
        {
            var rotated=new DemoWorld(flat:true);rotated.Workers.Clear();var position=new Cell(60,60);
            var back=position+DemoWorld.StairDirection(facing);rotated.Buildings[back]=new(back,BuildKind.Wall);
            Check(rotated.DeveloperPlace(position,BuildKind.Ladder,facing)&&rotated.Buildings[position].Facing==facing,"four mounting directions");
        }
        var w=Fixture();var bottom=new Cell(60,60);var top=new Cell(61,60,4);
        Check(!w.CanDeveloperPlace(bottom,BuildKind.Ladder,2)&&!w.CanPlace(bottom,BuildKind.Ladder,2),"backing wall direction required");
        for(int y=0;y<4;y++)Check(w.DeveloperPlace(bottom with{Level=y},BuildKind.Ladder),"wall mounted segment");
        Check(w.Walkable(bottom+new Cell(0,0,2)),"ladder supports mid-shaft body without floor");
        var route=w.FindPath(new(59,60),top);Check(route is not null&&route.Any(c=>c.X==60&&c.Level==3),"continuous ascent and top exit");
        Check(w.FindPath(top,new(59,60)) is not null,"return route");
        var worker=new DemoWorker{Id="ladder-test",Name="梯子测试",X=59,Z=60};w.Workers.Add(worker);
        foreach(var step in route!)worker.Path.Enqueue(step);
        int ticks=0;while(worker.ClimbProgress<=.2&&ticks++<300)w.Update(1d/30);
        Check(worker.ClimbProgress>0&&worker.ClimbProgress<1,"vertical motion is gradual");
        float height=w.WorkerHeight(worker);var loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(Math.Abs(loaded.WorkerHeight(loaded.Workers[0])-height)<.001,"mid-climb save preserves height");
        for(int i=0;i<600;i++)loaded.Update(1d/30);
        Check(loaded.Workers[0].Tile==top,"resident finishes climbing after reload");
        var descent=loaded.FindPath(top,new(59,60))!;foreach(var step in descent)loaded.Workers[0].Path.Enqueue(step);
        for(int i=0;i<600;i++)loaded.Update(1d/30);
        Check(loaded.Workers[0].Tile==new Cell(59,60)&&loaded.Workers[0].Person.Injuries.Count==0,"resident descends safely");
        w.Workers.Clear();w.DeveloperErase(bottom+new Cell(0,0,2));
        Check(w.FindPath(new(59,60),top) is null,"missing middle segment breaks ascent and invalidates graph");
        Check(!w.Connected(bottom+new Cell(0,0,2),bottom+new Cell(0,0,3)),"cannot vertically cross a gap");
        w.DeveloperPlace(bottom+new Cell(0,0,2),BuildKind.Ladder);Check(w.FindPath(new(59,60),top) is not null,"repair restores route");
        var roof=new Cell(60,60,5);w.Buildings[roof]=new(roof,BuildKind.Planks);Check(!w.Walkable(new(60,60,4)),"top exit needs body clearance");
        // Actual resident construction uses wood, including hanging segments.
        w=Fixture();w.Items.Clear();w.Stockpiles.Clear();w.SpawnItem("wood",10,new(59,60));
        worker=new(){Id="builder",Name="施工",X=59,Z=60};w.Workers.Add(worker);
        for(int y=0;y<4;y++)Check(w.QueueBuild(bottom with{Level=y},BuildKind.Ladder),"plan hanging ladder");
        for(int i=0;i<30*180&&w.Jobs.Count>0;i++)w.Update(1d/30);
        Check(w.Buildings.Values.Count(b=>b.Kind==BuildKind.Ladder)==4&&w.Total("wood")==6,"construction consumes one wood per segment");
        var shaft=new DemoWorld(flat:true){FallBreakChance=0};shaft.Workers.Clear();shaft.Items.Clear();shaft.Stockpiles.Clear();
        for(int y=-4;y<0;y++)shaft.Terrain.Excavate(new(60,60,y));
        shaft.SpawnItem("wood",4,new(59,60));shaft.Workers.Add(new(){Id="shaft-builder",Name="井口施工",X=59,Z=60});
        for(int y=-1;y>=-4;y--)Check(shaft.QueueBuild(new(60,60,y),BuildKind.Ladder),"shaft ladder plan");
        for(int i=0;i<30*180&&shaft.Jobs.Count>0;i++)shaft.Update(1d/30);
        var pending=string.Join(";",shaft.Jobs.Select(j=>$"{j.Cell}/{j.Worker}/"+string.Join(",",j.Materials.Select(r=>$"{r.Stage}:{r.Count}"))));
        Check(shaft.Buildings.Values.Count(b=>b.Kind==BuildKind.Ladder)==4,"shaft installation: "+pending+" residents="+string.Join(";",shaft.Workers.Select(w=>$"{w.Tile}/{w.Status}")));
        Check(shaft.FindPath(new(59,60),new(60,60,-4)) is not null&&shaft.FindPath(new(60,60,-4),new(59,60)) is not null,"shaft provides safe round trip");
        Console.WriteLine("LADDER_OK: backing, hanging construction, ascent/descent, top exit, headroom, broken/repair, smooth movement and mid-climb save");
    }
}
