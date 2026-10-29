using System.Net;
using System.Text;
using System.Text.Json;
using NoDwarf.Services.Llm;

static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
static JsonElement Json(string value)=>JsonDocument.Parse(value).RootElement.Clone();
static async Task Reject(Func<Task> action,string fragment="")
{
    try{await action();throw new Exception("Expected rejection");}
    catch(LlmException e){Check(e.Message.Contains(fragment),"Unexpected error: "+e.Message);}
}
static HttpResponseMessage Reply(string content,string finish="stop")=>new(HttpStatusCode.OK)
{Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{finish_reason=finish,message=new{content}}},usage=new{prompt_tokens=12,completion_tokens=8}}))};
var config=new LlmConfiguration{ApiKey="secret-fixture",Settings=new(){SessionRequestLimit=100}};
foreach(string url in new[]{"http://external.example","https://user:pass@example.com","https://example.com?key=x","file:///tmp/a"})
    await Reject(()=>{(config.Settings with{BaseUrl=url}).Endpoint();return Task.CompletedTask;});
Check((config.Settings with{BaseUrl="http://127.0.0.1:1234/v1/"}).Endpoint().AbsoluteUri=="http://127.0.0.1:1234/v1/chat/completions","Local endpoint normalization");
Check((config.Settings with{BaseUrl="https://example.com/v1/chat/completions"}).Endpoint().AbsolutePath=="/v1/chat/completions","Full endpoint duplicated");
var temp=Path.Combine(Path.GetTempPath(),"nodwarf-llm-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try
{
    string file=Path.Combine(temp,"settings.json");LlmSettingsStore.Save(file,config);
    Check(!File.ReadAllText(file).Contains(config.ApiKey)&&LlmSettingsStore.Load(file).ApiKey=="","Session key persisted");
    LlmSettingsStore.Save(file,new(){Settings=config.Settings,ApiKey=config.ApiKey,RememberKey=true});
    Check(LlmSettingsStore.Load(file).ApiKey==config.ApiKey,"Remembered key not restored");
    if(!OperatingSystem.IsWindows())Check((File.GetUnixFileMode(file)&(UnixFileMode.GroupRead|UnixFileMode.OtherRead))==0,"Secret file readable by other users");
    LlmSettingsStore.Save(file,config);Check(!File.ReadAllText(file).Contains(config.ApiKey),"Unchecking remember did not remove key");
    File.WriteAllText(file,"{broken secret-fixture");await Reject(()=>{LlmSettingsStore.Load(file);return Task.CompletedTask;},"无法读取");
}
finally{Directory.Delete(temp,true);}
var handler=new Handler(async(request,ct)=>
{
    Check(request.Headers.Authorization?.Parameter==config.ApiKey,"Authorization missing");
    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
    Check(body.RootElement.GetProperty("response_format").GetProperty("type").GetString()=="json_object","JSON mode missing");
    Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="disabled","Thinking not disabled");
    var observation=JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
    return Reply(JsonSerializer.Serialize(new{tool="connection_check",arguments=new{nonce=observation.RootElement.GetProperty("nonce").GetString()}}));
});
using(var client=new LlmClient(handler))
{
    var reply=await LlmConnectionProbe.RunAsync(client,config,default);
    Check(reply.InputTokens==12&&reply.OutputTokens==8&&client.Requests==1,"Usage missing");
    await Reject(()=>LlmConnectionProbe.RunAsync(client,new(){Settings=config.Settings with{SessionRequestLimit=1},ApiKey=config.ApiKey},default),"请求上限");
}
foreach(var bad in new[]{"", "```json\n{}\n```", "[]", "{\"tool\":\"a\",\"tool\":\"b\"}", "{\"a\":{\"x\":1,\"x\":2}}"})
    await Reject(()=>{using var d=AgentJson.Parse(bad);return Task.CompletedTask;});
foreach(var response in new[]{Reply("{}","length"),new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new StringContent("secret-fixture")},Reply("not json")})
{
    using var client=new LlmClient(new Handler((_,_)=>Task.FromResult(response)));
    await Reject(async()=>{try{await LlmConnectionProbe.RunAsync(client,config,default);}catch(LlmException e){Check(!e.Message.Contains("secret-fixture"),"Error leaked server body");throw;}});
}
using(var client=new LlmClient(new Handler(async(request,ct)=>
{
    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
    Check(!body.RootElement.TryGetProperty("thinking",out _),"Custom provider received vendor-only parameter");
    return Reply("{}");
})))
    await client.CompleteAsync(new(){Settings=config.Settings with{Provider="compatible"}},new[]{new LlmMessage("user","Return JSON")},default);
foreach(var response in new[]{new HttpResponseMessage(HttpStatusCode.Redirect),new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new string('x',262145))}})
{
    using var client=new LlmClient(new Handler((_,_)=>Task.FromResult(response)));
    await Reject(()=>client.CompleteAsync(config,new[]{new LlmMessage("user","Return JSON")},default));
}
using(var client=new LlmClient(new Handler(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Reply("{}");})))
    await Reject(()=>client.CompleteAsync(new(){Settings=config.Settings with{TimeoutSeconds=5}},new[]{new LlmMessage("user","Return JSON")},default),"超时");
