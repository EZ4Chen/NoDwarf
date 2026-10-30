using System.Text.Json;
using System.Text.Json.Nodes;
namespace NoDwarf.Simulation;

public static class ResidentChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Advance(DemoWorld world,double seconds){for(int i=0;i<(int)(seconds*30);i++)world.Update(1d/30);}
    private static DemoWorld Fresh()
    {var w=new DemoWorld(flat:true);w.Stockpiles.Clear();w.Items.RemoveAll(i=>i.Owner=="");return w;}
    public static void Run()
    {
        Progression();InventoryAndCare();ResearchAndArchive();WorkAndSocial();ResidentIntegrationChecks.Run();ResidentPlanningChecks.Run();
        Console.WriteLine("RESIDENT_OK: independent XP/points, prerequisites, physical gear/cargo, body injury/care, needs, research, social memory and AI validation");
    }
    private static void Progression()
    {
        var w=Fresh();var a=w.Workers[0];a.Person=new();
        a.Person.Earn("mining",999);Check(a.Person.Points==0&&a.Person.Level("mining")>0,"Skill can level without a point");
        a.Person.Earn("mining",1);Check(a.Person.Points==1,"Exactly 1000 total XP gives a point");
        Check(!w.ActResident(new(a.Id,"talent","smithing.1")).Ok,"Points alone cannot buy an untrained skill");
        long xp=a.Person.SkillXp["mining"],total=a.Person.GrowthXp;
        Check(w.ActResident(new(a.Id,"talent","mining.1")).Ok,"Valid talent purchased");
        Check(a.Person.Points==0&&a.Person.SkillXp["mining"]==xp&&a.Person.GrowthXp==total,"Purchase consumes points only");
        Check(!w.ActResident(new(a.Id,"talent","mining.1")).Ok,"Repeat purchase rejected");
        a.Person.Earn("mining",5000);
        Check(!w.ActResident(new(a.Id,"talent","mining.2")).Ok,"Research prerequisite enforced");
        w.Research.Completed.UnionWith(new[]{"foundations","industry"});
        long version=a.Person.DecisionVersion;
        var command=new ResidentCommand(a.Id,"talent","mining.2",ExpectedVersion:version,ExpectedResearch:w.Research.Version);
        Check(w.ActResident(command,true).Ok&&!w.ActResident(command,true).Ok,"AI purchase and replay rejection");
        Check(!w.ActResident(new(a.Id,"talent","mining.3"),true).Ok&&!w.ActResident(new(a.Id,"grant_xp","mining"),true).Ok,"AI cannot omit version or grant XP");
        var before=a.Person.Points;Check(!w.ActResident(new(a.Id,"talent","made_up"),true).Ok&&a.Person.Points==before,"Invalid AI output is side-effect free");
        a.Person.Earn("construction",1500);var batch=JsonNode.Parse(w.ResidentDecisionBatch())!.AsArray();Check(batch.Count<=10&&batch.ToJsonString().Contains("construction.1"),"Compact eligible decision batch");
        Check(w.ActResident(new(a.Id,"route","construction")).Ok&&a.Person.Talents["construction"]==1,"Fixed route spends eligible points locally");
    }
    private static void InventoryAndCare()
    {
        var w=Fresh();var a=w.Workers[0];var b=w.Workers[1];
        int swordId=w.SpawnItem("sword",2,a.Tile).Id;
        Check(w.ActResident(new(a.Id,"pickup",ItemId:swordId)).Ok,"Pickup queued");Advance(w,2);
        var sword=w.Inventory(a).Single(i=>i.Item=="sword");
        Check(w.Total("sword")==2&&w.Available("sword")==1&&sword.Count==1,"Pickup splits real stack without duplication");
        Check(w.ActResident(new(a.Id,"equip",ItemId:sword.Id)).Ok,"Equip real inventory sword");
        int pick=w.SpawnItem("pickaxe",1,a.Tile).Id;w.ActResident(new(a.Id,"pickup",ItemId:pick));Advance(w,2);
        var axe=w.Inventory(a).Single(i=>i.Item=="pickaxe");w.ActResident(new(a.Id,"equip",ItemId:axe.Id));
        Check(sword.Slot==""&&axe.Slot=="main"&&a.Person.GearBonuses["mining"]==.15,"Main-hand replacement keeps old item and refreshes cached efficiency");
        int shield=w.SpawnItem("shield",1,a.Tile).Id;w.ActResident(new(a.Id,"pickup",ItemId:shield));Advance(w,2);
        var sh=w.Inventory(a).Single(i=>i.Item=="shield");w.ActResident(new(a.Id,"equip",ItemId:sh.Id));
        Check(axe.Slot==""&&sh.Slot=="off","Offhand displaces two-handed tool");
        var saved=DemoWorld.LoadJson(w.SaveJson());var loaded=saved.Workers[0];
        Check(saved.Inventory(loaded).Count(i=>i.Slot=="off")==1&&saved.Total("sword")==2,"Equipped identity survives reload");
        Check(w.ActResident(new(a.Id,"drop",ItemId:sword.Id)).Ok&&w.Available("sword")==2&&w.Total("sword")==2,"Drop preserves count");
        long skill=a.Person.SkillXp["smithing"];
        Check(w.InjureResident(a.Id,"left_hand","fracture",60).Ok,"Body part injury");
        Check(a.Person.Manipulation<1&&a.Person.SkillXp["smithing"]==skill,"Injury reduces capacity, not knowledge");
        int bandages=w.Total("bandage");Check(w.ActResident(new(b.Id,"treat",a.Id)).Ok,"Care has real route and supplies");Advance(w,40);
        Check(a.Person.Injuries.All(i=>i.Treated)&&w.Total("bandage")==bandages-1&&b.Person.SkillXp["medicine"]>0,"Treatment consumes one bandage and earns medical XP");
        Check(a.Person.Relations.Any(r=>r.Target==b.Id&&r.Opinion>0),"Care changes directed relationship");
        double severity=a.Person.Injuries.Sum(i=>i.Severity);Advance(w,40);Check(a.Person.Injuries.Sum(i=>i.Severity)<severity,"Treated injury recovers in simulation");
        w.InjureResident(a.Id,"right_leg","missing",100);Check(a.Person.Mobility<1,"Missing limb affects movement");
        a.Person.Hunger=10;int meals=w.Total("meal");Advance(w,5);Check(a.Person.Hunger>40&&w.Total("meal")==meals-1,"Automatic eating consumes real food once");
        a.Person.Rest=10;Advance(w,20);Check(a.Person.Rest>20&&a.Job is null,"Fatigue triggers deterministic rest");
        Check(a.Person.Memories.Count<=24,"Bounded memories");
    }
    private static void ResearchAndArchive()
    {
        var w=Fresh();var a=w.Workers[1];
        Check(!w.ActResident(new(a.Id,"research_topic","precision")).Ok,"Research cannot skip parents");
        Check(w.ActResident(new(a.Id,"research_topic","foundations")).Ok&&w.ActResident(new(a.Id,"research")).Ok,"Research assigned");
        Advance(w,15);Check(w.Research.Progress["foundations"]>0&&a.Person.ResearchWork>0,"Research and XP grow from actual work");
        string saved=w.SaveJson();var loaded=DemoWorld.LoadJson(saved);Advance(loaded,100);
        Check(loaded.Research.Completed.Contains("foundations")&&loaded.Workers[1].Person.Activity is null,"In-progress research resumes after reload");
        long total=loaded.Workers.Sum(p=>p.Person.GrowthXp);Advance(loaded,10);Check(loaded.Workers.Sum(p=>p.Person.GrowthXp)==total,"Finished research cannot award repeatedly");
        loaded.DeveloperResidents(false);var empty=DemoWorld.LoadJson(loaded.SaveJson());empty.DeveloperResidents(true);
        Check(empty.Workers.Sum(p=>p.Person.GrowthXp)==total&&empty.Inventory(empty.Workers[1]).Any(),"Editor removal retains complete dossier and equipment");
        void Invalid(Action<JsonNode> mutate,string message)
        {
            var data=JsonNode.Parse(saved)!;mutate(data);bool rejected=false;
            try{DemoWorld.LoadJson(data.ToJsonString());}catch(InvalidDataException){rejected=true;}
            Check(rejected,message);
        }
        Invalid(n=>n["Workers"]![0]!["Person"]!["SpentPoints"]=99,"Overspent archive rejected");
        Invalid(n=>n["Items"]![0]!["Owner"]="ghost","Unknown inventory owner rejected");
        Invalid(n=>n["Workers"]![1]!["Id"]=n["Workers"]![0]!["Id"]!.GetValue<string>(),"Duplicate resident identity rejected");
        Invalid(n=>n["Workers"]![0]!["Person"]!["Blood"]=-1,"Invalid health rejected");
        Invalid(n=>n.AsObject().Remove("Research"),"Archive must retain research");
        Invalid(n=>n["Workers"]![0]!.AsObject().Remove("Person"),"Archive must retain character profile");
    }
    private static void WorkAndSocial()
    {
        var w=Fresh();var a=w.Workers[0];w.SpawnItem("cobblestone",20,new Cell(5,10));
        Check(w.QueueBuild(new Cell(6,10),BuildKind.Wall),"Real work queued");Advance(w,30);
        Check(w.Workers.Sum(w=>w.Person.GrowthXp)>0&&w.Buildings.ContainsKey(new Cell(6,10)),"Construction earns growth and skill XP");
        long xp=w.Workers.Sum(w=>w.Person.GrowthXp);Check(w.QueueBuild(new Cell(7,10),BuildKind.Wall),"Cancellable work queued");w.CancelJob(w.Jobs.Single());Advance(w,2);
        Check(w.Workers.Sum(w=>w.Person.GrowthXp)==xp,"Cancellation gives no free work XP");
        var b=w.Workers[1];Check(w.ActResident(new(a.Id,"social",b.Id)).Ok,"Social action scheduled");Advance(w,35);
        Check(a.Person.Relations.Any(r=>r.Target==b.Id)&&b.Person.Relations.Any(r=>r.Target==a.Id),"Social event changes both independent opinions");
        Check(!w.ActResident(new(a.Id,"social",b.Id)).Ok,"Social cooldown prevents farming");
        for(int i=0;i<100;i++)a.Person.Remember("event"+i,"重要事件",i);
        Check(a.Person.Memories.Count==24,"History memory cost is bounded");
        var moving=Fresh();moving.SpawnItem("cobblestone",20,new Cell(3,10));moving.QueueBuild(new Cell(15,10),BuildKind.Wall);
        Advance(moving,.3);var busy=moving.Workers.First(w=>w.Job is not null);var current=busy.Job;int steps=busy.Path.Count;
        Check(!moving.ActResident(new(busy.Id,"cancel")).Ok&&busy.Job==current&&busy.Path.Count==steps,"Cancelling absent personal activity must not clear a production route");
        // Normal building reservations cannot be stolen into personal inventory.
        var reserve=Fresh();var stack=reserve.SpawnItem("iron",2,new Cell(6,6));
        var job=new DemoJob{Id=900,Cell=new Cell(6,6),Haul=true};job.Materials.Add(new(){StackId=stack.Id,Item="iron",Count=2});reserve.Jobs.Add(job);
        Check(!reserve.ActResident(new(reserve.Workers[0].Id,"pickup",ItemId:stack.Id)).Ok,"Reserved cargo cannot be picked up");
    }
}
