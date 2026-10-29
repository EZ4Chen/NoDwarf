using NoDwarf.Services.Llm;
using NoDwarf.Simulation;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// Opt-in paid integration. Read credentials from a no-echo stdin, never argv or a saved setting.
internal static class StorytellerLive
{
    private static readonly JsonSerializerOptions Json = new(){WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static async Task Run(bool establishedOnly=false)
    {
        int limit=establishedOnly?4:12;
        Console.WriteLine($"LIVE_KEY_STDIN_READY (no key is logged; maximum {limit} requests)");
        string key=Console.ReadLine()?.Trim()??"";
        if(key.Length==0)throw new InvalidOperationException("Missing stdin credential");
        var config=new LlmConfiguration{ApiKey=key,Settings=new(){Provider="deepseek",Model="deepseek-flash",TimeoutSeconds=45,MaxOutputTokens=768,SessionRequestLimit=limit}};
        using var client=new LlmClient(new WireAudit());
        var capture=new CapturedClient(client);
        string directory=Path.Combine(Environment.CurrentDirectory,".local","storyteller-live",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        var results=new List<object>();
        foreach(string scenario in establishedOnly?new[]{"established_day_four"}:new[]{"first_day","wealthy_after_grace","loss_recovery"})
        {
            var world=CreateWorld(scenario);
            var initial=new {value=world.Values(true),threat=world.StoryThreatBudget(),gift=world.StoryGiftBudget(),world.Story.GraceUntil,world.Story.RecoveryUntil};
            capture.Scenario=scenario;
            Console.WriteLine($"SCENARIO {scenario}: value={initial.value.Total:0}, threat={initial.threat}, capable={initial.value.Capable}");
            bool receivedRestore=false;int ticksDuringNetwork=0;double maxPumpMs=0;
            var deadline=Stopwatch.StartNew();
            using(var director=new StorytellerDirector(capture))
            {
                while(world.Story.Turn?.Status is not ("completed" or "waiting"))
                {
                    if(deadline.Elapsed.TotalSeconds>210)throw new InvalidOperationException("Scenario deadline exceeded");
                    var timer=Stopwatch.StartNew();director.Pump(world,config,true);
                    maxPumpMs=Math.Max(maxPumpMs,timer.Elapsed.TotalMilliseconds);
                    if(director.InFlight){world.Update(1d/30);ticksDuringNetwork++;}
                    if(!receivedRestore&&world.Story.Turn?.Status=="received")
                    {
                        string before=world.Story.Turn.AcceptedJson;
                        int queued=world.Story.Pending.Count;
                        director.Pump(world,config,false);
                        Check(world.Story.Pending.Count==queued,"Paused response executed a tool");
                        world=DemoWorld.LoadJson(world.SaveJson());
                        Check(world.Story.Turn!.AcceptedJson==before,"Received JSON lost during reload");
                        receivedRestore=true;
                    }
                    await Task.Delay(20);
                }
            }
            var turn=world.Story.Turn!;
            var planned=world.Story.Pending.Select(e=>new StoryEvent{Id=e.Id,Kind=e.Kind,Due=e.Due,Count=e.Count,Item=e.Item,Weather=e.Weather,Duration=e.Duration,Reason=e.Reason}).ToArray();
            Console.WriteLine($"DECISION {scenario}: {turn.Status}, {turn.Requests} requests, {planned.Length} queued; {world.Story.Error}");
            world=DemoWorld.LoadJson(world.SaveJson());
            Check(world.Story.Pending.Count==planned.Length,"Queued event lost during reload");
            // No more director pumps: advance scheduled events only, without additional paid decisions.
            var effects=new List<object>();int historyCount=world.Story.History.Count;
            double until=planned.Select(e=>e.Due+e.Duration).DefaultIfEmpty(world.SimulationTime).Max()+1;
            while(world.SimulationTime<until)
            {
                var beforeItems=world.Items.GroupBy(i=>i.Item).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Count));
                int beforeEnemies=world.Enemies.Count;
                world.Update(1d/30);
                if(world.Story.History.Count==historyCount)continue;
                foreach(var h in world.Story.History.Skip(historyCount))
                {
                    var e=planned.FirstOrDefault(e=>e.Id==h.EventId);if(e is null)continue;
                    int? itemDelta=e.Kind=="supplies"?world.Items.Where(i=>i.Item==e.Item).Sum(i=>i.Count)-beforeItems.GetValueOrDefault(e.Item):null;
                    effects.Add(new {history=h,delta_item=itemDelta,delta_enemies=world.Enemies.Count-beforeEnemies,weather=world.Weather.Id});
                    if(h.Outcome=="executed")
                    {
                        if(e.Kind=="supplies")Check(itemDelta==e.Count,"Executed supplies did not add expected quantity");
                        if(e.Kind=="raid")Check(world.Enemies.Count-beforeEnemies==e.Count,"Executed raid did not spawn expected enemies");
                        if(e.Kind=="weather")Check(world.Weather.Id==e.Weather,"Executed weather did not apply");
                    }
                    Console.WriteLine($"EVENT {scenario}: {h.Kind} {h.Outcome}, item_delta={itemDelta}, enemies={world.Enemies.Count-beforeEnemies}, {h.Detail}");
                }
                historyCount=world.Story.History.Count;
            }
            Check(world.Story.Pending.Count==0,"Scheduled events remain pending after deadline");
            var restored=DemoWorld.LoadJson(world.SaveJson());restored.Update(1d/30);
            Check(restored.Story.Pending.Count==0,"Completed event replayed after reload");
            results.Add(new {scenario,initial,turn,planned,effects,receivedRestore,ticksDuringNetwork,maxPumpMs,final=new{time=world.SimulationTime,value=world.Values(true),enemies=world.Enemies.Count,world.Story.ActiveWeather,history=world.Story.History}});
            File.WriteAllText(Path.Combine(directory,"results.json"),JsonSerializer.Serialize(new{model="deepseek-flash",thinking="disabled",requests=client.Requests,calls=capture.Calls,scenarios=results},Json));
            if(turn.Status=="waiting"&&turn.Receipts.Count==0){Console.WriteLine("STOP: request/validation failed; no automatic retry.");break;}
        }
        Console.WriteLine($"LIVE_DONE requests={client.Requests}; report={Path.Combine(directory,"results.json")}");
    }
    private static DemoWorld CreateWorld(string scenario)
    {
        var w=new DemoWorld(flat:true);w.DeveloperResidents(false);w.DeveloperResidents(true);w.SetWeather("auto");w.EnableStory(true);
        if(scenario!="first_day")w.Story.GraceUntil=0; // Explicit fixture: simulate protection having ended.
        if(scenario is "wealthy_after_grace" or "established_day_four"){w.SpawnItem("iron",500,w.Camp);w.SpawnItem("meal",100,w.Camp);}
        if(scenario=="established_day_four")
        {
            w.SpawnItem("wood",200,w.Camp);w.SpawnItem("bandage",20,w.Camp);
            foreach(var (cell,kind) in new[]{(new Cell(5,9),BuildKind.Loom),(new Cell(6,9),BuildKind.Workbench),(new Cell(7,9),BuildKind.Anvil),(new Cell(4,8),BuildKind.Bed),(new Cell(5,8),BuildKind.Bed),(new Cell(6,8),BuildKind.Bed)})w.Buildings[cell]=new(cell,kind);
            // A constructed save fixture, not three days of measured gameplay.
            var saved=JsonNode.Parse(w.SaveJson())!;saved["SimulationTime"]=3*GameCalendar.SecondsPerDay;
            w=DemoWorld.LoadJson(saved.ToJsonString());
        }
        if(scenario=="loss_recovery")
        {
            w.Workers[0].Person.DeathCause="测试场景中的既有伤亡";
            w.Story.History.Add(new("fixture-loss","loss",w.SimulationTime,"observed","一位居民刚刚死亡，营地进入恢复期。"));
            w.Story.RecoveryUntil=w.SimulationTime+2*GameCalendar.SecondsPerDay;
            w.Story.PreviousCapable=w.Values(true).Capable;
        }
        return w;
    }
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private sealed class CapturedClient(LlmClient client):ILlmClient
    {
        public string Scenario="";
        public List<object> Calls=new();
        public async Task<LlmReply> CompleteAsync(LlmConfiguration c,IReadOnlyList<LlmMessage> m,CancellationToken ct)
        {
            var reply=await client.CompleteAsync(c,m,ct);
            Calls.Add(new{scenario=Scenario,reply.Json,reply.InputTokens,reply.OutputTokens,reply.Milliseconds});
            Console.WriteLine($"MODEL {Scenario} {reply.Milliseconds}ms: {reply.Json}");
            return reply;
        }
    }
    private sealed class WireAudit:DelegatingHandler
    {
        public WireAudit():base(new HttpClientHandler{AllowAutoRedirect=false}){}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellation));
            Check(request.RequestUri!.AbsoluteUri=="https://api.deepseek.com/chat/completions","Unexpected destination");
            Check(body.RootElement.GetProperty("model").GetString()=="deepseek-flash"&&body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="disabled","Flash/non-thinking configuration not sent");
            return await base.SendAsync(request,cancellation);
        }
    }
}
