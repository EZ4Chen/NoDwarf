using System.Text.Json;
using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;

public static class ResidentIntegrationChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)(seconds*30);i++)w.Update(1d/30);}
    private static DemoWorld Fresh()
    {var w=new DemoWorld(flat:true);w.Stockpiles.Clear();w.Items.RemoveAll(i=>i.Owner=="");return w;}
    public static void Run()
    {
        ResearchNeeds();CapacityAndJobs();AiBoundary();
        Console.WriteLine("RESIDENT_INTEGRATION_OK: research self-care/resume/save, incapable job release, bounded AI observation, JSON validation and replay rejection");
    }
    private static void ResearchNeeds()
    {
        var w=Fresh();var a=w.Workers[0];a.Person.Hunger=10;
        Check(w.ActResident(new(a.Id,"research_topic","foundations")).Ok&&w.ActResident(new(a.Id,"research")).Ok,"Research starts");
        int meals=w.Total("meal");Advance(w,2.1);
        Check(a.Person.Activity?.Kind=="eat"&&a.Person.PausedResearch?.Kind=="research","Hunger interrupts research locally");
        double progress=w.Research.Progress["foundations"];var restored=DemoWorld.LoadJson(w.SaveJson());Advance(restored,4);
        var r=restored.Workers[0];
        Check(r.Person.Hunger>40&&restored.Total("meal")==meals-1&&r.Person.Activity?.Kind=="research"&&r.Person.PausedResearch is null,"Saved food break resumes research once");
        Check(restored.Research.Progress["foundations"]>progress,"Research continues after needs are met");
        r.Person.Rest=10;Advance(restored,2.1);
        Check(r.Person.Activity?.Kind=="rest"&&r.Person.PausedResearch is not null,"Fatigue interrupts research");
        Check(restored.ActResident(new(r.Id,"cancel")).Ok&&r.Person.PausedResearch is null,"Player cancellation clears automatic resumption");
        r.Person.Rest=90;Advance(restored,4);Check(r.Person.Activity is null,"Cancelled research does not restart");
        var invalid=JsonNode.Parse(w.SaveJson())!;invalid["Workers"]![0]!["Person"]!["PausedResearch"]!["Kind"]="pickup";
        bool rejected=false;try{DemoWorld.LoadJson(invalid.ToJsonString());}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Saved suspended activity is restricted to research");
    }
    private static void CapacityAndJobs()
    {
        var w=Fresh();w.SpawnItem("cobblestone",20,new Cell(3,10));Check(w.QueueBuild(new Cell(10,10),BuildKind.Wall),"Build queued");
        Advance(w,.3);var worker=w.Workers.First(w=>w.Job is not null);var job=worker.Job!;
        long xp=worker.Person.GrowthXp;
        w.InjureResident(worker.Id,"left_hand","missing",100);w.InjureResident(worker.Id,"right_hand","missing",100);
        Advance(w,.1);Check(worker.Job is null&&job.Worker!=w.Workers.IndexOf(worker),"Incapable worker releases ordinary job");
        Check(!w.ActResident(new(worker.Id,"research_topic","foundations")).Ok||!w.ActResident(new(worker.Id,"research")).Ok,"Research requiring hands is rejected");
        Advance(w,35);Check(w.Buildings.ContainsKey(new Cell(10,10))&&worker.Person.GrowthXp==xp,"Another worker completes the released work");
        Check(worker.Person.WorkRate("social")>0,"Hand injury does not disable conversation");
        // Loss of mobility must not prevent eating food already carried.
        w.InjureResident(worker.Id,"left_leg","missing",100);w.InjureResident(worker.Id,"right_leg","missing",100);
        worker.Person.Hunger=10;Advance(w,5);Check(worker.Person.Hunger>40,"Immobile conscious resident can eat carried food");
        Check(ResidentCatalog.Skill("growing").HasWork,"Farming is exposed as implemented");
        var tools=Fresh();var miner=tools.Workers[0];var pick=tools.SpawnItem("pickaxe",1,miner.Tile);
        tools.ActResident(new(miner.Id,"pickup",ItemId:pick.Id));Advance(tools,2);
        tools.ActResident(new(miner.Id,"equip",ItemId:tools.Inventory(miner).Single(i=>i.Item=="pickaxe").Id));
        Check(miner.Person.GearBonuses.GetValueOrDefault("mining")>0,"Equipped pickaxe improves mining");
        tools.InjureResident(miner.Id,"left_hand","missing",100);
        Check(miner.Person.GearBonuses.GetValueOrDefault("mining")==0,"Losing a hand immediately disables the two-handed bonus");
    }
    private static void AiBoundary()
    {
        var w=Fresh();var a=w.Workers[0];a.Person.Earn("mining",1500);
        string saved=w.SaveJson();string observed=w.ObserveResident(a.Id);
        Check(w.SaveJson()==saved,"Observation is read-only");
        var state=JsonNode.Parse(observed)!;
        Check(state["contractVersion"]!.GetValue<int>()==1&&state["inventory"]!["items"]!.AsArray().Count<=12&&state["nearby"]!.AsArray().Count<=8,"Observation contract and bounded lists");
        Check(state["skills"]!.AsArray().Count==11&&!state.AsObject().ContainsKey("memories"),"Only compact character context is exported");
        foreach(string bad in new[]{"null","[]","{}","{","{\"resident\":null,\"action\":\"rest\"}","{\"resident\":\"azhen\",\"action\":\"rest\",\"ai\":false}","{\"action\":\"rest\",\"action\":\"eat\"}",new string('x',4097)})
            Check(!w.ActResidentJson(bad).Ok,"Malformed model output rejected");
        Check(w.SaveJson()==saved,"Rejected JSON cannot change the world");
        string Command(string action,string target="")=>JsonSerializer.Serialize(new{resident=a.Id,action,target,expectedVersion=a.Person.DecisionVersion,expectedResearch=w.Research.Version});
        Check(!w.ActResidentJson(Command("grant_xp","mining")).Ok,"Model cannot call developer cheats");
        string learn=Command("talent","mining.1");Check(w.ActResidentJson(learn).Ok&&!w.ActResidentJson(learn).Ok,"Actual JSON purchase and replay rejection");
        long version=a.Person.DecisionVersion;a.Person.Earn("mining",1);
        Check(a.Person.DecisionVersion==version,"Tiny XP ticks do not invalidate eligible decisions");
        string stale=Command("rest");Check(w.ActResident(new(a.Id,"route","mining")).Ok&&!w.ActResidentJson(stale).Ok,"A player decision invalidates pending AI commands");
        string researchStale=Command("relax");w.ActResident(new(a.Id,"research_topic","foundations"));
        Check(!w.ActResidentJson(researchStale).Ok,"Changed research invalidates pending model commands");
        Check(!w.ActResident(null!).Ok&&!w.ActResident(new(a.Id,"route",null!)).Ok,"Typed boundary rejects nulls");
        Check(!JsonNode.Parse(w.ObserveResident("unknown"))!["ok"]!.GetValue<bool>(),"Unknown observation is explicit");
    }
}