using(var client=new LlmClient(new Handler(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Reply("{}");})))
{
    using var cancel=new CancellationTokenSource();var a=LlmConnectionProbe.RunAsync(client,config,cancel.Token);var b=LlmConnectionProbe.RunAsync(client,config,cancel.Token);
    await Reject(()=>LlmConnectionProbe.RunAsync(client,config,default),"正忙");cancel.Cancel();
    foreach(var task in new[]{a,b}){try{await task;throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}}
}
int events=0;var receipts=new HashSet<string>();
var tool=new AgentTool("schedule_event","Schedule a test event",Json("{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":3}},\"required\":[\"count\"],\"additionalProperties\":false}"),
    args=>{AgentJson.Fields(args,"count");if(!args.GetProperty("count").TryGetInt32(out int n)||n is <1 or >3)throw new LlmException("Invalid count");},
    (context,args,ct)=>{ct.ThrowIfCancellationRequested();Check(context.ExpectedVersion==7,"Version missing");Check(receipts.Add(context.CallId),"Duplicate mutation");events+=args.GetProperty("count").GetInt32();return Task.FromResult(Json("{\"ok\":true}"));});
var fake=new FakeClient("{\"tool\":\"schedule_event\",\"arguments\":{\"count\":2}}","{\"tool\":\"finish\",\"arguments\":{\"summary\":\"done\"}}");
var run=new AgentRunState{ExpectedVersion=7};string saved="";
await new JsonAgent(fake).RunAsync(config,run,"test","{}",new[]{tool},()=>true,s=>saved=JsonSerializer.Serialize(s),default);
Check(events==2&&run.Status=="completed"&&run.Steps.Count==1&&saved.Contains("schedule_event"),"Tool JSON did not dispatch/checkpoint");
var restored=JsonSerializer.Deserialize<AgentRunState>(saved)!;
await new JsonAgent(fake).RunAsync(config,restored,"test","{}",new[]{tool},()=>true,_=>{},default);
Check(events==2&&fake.Calls==2,"Completed run replayed");
foreach(string invalid in new[]{"{\"tool\":\"unknown\",\"arguments\":{}}","{\"tool\":\"schedule_event\",\"arguments\":{\"count\":999}}","{\"tool\":\"schedule_event\",\"arguments\":{\"count\":1,\"extra\":1}}"})
    await Reject(()=>new JsonAgent(new FakeClient(invalid)).RunAsync(config,new(){ExpectedVersion=7},"test","{}",new[]{tool},()=>true,_=>{},default));
int checks=0;
await Reject(()=>new JsonAgent(new FakeClient("{\"tool\":\"schedule_event\",\"arguments\":{\"count\":1}}")).RunAsync(config,new(){ExpectedVersion=7},"test","{}",new[]{tool},()=>++checks==1,_=>{},default),"过期");
Check(events==2,"Rejected call changed world");
await Reject(()=>new JsonAgent(fake).RunAsync(config,new(){Status="dispatching"},"test","{}",new[]{tool},()=>true,_=>{},default),"核对");
var uncertain=new AgentRunState{ExpectedVersion=7};
var failing=tool with{Dispatch=(_,_,_)=>throw new IOException("Host failed after possible mutation")};
try{await new JsonAgent(new FakeClient("{\"tool\":\"schedule_event\",\"arguments\":{\"count\":1}}")).RunAsync(config,uncertain,"test","{}",new[]{failing},()=>true,_=>{},default);throw new Exception("Expected host failure");}
catch(IOException){Check(uncertain.Status=="dispatching","Uncertain mutation became retryable");}
var limited=new AgentRunState{ExpectedVersion=7};
await Reject(()=>new JsonAgent(new FakeClient("{\"tool\":\"schedule_event\",\"arguments\":{\"count\":1}}")).RunAsync(config,limited,"test","{}",new[]{tool},()=>true,_=>{},default,maxSteps:1),"步数上限");
Check(limited.Status=="step_limit"&&limited.Steps.Count==1,"Step cap lost successful call");
Console.WriteLine("LLM_CHECKS_OK: configuration, key persistence/removal, HTTP contract, JSON validation, usage/budget, cancellation/concurrency, redacted failures, tool dispatch, stale result rejection and saved run replay. No external network.");

sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);}
sealed class FakeClient(params string[] replies):ILlmClient
{
    public int Calls;
    public Task<LlmReply> CompleteAsync(LlmConfiguration config,IReadOnlyList<LlmMessage> messages,CancellationToken cancellation)
    {cancellation.ThrowIfCancellationRequested();return Task.FromResult(new LlmReply(replies[Calls++],1,1,1));}
}
