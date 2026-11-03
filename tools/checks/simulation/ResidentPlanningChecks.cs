using System.Text.Json;
using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;

public static class ResidentPlanningChecks
{
    private static void Check(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)(seconds*SimulationClock.BaseTicksPerSecond);i++)w.Update(SimulationClock.TickSeconds);}
    private static DemoWorld Fresh()
    {
        var w=new DemoWorld(flat:true);w.Workers.RemoveRange(1,w.Workers.Count-1);w.Stockpiles.Clear();w.Items.RemoveAll(i=>i.Owner!=w.Workers[0].Id);return w;
    }
    private static void Priority(DemoWorld w,string type,int value)=>Check(w.ActResident(new(w.Workers[0].Id,"work_priority",type,Priority:value)).Ok,"Priority update");
    private static void Day(DemoWorld w,string mode)=>Check(w.ActResident(new(w.Workers[0].Id,"schedule",Hours:Enumerable.Repeat(mode,24).ToArray())).Ok,"Schedule update");
    public static void Run()
    {
        Priorities();Schedules();ResearchAndCare();ArchiveAndAi();
        Console.WriteLine("RESIDENT_PLANNING_OK: work ranks/disabled types, 24h transitions, emergency needs, cargo conservation, automatic research/care, save roundtrip and AI policies");
    }
    private static void Priorities()
    {
        var w=Fresh();var a=w.Workers[0];w.SpawnItem("cobblestone",20,a.Tile);
        Check(w.QueueBuild(new(5,10),BuildKind.Wall)&&w.QueueDig(new(14,10,-1)),"Competing work queued");
        Priority(w,"construction",4);Priority(w,"mining",1);w.Update(.1);
        Check(a.Job?.Dig==true,"Higher-priority distant work wins over nearby low-priority work");
        int stone=w.Total("cobblestone");Priority(w,"mining",0);w.Update(.1);
        Check(a.Job?.Build==BuildKind.Wall&&w.Total("cobblestone")==stone,"Disabling current work releases it without losing carried materials");
        Advance(w,20);Check(w.Buildings.ContainsKey(new(5,10))&&w.Jobs.Any(j=>j.Dig&&j.Worker<0),"Disabled work remains unassigned");
        Priority(w,"mining",3);Check(ResidentWork.Rank(a.Person,"construction")>ResidentWork.Rank(a.Person,"mining"),"Numeric priority wins over column order");
        Priority(w,"construction",3);Check(ResidentWork.Rank(a.Person,"construction")<ResidentWork.Rank(a.Person,"mining"),"Equal priorities use left-to-right work order");
        Check(!w.ActResident(new(a.Id,"work_priority","butchery",Priority:1)).Ok&&a.Person.WorkPriorities["butchery"]==0,"Unsupported butchery is not advertised as working");
        var haul=Fresh();haul.SpawnItem("wood",10,new(3,10));haul.SetStockpile(new(8,10),new(8,10),false);Advance(haul,.4);
        Priority(haul,"hauling",0);Advance(haul,1);
        Check(!haul.Jobs.Any(j=>j.Haul)&&haul.Available("wood")==10,"Disabling all haulers releases idle hauling reservations");
    }
    private static void Schedules()
    {
        var w=Fresh();var a=w.Workers[0];a.Person.Rest=30;w.SpawnItem("cobblestone",20,a.Tile);w.QueueBuild(new(10,10),BuildKind.Wall);w.Update(.1);
        Check(a.Job is not null,"Ordinary work assigned");int stone=w.Total("cobblestone");
        Day(w,"sleep");w.Update(.1);
        Check(a.Job is null&&a.Person.Activity is {Kind:"rest",Origin:"schedule"}&&w.Total("cobblestone")==stone,"Sleep block suspends work and preserves cargo");
        var snapshot=JsonNode.Parse(w.SaveJson())!;snapshot["SimulationTime"]=24.9;
        // Starts at 08:00; 25 simulation seconds later the game reaches 09:00.
        snapshot["Workers"]![0]!["Person"]!["Schedule"]![9]="work";
        var boundary=DemoWorld.LoadJson(snapshot.ToJsonString());boundary.Update(.2);
        Check(boundary.Calendar.Hour==9&&boundary.Workers[0].Person.Activity?.Kind!="rest"&&boundary.Workers[0].Job is not null,"Hour transition wakes scheduled sleeper for work");
        Day(w,"recreation");a.Person.Recreation=40;w.Update(.1);
        Check(a.Person.Activity is {Kind:"relax",Origin:"schedule"}&&a.Job is null,"Recreation block actually relaxes");
        a.Person.Hunger=10;Advance(w,4);
        Check(a.Person.Hunger>40,"Essential eating overrides scheduled recreation");
        var work=Fresh();Day(work,"work");var b=work.Workers[0];b.Person.Rest=10;b.Person.Recreation=5;Advance(work,2.1);
        Check(b.Person.Activity is null,"Work hours suppress optional recreation and ordinary sleep");
        b.Person.Rest=3;Advance(work,2.1);Check(b.Person.Activity is {Kind:"rest",Origin:"need"},"Extreme fatigue remains protected");
        var free=Fresh();free.Workers[0].Person.Recreation=5;Advance(free,2.1);
        Check(free.Workers[0].Person.Activity?.Kind=="relax","Anything follows local needs");
        var manual=Fresh();Day(manual,"sleep");manual.ActResident(new(manual.Workers[0].Id,"research_topic","foundations"));manual.ActResident(new(manual.Workers[0].Id,"research"));manual.Update(.1);
        Check(manual.Workers[0].Person.Activity is {Kind:"research",Origin:"manual"},"Explicit orders keep their documented override");
    }
    private static void ResearchAndCare()
    {
        var w=Fresh();var a=w.Workers[0];w.ActResident(new(a.Id,"research_topic","foundations"));
        Priority(w,"research",1);w.Update(.1);Check(a.Person.Activity is {Kind:"research",Origin:"work"},"Research participates in ordinary work selection");
        w.SpawnItem("cobblestone",20,a.Tile);w.QueueBuild(new(6,10),BuildKind.Wall);Priority(w,"construction",1);w.Update(.1);
        Check(a.Job?.Build==BuildKind.Wall&&a.Person.PausedResearch is not null,"Higher-ranked construction interrupts automatic research");
        Advance(w,15);Check(w.Buildings.ContainsKey(new(6,10))&&a.Person.Activity?.Kind=="research","Research resumes when higher-ranked work is done");
        Priority(w,"research",0);w.Update(.1);Check(a.Person.Activity is null&&a.Person.PausedResearch is null,"Disabling research stops automatic resumption");
        Priority(w,"doctor",1);w.InjureResident(a.Id,"left_hand","cut",15);int bandages=w.Total("bandage");Advance(w,15);
        Check(a.Person.Injuries.All(i=>i.Treated)&&w.Total("bandage")==bandages-1,"Medical work automatically consumes real bandage and treats injury");
    }
    private static void ArchiveAndAi()
    {
        var w=Fresh();var a=w.Workers[0];var day=ResidentWork.DayShift();
        w.ActResident(new(a.Id,"schedule",Hours:day));day[8]="sleep";
        Check(a.Person.Schedule[8]=="work","Caller cannot mutate stored schedule through array alias");
        Priority(w,"research",2);var loaded=DemoWorld.LoadJson(w.SaveJson());
        Check(loaded.Workers[0].Person.Schedule.SequenceEqual(a.Person.Schedule)&&loaded.Workers[0].Person.WorkPriorities["research"]==2,"Policies survive save and load");
        foreach(bool schedule in new[]{true,false})
        {
            var data=JsonNode.Parse(w.SaveJson())!;if(schedule)data["Workers"]![0]!["Person"]!["Schedule"]![0]="invalid";else data["Workers"]![0]!["Person"]!["WorkPriorities"]!["hauling"]=9;
            bool bad=false;try{DemoWorld.LoadJson(data.ToJsonString());}catch(InvalidDataException){bad=true;}Check(bad,"Invalid saved policies rejected");
        }
        string command=JsonSerializer.Serialize(new{resident=a.Id,action="schedule",target="recreation",hour=12,expectedVersion=a.Person.DecisionVersion,expectedResearch=w.Research.Version});
        Check(w.ActResidentJson(command).Ok&&!w.ActResidentJson(command).Ok&&a.Person.Schedule[12]=="recreation","AI can edit schedule with replay protection");
        command=JsonSerializer.Serialize(new{resident=a.Id,action="work_priority",target="hauling",priority=1,expectedVersion=a.Person.DecisionVersion,expectedResearch=w.Research.Version});
        Check(w.ActResidentJson(command).Ok&&a.Person.WorkPriorities["hauling"]==1,"AI shares the work priority entry");
        string saved=w.SaveJson();
        Check(!w.ActResident(new(a.Id,"schedule","sleep",Hour:24)).Ok&&!w.ActResident(new(a.Id,"schedule",Hours:new[]{"work"})).Ok&&
            !w.ActResident(new(a.Id,"work_priority","hauling",Priority:5)).Ok&&w.SaveJson()==saved,"Invalid policies are atomic");
        var observed=JsonNode.Parse(w.ObserveResident(a.Id))!;
        Check(observed["schedule"]!.AsArray().Count==24&&observed["workPriorities"]!["hauling"]!.GetValue<int>()==1,"AI observes both policy tables");
    }
}
