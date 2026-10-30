using System.Text.Json;
using System.Text.Json.Nodes;
using NoDwarf.Simulation;
using NoDwarf.Services.Llm;
static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static void Reject(Action action){try{action();}catch(Exception e) when(e is InvalidDataException or LlmException){return;}throw new Exception("Expected rejection");}
static DemoWorld World()
{
    var w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.DeveloperResidents(true);w.DialogueSettings=new(){Enabled=true,ShortTermRounds=2};
    foreach(var p in w.Workers.Select(w=>w.Person)){p.Hunger=80;p.Thirst=80;p.Rest=80;p.Recreation=60;p.Schedule=ResidentWork.FreeDay();foreach(string key in p.WorkPriorities.Keys.ToArray())p.WorkPriorities[key]=0;}
    return w;
}
static DialogueTurn Turn(DemoWorld w)
{w.Dialogues.NextAt=0;w.QueueConversation(w.Workers[0],w.Workers[1]);return w.BeginConversation()??throw new Exception("No eligible conversation");}
static void Line(DemoWorld w,DialogueTurn t,string text)
{t.Response=JsonSerializer.Serialize(new{text});t.Status="received";DialogueDirector.Apply(w,t);}
var world=World();var a=world.Workers[0];var b=world.Workers[1];var outsider=world.Workers[2];
a.Person.Remember("private","private-speaker-only",0);outsider.Person.Remember("private","secret-outsider",0);
var turn=Turn(world);string prompt=world.ConversationPrompt(turn);
Check(prompt.Contains("private-speaker-only")&&!prompt.Contains("secret-outsider"),"Speaker context leaked");
Line(world,turn,"今天想研究一下。");Check(!world.ConversationPrompt(turn).Contains("private-speaker-only"),"Next speaker received private memory");
Line(world,turn,"可以，休息后再继续。");Line(world,turn,"谢谢你的建议。");
turn.Response="{\"text\":\"回头再聊。\"}";turn.Status="received";
var loaded=DemoWorld.LoadJson(world.SaveJson());loaded.DialogueSettings=world.DialogueSettings;turn=loaded.Dialogues.Turn!;DialogueDirector.Apply(loaded,turn);
Check(loaded.Dialogues.Records.Count==1&&loaded.Dialogues.Records[0].Lines.Count==4,"Received save did not commit whole dialogue");
DialogueDirector.Apply(loaded,turn);Check(loaded.Dialogues.Records.Count==1,"Replay duplicated dialogue");
Check(JsonSerializer.Serialize(loaded.RecallDialogue(outsider.Id,a.Id))=="[]","Outsider recalled private conversation");
Check(JsonSerializer.Serialize(loaded.RecallDialogue(a.Id,b.Id),new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}).Contains("今天想研究"),"Participant recall missing");
var snapshot=loaded.CaptureSnapshot();snapshot.Dialogues!.Records[0].Lines.Clear();Check(loaded.Dialogues.Records[0].Lines.Count==4,"Snapshot aliases live conversation");
for(int i=0;i<3;i++){var t=Turn(loaded);for(int n=0;n<4;n++)Line(loaded,t,"第"+i+"轮的第"+n+"句话。");}
Check(loaded.Dialogues.Records.Count(r=>!r.IsLongTerm)==2,"Short-term retention not enforced");
var old=loaded.Dialogues.Records[0];var summary=new DialogueTurn{Kind="summary",Record=SnapshotCopy.Copy(old),Status="received",Response="{\"summary\":\"双方谈及研究并约定休息后继续。\"}"};loaded.Dialogues.Turn=summary;DialogueDirector.Apply(loaded,summary);
Check(old.Summary!=""&&old.Lines.Count==0&&old.IsLongTerm,"Summary not compacted");
var roundtrip=DemoWorld.LoadJson(loaded.SaveJson());Check(roundtrip.Dialogues.Records[0].Summary==old.Summary,"Long term summary lost");
var invalid=World();var bad=Turn(invalid);bad.Status="received";bad.Response="{\"text\":\"越权\",\"opinion\":100}";Reject(()=>DialogueDirector.Apply(invalid,bad));Check(invalid.Dialogues.Records.Count==0,"Bad response mutated ledger");
bad.Response="{\"text\":\"正常发言\"}";invalid.Workers[1].Person.Blood=0;DialogueDirector.Apply(invalid,bad);Check(invalid.Dialogues.Records.Count==0,"Invalid participant received dialogue");
var broken=JsonNode.Parse(loaded.SaveJson())!;broken["Dialogues"]!["Records"]![0]!["Participants"]=new JsonArray("same","same");Reject(()=>DemoWorld.LoadJson(broken.ToJsonString()));
var paused=World();var client=new Deferred();var config=new LlmConfiguration{ApiKey="fixture"};
using(var director=new DialogueDirector(client))
{
    director.Pump(paused,config,false);Check(paused.Dialogues.Turn is null,"Paused request started");
    director.Pump(paused,config,true);await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var interrupted=DemoWorld.LoadJson(paused.SaveJson());Check(interrupted.Dialogues.Turn?.Status=="waiting","Read auto-resumed HTTP");
    client.Reply.SetResult(new("{\"text\":\"今天天气不错。\"}",1,1,1));
    for(int i=0;i<100&&paused.Dialogues.Turn?.Status!="received";i++){await Task.Delay(5);director.Pump(paused,config,false);}
    Check(paused.Dialogues.Turn?.Status=="received"&&paused.Dialogues.Turn.Record.Lines.Count==0,"Paused reply applied");
    director.Pump(paused,config,true);Check(paused.Dialogues.Turn!.Record.Lines.Count==1,"Main-thread reply did not apply");
}
var off=World();var deferred=new Deferred();using(var director=new DialogueDirector(deferred))
{
    director.Pump(off,config,true);await deferred.Started.Task;off.DialogueSettings=off.DialogueSettings with{Enabled=false};director.Pump(off,config,false);
    deferred.Reply.SetResult(new("{\"text\":\"迟到回复\"}",0,0,0));director.Pump(off,config,true);Check(off.Dialogues.Turn is null&&off.Dialogues.Records.Count==0,"Disabled dialogue applied late response");
}
var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try
{
    var prefs=new GamePreferences{CameraSpeed=1.5,AutosaveMinutes=12,Dialogue=new(){Enabled=true,ShortTermRounds=8}};prefs.Save(path);Check(GamePreferences.Load(path)==prefs,"Preferences round trip");
    Reject(()=>(prefs with{Dialogue=prefs.Dialogue with{DetailedRounds=12,RecallRounds=2}}).Validate());
    File.WriteAllText(path,"{\"Version\":9}");Reject(()=>GamePreferences.Load(path));
}finally{File.Delete(path);File.Delete(path+".bak");}
Console.WriteLine("DIALOGUE_OK: participant privacy, alternating speech, save/receipt replay, snapshot isolation, short/long recall, summarization, stale/invalid replies, pause/cancel, preferences persistence; no network");
sealed class Deferred:ILlmClient
{
    public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<LlmReply> Reply=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<LlmReply> CompleteAsync(LlmConfiguration c,IReadOnlyList<LlmMessage> m,CancellationToken token){Started.TrySetResult();return Reply.Task;}
}
