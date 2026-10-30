using System.Text.Json;
using System.Text.Json.Nodes;
using NoDwarf.Simulation;
using NoDwarf.Services.Llm;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void Reject(Action action){try{action();}catch(Exception e) when(e is InvalidDataException or LlmException){return;}throw new Exception("Expected rejection");}
static DemoWorld World()
{
    var w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.DeveloperResidents(true);
    foreach(var p in w.Workers.Select(w=>w.Person)){p.Hunger=80;p.Thirst=80;p.Rest=80;p.Recreation=60;p.Schedule=ResidentWork.FreeDay();foreach(string key in p.WorkPriorities.Keys.ToArray())p.WorkPriorities[key]=0;}
    w.EnableResidentMind(true);return w;
}
static void Advance(DemoWorld w,double seconds){for(int i=0;i<(int)Math.Ceiling(seconds*30);i++)w.Update(1d/30);}
static string Choice(string id,IEnumerable<string>? refs=null)=>JsonSerializer.Serialize(new{tool="choose",arguments=new{option_id=id,reason="记得对方曾照料我，希望在有空时回应。",memory_ids=refs??Array.Empty<string>()}});
static ResidentMindTurn Accept(DemoWorld w,string? choice=null)
{
    var t=w.BeginMindTurn("fixture")??throw new Exception("No idle resident candidate");
    t.Response=Choice(choice??t.Choices.First(c=>c.Action=="social").Id,t.MemoryIds.Take(1));t.Status="received";return t;
}

var memory=ResidentProfile.Create(0);memory.Remember("care:friend","同伴曾护理我",0,"friend",eventId:"care-1");long version=memory.DecisionVersion;
memory.Remember("care:friend","重复投递",0,"friend",eventId:"care-1");Check(memory.DecisionVersion==version&&memory.Memories.Count(m=>m.EventId=="care-1")==1,"Duplicate event changed memory");
for(int i=0;i<1000;i++)memory.Remember("routine:"+i,"完成普通活动",0);
Check(memory.Memories.Count<=24&&memory.MemorySummaries.Count<=8&&memory.Memories.Any(m=>m.EventId=="care-1"),"Routine events evicted protected care memory");
Check(memory.MemorySummaries.Sum(s=>s.Evidence.Count)<=24&&memory.Recall(0,["friend"]).First().EventId=="care-1","Archive/recall unbounded or unrelated");
Check(memory.Gratitude("friend")>0,"Care did not form reciprocal goal");memory.Remember("helped:friend","已经送达口粮",1,"friend",importance:75);
Check(memory.Gratitude("friend")==0,"Completed help did not resolve reciprocal goal");
for(int i=0;i<100;i++)memory.Remember("milestone:"+i,"新的重要经历",2,importance:90);
Check(memory.Gratitude("friend")==0,"Archived help reopened completed goal");
Check(ResidentProfile.Create(1).Personality().Sociability>ResidentProfile.Create(0).Personality().Sociability,"Traits did not differentiate personality");
Console.WriteLine("MEMORY_OK: dedup, protected milestones through 1000 events, bounded factual archive, relevant recall, trait differences and goal completion");

var large=World();foreach(var person in large.Workers.Select(w=>w.Person))
{person.Childhood=new string('中',100);person.Adulthood=new string('文',100);for(int i=0;i<30;i++)person.Remember("event:"+i,new string('事',400),0);}
Check(large.BeginMindTurn("fixture") is {Observation.Length: <20000},"Valid long Chinese memories prevented a bounded request");

var w=World();var helper=w.Workers[0];var friend=w.Workers[2];
helper.Person.Remember("care:"+friend.Id,friend.Name+"曾照料我",0,friend.Id,eventId:"private-care");
using(var observed=JsonDocument.Parse(w.ObserveResident(helper.Id)))
    Check(observed.RootElement.GetProperty("mind").GetProperty("memories").EnumerateArray().Any(m=>m.GetProperty("EventId").GetString()=="private-care"),"Observation omitted memory");
