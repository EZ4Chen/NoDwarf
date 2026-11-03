namespace NoDwarf.Simulation;
public static class FallSafetyChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("FALL_SAFETY: "+message);}
    private static DemoWorld World(){var w=new DemoWorld(flat:true){FallBreakChance=0};w.Workers.Clear();w.Items.Clear();return w;}
    private static void Settle(DemoWorld w){for(int i=0;i<100&&w.SupportPending;i++)w.AdvanceSupport();w.AdvanceSupport();}
    public static void Run()
    {
        var w=World();var low=new Cell(60,60);var high=new Cell(61,60,2);
        for(int y=0;y<2;y++)w.Buildings[new(61,60,y)]=new(new(61,60,y),BuildKind.Wall);
        Check(w.Connected(high,low)&&!w.Connected(low,high),"directed two-metre drop");
        Check(w.FindPath(high,low)?.Count==1&&w.FindPath(low,high) is null,"exact search respects one-way island");
        Check(w.Navigation.CanReach(high,low)&&!w.Navigation.CanReach(low,high),"cached reachability is directional");
        Check(w.Navigation.FindNearest(low,new[]{high},out _) is null,"work search cannot climb two metres");
        var ceiling=low+new Cell(0,0,3);w.Buildings[ceiling]=new(ceiling,BuildKind.Planks);
        Check(!w.Connected(high,low),"drop needs swept headroom");w.Buildings.Remove(ceiling);
        var dwarf=new DemoWorker{Id="fall-test",Name="测试",X=61,Z=60,Level=2};w.Workers.Add(dwarf);
        var unsafeJob=new DemoJob{Id=1,Cell=low+new Cell(0,0,-1),Dig=true,Worker=0};w.Jobs.Add(unsafeJob);dwarf.Job=unsafeJob;dwarf.Path.Enqueue(low);
        w.Update(1d/30);Check(dwarf.Tile==high&&dwarf.Path.Count==0&&dwarf.Job is null,"autonomous miner refuses a one-way pit without return route");
        w.Jobs.Clear();dwarf.Path.Enqueue(low);
        for(int i=0;i<70;i++)w.Update(1d/30);
        Check(dwarf.Tile==low&&dwarf.Person.Injuries.Count==0,"safe two-metre descent actually completes without injury");
        for(int distance=2;distance<=5;distance+=3)
        {
            w=World();dwarf=new(){Id="fall-test",Name="测试",X=60,Z=60,Level=distance};w.Workers.Add(dwarf);w.AdvanceSupport();
            Check(dwarf.Level==0,"unsupported resident settles");
            Check((dwarf.Person.Injuries.Count>0)==(distance>2),"injury threshold");
            double damage=dwarf.Person.Injuries.Sum(i=>i.Severity);w.AdvanceSupport();Check(dwarf.Person.Injuries.Sum(i=>i.Severity)==damage,"landing applies damage once");
            var loaded=DemoWorld.LoadJson(w.SaveJson());loaded.AdvanceSupport();Check(loaded.Workers[0].Person.Injuries.Sum(i=>i.Severity)==damage,"save does not repeat injury");
        }
        w=World();for(int y=0;y<4;y++)w.Buildings[new(61,60,y)]=new(new(61,60,y),BuildKind.Wall);
        high=new(61,60,4);Check(!w.Connected(high,low)&&w.FindPath(high,low) is null,"no suicidal four-metre route");
        dwarf=new(){Id="fall-test",Name="测试",X=62,Z=60,Level=4};w.Workers.Add(dwarf);
        var platform=new Cell(62,60,3);w.Buildings[platform]=new(platform,BuildKind.Planks);Settle(w);
        Check(!w.DeveloperErase(new(61,60,0)),"cannot remove last pillar with resident on connected platform");
        Check(!w.DeveloperDig(new(61,60,-1)),"cannot excavate supporting foundation under occupied platform");
        // An external destruction bypasses work safety and must cause an injured landing.
        w.Buildings.Remove(new(61,60,0));w.Buildings.Remove(new(61,60,1));w.Buildings.Remove(new(61,60,2));Settle(w);
        Check(dwarf.Level==1&&dwarf.Person.Injuries.Any(),"collapsing platform causes fall injury");
        w=World();dwarf=new(){Id="fall-test",Name="测试",X=60,Z=60,Level=0};w.Workers.Add(dwarf);
        Check(!w.DeveloperDig(new(60,60,-1)),"cannot dig own feet");
        dwarf.Path.Enqueue(new(61,60));Check(!w.DeveloperDig(new(61,60,-1)),"cannot dig next landing support");
        w=World();for(int i=0;i<100;i++)w.Workers.Add(new(){Id="idle"+i,Name="idle",X=80+i%10,Z=80+i/10,Level=0});
        w.AdvanceSupport();long visits=w.SupportVisits;var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<1000;i++)w.AdvanceSupport();
        Check(w.SupportVisits==visits,"idle residents do not trigger support graph scans");
        Console.WriteLine($"FALL_IDLE_PERF: 100 residents, {timer.Elapsed.TotalMilliseconds/1000:F3} ms per support update; zero graph visits");
        Console.WriteLine("FALL_SAFETY_OK: directed 2m navigation, 3m+ rejection, headroom, actual jump, injury/save, occupied platform and excavation protection");
    }
}
