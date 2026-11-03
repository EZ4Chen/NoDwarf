namespace NoDwarf.Simulation;
public static class ClimbingChecks
{
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("CLIMB: "+message);}
    public static void Run()
    {
        Check(WorldScale.DwarfHeightMeters==1.4f,"resident scale");
        var w=new DemoWorld(flat:true){FallBreakChance=0};w.DeveloperResidents(false);
        var low=new Cell(60,60);var ledge=new Cell(61,60);var high=ledge+new Cell(0,0,1);
        w.DeveloperPlace(ledge,BuildKind.Wall);
        Check(w.Connected(low,high)&&w.Connected(high,low),"one-metre up/down");
        Check(w.FindPath(low,high)?.Count==1,"navigation includes climbing link");
        var person=new DemoWorker{Name="climber",X=60.45,Z=60,Level=0,TravelFrom=low};person.Path.Enqueue(high);
        Check(Math.Abs(w.WorkerHeight(person)-1)<.001,"feet clear wall before crossing shared edge");
        person.X=60.2;Check(w.WorkerHeight(person)>0&&w.WorkerHeight(person)<1,"continuous ascent");
        person.Path.Clear();person.Path.Enqueue(low);person.TravelFrom=high;person.Level=1;person.X=60.55;
        Check(Math.Abs(w.WorkerHeight(person)-1)<.001,"downward step clears edge before dropping");
        person.X=60.2;Check(w.WorkerHeight(person)>0&&w.WorkerHeight(person)<1,"continuous descent");
        var ceiling=low+new Cell(0,0,2);w.Buildings[ceiling]=new(ceiling,BuildKind.Planks);
        Check(w.Walkable(low)&&!w.Connected(low,high)&&!w.Connected(high,low),"standing clearance is not enough for climbing under ceiling");
        w.Buildings.Remove(ceiling);
        w.Workers.Add(person);Check(!w.CanDeveloperPlace(ceiling,BuildKind.Planks),"cannot build into swept climbing headroom");w.Workers.Clear();
        Check(w.Connected(low,high),"removing ceiling restores traversal");
        w.Buildings[high]=new(high,BuildKind.Wall);
        Check(!w.Connected(low,high+new Cell(0,0,1))&&w.Connected(high+new Cell(0,0,1),low),"two-metre drop allowed, two-metre climb rejected");
        w.Buildings.Remove(high);
        var end=new Cell(62,60);var route=w.FindPath(low,end);
        Check(route is not null&&!route.Contains(high),"short flat detour is cheaper than up/down climbing");
        var nearest=w.Navigation.FindNearest(low,new[]{end},out _);
        Check(nearest is not null&&!nearest.Contains(high),"work routing also prices climbing");
        Console.WriteLine("CLIMB_OK: 1.4m body, 1m up/down, smooth ledge clearance, overhead obstruction, 2m rejection and weighted routing");
    }
}
