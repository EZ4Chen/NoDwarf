using NoDwarf.Simulation;
using NoDwarf.Services.Llm;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

if(args.Contains("--live")){await StorytellerLive.Run(args.Contains("--established"));return;}

static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
static void Reject(Action action)
{try{action();throw new Exception("Expected rejection");}catch(Exception e) when(e is LlmException or InvalidDataException){}}
static DemoWorld World()
{
    var w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.DeveloperResidents(true);w.EnableStory(true);return w;
}
static StoryTurn Accept(DemoWorld w,string json,string? id=null)
{
    var t=new StoryTurn{Id=id??Guid.NewGuid().ToString("N"),ObservedAt=w.SimulationTime,VitalState=w.StoryVitalState(),Status="received",Requests=1,AcceptedJson=json};
    t.AcceptedCallId=t.Id+":0";w.Story.Turn=t;return t;
}
static void Advance(DemoWorld w,double seconds)
{for(int i=0;i<(int)Math.Ceiling(seconds*30);i++)w.Update(1d/30);}
static string Supplies(int count=2)=>JsonSerializer.Serialize(new{tool="schedule_supplies",arguments=new{item="wood",count,delay_hours=.1,reason="资源发现测试"}});
var config=new LlmConfiguration{ApiKey="fixture-only",Settings=new(){SessionRequestLimit=100}};
foreach(var item in ItemCatalog.All)Check(ValueCatalog.Base(item.Id)>=0,"Missing item value: "+item.Id);
var original=World();original.Items.Clear();original.Jobs.Clear();
var stack=original.SpawnItem("wood",10,original.Camp);
double before=original.Values(true).Items;stack.Owner=original.Workers[0].Id;
Check(original.Values(true).Items==before,"Inventory transfer changed wealth");stack.Owner="";
var job=new DemoJob{Id=900,Cell=original.Camp};job.Materials.Add(new(){StackId=stack.Id,Item="wood",Count=4,Stage=0});original.Jobs.Add(job);
Check(original.Values(true).Items==before,"Reservation double-counted wealth");
stack.Count-=4;job.Materials[0].Stage=1;job.Materials[0].StackId=0;
Check(original.Values(true).Items==before,"Carried cargo disappeared from wealth");
var delivered=original.SpawnItem("wood",4,original.Camp+new Cell(1,0));job.Materials[0].Stage=2;job.Materials[0].StackId=delivered.Id;
Check(original.Values(true).Items==before,"Delivered cargo double-counted");
Check(ValueCatalog.Item("sword",new(){MaterialId="steel",QualityId="fine"},0)>ValueCatalog.Base("sword"),"Material/quality not valued");
Check(ValueCatalog.Item("sword",new(){Durability=50,MaxDurability=100},0)==ValueCatalog.Base("sword")*.5,"Durability ignored");
Check(ValueCatalog.Item("meal",new(){ExpiresAt=1},2)==0,"Expired food counted");
original.Jobs.Clear();original.Items.Clear();original.SpawnItem("wood",10,original.Camp);
var cell=original.Camp+new Cell(2,2);original.Buildings[cell]=new(cell,BuildKind.Wall);
double built=original.Values(true).Buildings;original.DamageBlock(cell,10);
Check(original.Values(true).Buildings<built,"Building damage ignored");
var disarmed=World();var wielder=disarmed.Workers[0];wielder.Person.Blood=10;
double reducedPower=disarmed.Values(true).CombatPower;
var weapon=disarmed.SpawnItem("sword",1,disarmed.Camp);weapon.Owner=wielder.Id;weapon.Slot="main";
Check(disarmed.Values(true).CombatPower==reducedPower,"Incapacitated resident weapon increased raid budget");
Console.WriteLine("VALUE_OK: all item definitions, material/quality/durability, ownership, reservations/cargo, expiry and buildings");