Check(!JsonSerializer.Serialize(w.MindContext(w.Workers[1])).Contains("private-care"),"Personal memory leaked to uninvolved resident");
Check(w.MindChoices(helper).Where(c=>c.Action=="social").First().Target==friend.Id,"Care did not affect target preference");
var accepted=Accept(w,"social:"+friend.Id);string response=accepted.Response;
w=DemoWorld.LoadJson(w.SaveJson());accepted=w.ResidentMind.Turn!;
string receipt=ResidentMindTools.Apply(w,accepted);Check(w.Workers[0].Person.Activity?.Target==friend.Id,"Loaded accepted choice did not start real activity: "+receipt);
int events=w.ResidentMind.History.Count;long changed=w.Workers[0].Person.DecisionVersion;
Check(ResidentMindTools.Apply(w,accepted)==receipt&&w.ResidentMind.History.Count==events&&w.Workers[0].Person.DecisionVersion==changed,"Receipt replay duplicated action");
w=DemoWorld.LoadJson(w.SaveJson());Check(ResidentMindTools.Apply(w,w.ResidentMind.Turn!)==receipt&&w.ResidentMind.History.Count==events,"Saved receipt replay duplicated action");
Advance(w,20);Check(w.Workers[0].Person.Relations.Any(r=>r.Target==friend.Id&&r.Opinion>0),"Selected social activity never produced relationship outcome");
Console.WriteLine("MIND_ACTION_OK: private context, preferred target, accepted-response save/load, actual social action and relationship change, idempotent receipts");

// Two equally hungry immobile patients: deterministic care should prefer a past helper.
var care=World();var carer=care.Workers[0];carer.Person.WorkPriorities["care"]=2;
foreach(var patient in care.Workers.Skip(1))
{
    patient.Person.Hunger=20;patient.Person.Rest=20;
    care.Items.RemoveAll(item=>item.Owner==patient.Id&&item.Item=="meal");
    patient.Person.Injuries.Add(new(){Part="left_leg",Kind="missing",Severity=100,Treated=true});
    patient.Person.Injuries.Add(new(){Part="right_leg",Kind="missing",Severity=100,Treated=true});
}
var preferred=care.Workers[2];carer.Person.Remember("care:"+preferred.Id,"曾受照料",0,preferred.Id);
care.EnableResidentMind(false);Advance(care,3);
Check(carer.Person.Activity?.Target==preferred.Id,"Offline automatic feeding ignored gratitude: "+carer.Status+" target="+carer.Person.Activity?.Target+" preferred="+preferred.Id+" food="+carer.Person.FoodIssue);
for(int i=0;i<80&&!carer.Person.Memories.Any(m=>m.Kind=="helped");i++)Advance(care,1);
Check(carer.Person.Memories.Any(m=>m.Kind=="helped"&&m.Target==preferred.Id)&&preferred.Person.Memories.Any(m=>m.Kind=="fed"),"Food delivery did not record actual outcome");
Check(carer.Person.Gratitude(preferred.Id)==0,"Reciprocal goal remained after real feeding");
Console.WriteLine("OFFLINE_CARE_OK: equally urgent patients, remembered helper selected, real food consumed/delivered, goal resolved without model");