var w=World();int wood=w.Total("wood");var accepted=Accept(w,Supplies());
string pendingResponse=w.SaveJson();w=DemoWorld.LoadJson(pendingResponse);accepted=w.Story.Turn!;
string result=StorytellerTools.Apply(w,accepted);
Check(w.Story.Pending.Count==1&&w.Total("wood")==wood,"Tool did not schedule an actual event");
Check(StorytellerTools.Apply(w,accepted)==result&&w.Story.Pending.Count==1,"Repeated call scheduled twice");
// Save the receipt and the still-unacknowledged JSON together: recovery must return the receipt.
w=DemoWorld.LoadJson(w.SaveJson());Check(StorytellerTools.Apply(w,w.Story.Turn!)==result&&w.Story.Pending.Count==1,"Receipt replay after load duplicated event");
Advance(w,3);Check(w.Total("wood")==wood+2&&w.Story.Pending.Count==0&&w.Story.History.Any(h=>h.Outcome=="executed"),"Scheduled supplies did not occur");
var executed=DemoWorld.LoadJson(w.SaveJson());Advance(executed,3);Check(executed.Total("wood")==w.Total("wood"),"Executed event replayed after save");
Console.WriteLine("STORY_SAVE_OK: accepted JSON, queued event, receipt/ack gap, executed event and no duplicate resources after restore");
foreach (string command in new[]{"{\"tool\":\"wait\",\"arguments\":{\"hours\":6,\"reason\":\"休养\"}}","{\"tool\":\"finish\",\"arguments\":{\"summary\":\"安排完成\"}}"})
{
    var ended=World();StorytellerTools.Apply(ended,Accept(ended,command));
    ended=DemoWorld.LoadJson(ended.SaveJson());
    using var director=new StorytellerDirector(new FailClient());director.Pump(ended,config,true);
    Check(ended.Story.Turn!.Status=="completed"&&ended.Story.Turn.Receipts.Count==1,"Restored terminating receipt resumed agent or repeated action");
}

var weather=World();weather.SetWeather("auto");
var wt=Accept(weather,"{\"tool\":\"schedule_weather\",\"arguments\":{\"weather\":\"fog\",\"duration_hours\":1,\"delay_hours\":0.1,\"reason\":\"短期雾\"}}");
StorytellerTools.Apply(weather,wt);Advance(weather,3);Check(weather.Weather.Id=="fog","Weather did not apply");
weather=DemoWorld.LoadJson(weather.SaveJson());Advance(weather,26);Check(weather.Story.ActiveWeather==""&&weather.WeatherOverride is null,"Weather did not expire naturally");
var cancel=World();StorytellerTools.Apply(cancel,Accept(cancel,Supplies()));var cancelId=cancel.Story.Pending.Single().Id;
cancel.CancelStoryEvent(cancelId,"test");Advance(cancel,3);Check(cancel.Story.History.Any(h=>h.Outcome=="cancelled"),"Cancellation missing");
var grace=World();var raid=Accept(grace,"{\"tool\":\"schedule_raid\",\"arguments\":{\"count\":1,\"delay_hours\":2,\"reason\":\"袭击测试\"}}");
Check(StorytellerTools.Apply(grace,raid).Contains("false")&&grace.Story.Pending.Count==0,"Grace period bypassed");
grace.Story.GraceUntil=0;double budget=grace.StoryThreatBudget();grace.SpawnItem("iron",1000,grace.Camp);
Check(grace.StoryThreatBudget()>=budget&&grace.StoryThreatBudget()<=grace.Values(true).Capable*100,"Wealth/capability limits wrong");
Check(grace.ScheduleStoryEvent(new(){Id="raid-real",Kind="raid",Count=1,Due=grace.SimulationTime+2*DemoWorld.StoryHour,Reason="real raid"}).Ok,"Raid schedule failed");
grace.Story.Pending[0].Due=grace.SimulationTime+.01;Advance(grace,.1);Check(grace.Enemies.Count==1,"Actual raid not created");
var wide=World();Check(wide.StartRaid(4).Ok,"Formal raid cap not expanded");Check(DemoWorld.LoadJson(wide.SaveJson()).Enemies.Count==4,"Larger raid lost in save");
var loss=World();loss.Story.GraceUntil=0;Advance(loss,.1);loss.Workers[0].Person.Blood=10;Advance(loss,2.1);Check(loss.Story.RecoveryUntil>loss.SimulationTime,"Loss recovery not applied");
Console.WriteLine("STORY_EVENTS_OK: actual raid, wealth/capability budget, first-day grace, recovery, weather expiry, cancellation and expanded raid save");

foreach(string json in new[]{"{\"tool\":\"arbitrary_code\",\"arguments\":{}}","{\"tool\":\"schedule_supplies\",\"arguments\":{\"item\":\"wood\",\"count\":999,\"delay_hours\":1,\"reason\":\"bad\"}}","{\"tool\":\"wait\",\"arguments\":{\"hours\":1,\"hours\":2,\"reason\":\"duplicate\"}}"})
{var invalid=World();Reject(()=>StorytellerTools.Apply(invalid,Accept(invalid,json)));Check(invalid.Story.Pending.Count==0,"Invalid JSON changed world");}
var stale=World();var st=Accept(stale,Supplies());stale.Workers[0].Person.Blood=10;Reject(()=>StorytellerTools.Apply(stale,st));
var archive=JsonNode.Parse(w.SaveJson())!;archive["Story"]!["Pending"]=JsonNode.Parse("[{\"Id\":\"bad\",\"Kind\":\"raid\",\"Count\":999,\"Due\":1}]");Reject(()=>DemoWorld.LoadJson(archive.ToJsonString()));
var interrupted=World();var it=Accept(interrupted,Supplies());it.AcceptedJson="";it.AcceptedCallId="";it.Status="requesting";
Check(DemoWorld.LoadJson(interrupted.SaveJson()).Story.Turn!.Status=="waiting","Interrupted request silently resent");
Console.WriteLine("STORY_GUARDS_OK: invalid tools/arguments/duplicate fields, stale health snapshot, malformed archive");

var delayed=new DelayedClient();var asyncWorld=World();using(var director=new StorytellerDirector(delayed))
{
    director.Pump(asyncWorld,config,true);await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(asyncWorld.Story.Turn!.Status=="requesting"&&asyncWorld.Story.Pending.Count==0,"Request phase wrong");
    Advance(asyncWorld,.1);Check(asyncWorld.SimulationTime>0,"Network blocked simulation");
    delayed.Reply.SetResult(new(Supplies(),10,8,5));
    for(int i=0;i<100&&asyncWorld.Story.Turn.Status!="received";i++){await Task.Delay(5);director.Pump(asyncWorld,config,false);}
    Check(asyncWorld.Story.Turn.Status=="received"&&asyncWorld.Story.Pending.Count==0,"Paused completion executed event");
    var pausedSave=DemoWorld.LoadJson(asyncWorld.SaveJson());
    using(var restoredDirector=new StorytellerDirector(new FailClient())){restoredDirector.Pump(pausedSave,config,true);Check(pausedSave.Story.Pending.Count==1,"Saved received JSON was not executed without network");}
    director.Pump(asyncWorld,config,true);Check(asyncWorld.Story.Pending.Count==1,"Main-thread pump did not dispatch");
}
var abandoned=new DelayedClient();var oldWorld=World();var replacement=World();replacement.EnableStory(false);
using(var director=new StorytellerDirector(abandoned))
{
    director.Pump(oldWorld,config,true);await abandoned.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));director.Pump(replacement,config,false);
    abandoned.Reply.TrySetResult(new(Supplies(),1,1,1));await Task.Delay(20);director.Pump(replacement,config,true);
    Check(replacement.Story.Pending.Count==0&&oldWorld.Story.Pending.Count==0,"Old response crossed world boundary");
}
var cycle=World();using(var director=new StorytellerDirector(new SequenceClient(Supplies(),"{\"tool\":\"wait\",\"arguments\":{\"hours\":6,\"reason\":\"休养\"}}")))
{
    for(int i=0;i<200&&cycle.Story.Turn?.Status!="completed";i++){director.Pump(cycle,config,true);await Task.Delay(5);}
    Check(cycle.Story.Turn?.Status=="completed"&&cycle.Story.Turn.Receipts.Count==2&&cycle.Story.Pending.Count==1,"Agent did not use tool result and finish plan");
}
Console.WriteLine("STORY_ASYNC_OK: nonblocking requests, paused receive, saved reply, stale-world cancellation, tool-result continuation and deterministic execution");

var perf=World();perf.EnableStory(false);perf.Items.Clear();
for(int i=0;i<20000;i++)perf.Items.Add(new(){Id=100000+i,Item="wood",Count=1,Cell=perf.Camp});
perf.Values(true);var timer=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<1000;i++)perf.Values();
long cacheBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;double cacheMs=timer.Elapsed.TotalMilliseconds;
timer.Restart();for(int i=0;i<20;i++)perf.Values(true);double fullMs=timer.Elapsed.TotalMilliseconds/20;
Check(cacheBytes<100000,"Cached reads allocate excessively");
Console.WriteLine($"VALUE_PERF: 20,000 stacks; full aggregate avg {fullMs:F3} ms; 1,000 cached reads {cacheMs:F3} ms / {cacheBytes} bytes");
Console.WriteLine("STORYTELLER_CHECKS_OK: offline integration; no cloud requests or GUI");

sealed class DelayedClient:ILlmClient
{
    public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<LlmReply> Reply=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<LlmReply> CompleteAsync(LlmConfiguration c,IReadOnlyList<LlmMessage> m,CancellationToken ct){Started.TrySetResult();return Reply.Task.WaitAsync(ct);}
}
sealed class FailClient:ILlmClient
{public Task<LlmReply> CompleteAsync(LlmConfiguration c,IReadOnlyList<LlmMessage> m,CancellationToken ct)=>throw new Exception("Unexpected network call");}
sealed class SequenceClient(params string[] replies):ILlmClient
{
    private int _index;
    public Task<LlmReply> CompleteAsync(LlmConfiguration c,IReadOnlyList<LlmMessage> m,CancellationToken ct)
    {if(_index>0&&!m.Last().content.Contains("tool_result"))throw new Exception("Tool feedback missing");return Task.FromResult(new LlmReply(replies[_index++],2,2,1));}
}