foreach(string invalid in new[]{"{\"tool\":\"delete_world\",\"arguments\":{}}",Choice("wait",["invented-memory"]),"{\"tool\":\"choose\",\"arguments\":{\"option_id\":\"wait\",\"reason\":\"x\",\"memory_ids\":[],\"extra\":1}}"})
{var bad=World();var t=Accept(bad);t.Response=invalid;Reject(()=>ResidentMindTools.Apply(bad,t));Check(bad.Workers.All(w=>w.Person.Activity is null),"Invalid tool mutated world");}
var stale=World();var st=Accept(stale);stale.Workers[0].Person.Remember("new","新的经历",0);Check(ResidentMindTools.Apply(stale,st).Contains("false"),"Stale memory decision executed");
var independent=World();var independentTurn=Accept(independent,"social:"+independent.Workers[1].Id);independent.Workers[2].Person.Remember("private","另一人自己的经历",0);
Check(ResidentMindTools.Apply(independent,independentTurn).Contains("true"),"Unrelated private event invalidated idle resident decision");
var manual=World();var mt=Accept(manual);manual.ActResident(new(manual.Workers[0].Id,"rest"));ResidentMindTools.Apply(manual,mt);Check(manual.Workers[0].Person.Activity?.Kind=="rest","Model replaced manual order");
var emergency=World();var et=Accept(emergency);emergency.Workers[1].Person.Blood=10;Check(ResidentMindTools.Apply(emergency,et).Contains("false"),"Emergency did not suppress discretionary action");
var sleep=World();var sl=Accept(sleep);sleep.Workers[0].Person.Schedule=Enumerable.Repeat("sleep",24).ToArray();Check(ResidentMindTools.Apply(sleep,sl).Contains("false"),"Model bypassed sleep schedule");
var malformed=JsonNode.Parse(World().SaveJson())!;malformed["Workers"]![0]!["Person"]!["MemorySummaries"]=JsonNode.Parse("[{\"Kind\":\"x\",\"Count\":-1}]");Reject(()=>DemoWorld.LoadJson(malformed.ToJsonString()));
var interrupted=World();Accept(interrupted);interrupted.ResidentMind.Turn!.Response="";interrupted.ResidentMind.Turn.Status="requesting";
Check(DemoWorld.LoadJson(interrupted.SaveJson()).ResidentMind.Turn!.Status=="waiting","Interrupted request auto-retried");
Console.WriteLine("MIND_GUARDS_OK: strict JSON/evidence, stale memory, manual orders, emergency, schedule, corrupt archive, interrupted-request recovery");

var config=new LlmConfiguration{ApiKey="offline-fixture"};var client=new Deferred();var asyncWorld=World();
using(var director=new ResidentMindDirector(client))
{
    director.Pump(asyncWorld,config,false);Check(asyncWorld.ResidentMind.Turn is null,"Paused game sent request");
    director.Pump(asyncWorld,config,true);await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(client.Messages!.Last().content.Contains("private-care")==false&&client.Messages!.Last().content.Contains("personality"),"Detached context missing");
    client.Reply.SetResult(new(Choice("wait"),1,1,1));
    for(int i=0;i<200&&asyncWorld.ResidentMind.Turn?.Status!="received";i++){await Task.Delay(5);director.Pump(asyncWorld,config,false);}
    Check(asyncWorld.ResidentMind.Turn?.Status=="received"&&asyncWorld.ResidentMind.History.Count==0,"Paused response applied action");
    director.Pump(asyncWorld,config,true);Check(asyncWorld.ResidentMind.History.Count==1,"Main-thread dispatch missing");
}
var switched=World();var late=new Deferred();using(var director=new ResidentMindDirector(late))
{
    director.Pump(switched,config,true);await late.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));var other=World();other.EnableResidentMind(false);
    director.Pump(other,config,false);late.Reply.TrySetResult(new(Choice("wait"),1,1,1));await Task.Delay(20);director.Pump(other,config,true);
    Check(other.ResidentMind.History.Count==0&&switched.ResidentMind.History.Count==0,"Late result crossed worlds");
}
Console.WriteLine("PERSONALITY_OK: async/pause/main-thread dispatch, world isolation; no external network requests");

sealed class Deferred:ILlmClient
{
    public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<LlmReply> Reply=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public IReadOnlyList<LlmMessage>? Messages;
    public Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken cancellation)
    {Messages=messages;Started.TrySetResult();return Reply.Task;}
}
