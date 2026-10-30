using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoDwarf.Simulation;

public sealed record ResidentChoice(string Id,string Action,string Target,string Label,double Preference);
public sealed record ResidentDecision(string Id,string Resident,double Time,string Choice,string Reason,string Result,string[] Memories);
public sealed class ResidentMindTurn
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Resident {get;set;}="";
    public string Stamp {get;set;}="";
    public string Model {get;set;}="";
    public int PromptVersion {get;set;}=1;
    public double ObservedAt {get;set;}
    public string Status {get;set;}="requesting";
    public string Observation {get;set;}="";
    public string[] MemoryIds {get;set;}=[];
    public List<ResidentChoice> Choices {get;set;}=new();
    public string Response {get;set;}="";
    public string Receipt {get;set;}="";
}
public sealed class ResidentMindState
{
    public bool Enabled {get;set;}
    public int IntervalHours {get;set;}=2;
    public double NextAt {get;set;}
    public int Cursor {get;set;}
    public string Error {get;set;}="";
    public ResidentMindTurn? Turn {get;set;}
    public List<ResidentDecision> History {get;set;}=new();
}
public sealed partial class DemoWorld
{
    public ResidentMindState ResidentMind {get;private set;}=new();
    public void EnableResidentMind(bool enabled)
    {ResidentMind.Enabled=enabled;ResidentMind.Turn=null;ResidentMind.Error="";ResidentMind.NextAt=SimulationTime;Revision++;}
    public void RetryResidentMind()
    {if(ResidentMind.Enabled){ResidentMind.Turn=null;ResidentMind.Error="";ResidentMind.NextAt=SimulationTime;Revision++;}}
    public bool MindAvailable(DemoWorker w)=>!w.Person.Dead&&!w.Person.Incapacitated&&!w.WaterEvacuating&&!FloodThreat(w)&&!MilitaryControlled(w)&&
        w.Job is null&&w.Path.Count==0&&w.Person.Activity is null&&w.Person.PausedResearch is null&&w.Person.Hunger>=40&&w.Person.Thirst>=40&&w.Person.Rest>=30&&
        w.Person.Mobility>=.2&&w.Person.Manipulation>=.2&&ScheduledMode(w) is "anything" or "work"&&
        !Workers.Any(o=>!o.Person.Dead&&(HealthCare.Urgency(o.Person)<=2||NeedsFeeding(o)&&o.Person.Hunger<10));
    public DemoWorker[] MindNeighbors(DemoWorker w)=>Workers.Where(o=>o!=w&&!o.Person.Dead&&Math.Abs(o.Tile.X-w.Tile.X)+Math.Abs(o.Tile.Z-w.Tile.Z)+Math.Abs(o.Level-w.Level)<=24)
        .OrderByDescending(o=>w.Person.Affinity(o.Id)).ThenBy(o=>Math.Abs(o.Tile.X-w.Tile.X)+Math.Abs(o.Tile.Z-w.Tile.Z)).ThenBy(o=>o.Id).Take(8).ToArray();
    public List<ResidentChoice> MindChoices(DemoWorker w)
    {
        var choices=new List<ResidentChoice>();if(!MindAvailable(w))return choices;
        var p=w.Person;var persona=p.Personality();
        foreach(var other in MindNeighbors(w))
        {
            if(other.WaterEvacuating||MilitaryControlled(other)||FloodThreat(other))continue;
            double affinity=p.Affinity(other.Id);
            if(p.WorkPriorities.GetValueOrDefault("care")>0&&NeedsFeeding(other)&&!FoodCareBusy(other.Id,w)&&AvailableFood()>0)
                choices.Add(new("feed:"+other.Id,"feed",other.Id,"送饭给"+other.Name,60+affinity));
            if(p.WorkPriorities.GetValueOrDefault("doctor")>0&&HealthCare.Needed(other.Person)&&!RescueReserved(other.Id)&&!LifeReserved(other.Id)&&
                !Workers.Any(o=>o!=w&&o.Person.Activity is {Kind:"treat" or "care_supply"} a&&a.Target==other.Id)&&
                (Inventory(w).Any(i=>i.Item=="bandage")||Available("bandage")>0))
                choices.Add(new("treat:"+other.Id,"treat",other.Id,"护理"+other.Name,65+affinity));
            if(ScheduledMode(w)=="anything"&&!p.Isolated&&!other.Person.Isolated&&!other.Person.Incapacitated&&other.Job is null&&other.Path.Count==0&&other.Person.Activity is null&&
                !p.Relations.Any(r=>r.Target==other.Id&&SimulationTime-r.LastSocial<120))
                choices.Add(new("social:"+other.Id,"social",other.Id,"与"+other.Name+"交流",persona.Sociability*.5+affinity));
        }
        if(Research.Active!=""&&p.WorkPriorities.GetValueOrDefault("research")>0)choices.Add(new("research","research","","继续当前研究",persona.Curiosity*.7));
        if(ScheduledMode(w)=="anything"&&p.Recreation<85)choices.Add(new("relax","relax","","放松片刻",(100-p.Recreation)*.5));
        choices.Add(new("wait","wait","","继续原有生活与工作安排",20+persona.Duty*.1));
        return choices.OrderByDescending(c=>c.Preference).ThenBy(c=>c.Id).Take(20).ToList();
    }
    public string MindStamp(DemoWorker worker)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        worker.Id,Research.Version,mode=ScheduledMode(worker),CivilianAlert,
        worker.Person.DecisionVersion,worker.Tile,worker.Person.Traits,worker.Person.WorkPriorities,worker.Person.Isolated,
        food=(int)(worker.Person.Hunger/10),thirst=(int)(worker.Person.Thirst/10),rest=(int)(worker.Person.Rest/10)
    }))));
    public object MindContext(DemoWorker w,IEnumerable<string>? targetIds=null)
    {
        var neighbors=MindNeighbors(w);var targets=(targetIds??neighbors.Select(o=>o.Id)).ToArray();var p=w.Person;
        var memories=p.Recall(SimulationTime,targets);
        return new
        {
            personality=new{p.Age,p.Childhood,p.Adulthood,traits=p.Traits,disposition=p.Personality()},
            relationships=neighbors.Select(o=>new{resident=o.Id,name=o.Name,opinion=p.Relations.FirstOrDefault(r=>r.Target==o.Id)?.Opinion??0,gratitude=p.Gratitude(o.Id)}),
            goals=neighbors.Where(o=>p.Gratitude(o.Id)>0).Take(2).Select(o=>new{kind="reciprocate_care",target=o.Id,description="条件允许时帮助曾照料自己的同伴；不越过紧急救援与工作限制"}),
            memories,conversations=DialogueSettings.Enabled?RecallDialogue(w.Id,targets.FirstOrDefault()??""):Array.Empty<object>(),archive=p.MemorySummaries.Where(s=>targets.Contains(s.Target)).OrderByDescending(s=>s.Until).Take(2).Select(s=>new{s.Kind,s.Target,s.Count,s.From,s.Until,s.Text,evidence=s.Evidence.Select(m=>m.EventId)})
        };
    }
    public ResidentMindTurn? BeginMindTurn(string model)
    {
        if(!ResidentMind.Enabled||Workers.Count==0)return null;
        for(int i=0;i<Workers.Count;i++)
        {
            int index=(ResidentMind.Cursor+i)%Workers.Count;var w=Workers[index];if(!MindAvailable(w))continue;
            var choices=MindChoices(w);if(choices.Count<=1)continue;
            var memories=w.Person.Recall(SimulationTime,MindNeighbors(w).Select(o=>o.Id));
            var turn=new ResidentMindTurn{Resident=w.Id,Stamp=MindStamp(w),Model=model,ObservedAt=SimulationTime,Choices=choices,MemoryIds=memories.Select(m=>m.EventId).ToArray()};
            turn.Observation=JsonSerializer.Serialize(new{resident=w.Id,w.Name,time=SimulationTime,context=MindContext(w),
                needs=new{w.Person.Hunger,w.Person.Thirst,w.Person.Rest,w.Person.Recreation},choices,
                recent_decisions=ResidentMind.History.Where(h=>h.Resident==w.Id).TakeLast(2)},ResidentObservationJsonOptions);
            if(turn.Observation.Length>20000)continue;
            ResidentMind.Turn=turn;ResidentMind.Cursor=(index+1)%Workers.Count;ResidentMind.NextAt=SimulationTime+ResidentMind.IntervalHours*StoryHour;
            return turn;
        }
        ResidentMind.NextAt=SimulationTime+StoryHour;return null;
    }
    public ResidentResult ExecuteMindChoice(ResidentMindTurn turn,string id,string reason,string[] evidence)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==turn.Resident);
        if(!ResidentMind.Enabled||!ReferenceEquals(turn,ResidentMind.Turn)||w is null||!MindAvailable(w)||SimulationTime-turn.ObservedAt>2*StoryHour||MindStamp(w)!=turn.Stamp)
            return new(false,"观察已过期或居民正在执行其他安排，未执行决定");
        // Other residents keep living during HTTP. Recheck the chosen target/route now;
        // unrelated movement or private memories must not invalidate this resident's turn.
        var choice=MindChoices(w).FirstOrDefault(c=>c.Id==id);
        if(choice is null||turn.Choices.All(c=>c.Id!=id))return new(false,"该选择已不可用");
        ResidentResult result=choice.Action switch
        {
            "wait"=>new(true,"继续原有生活与工作安排"),
            "feed"=>BeginFoodSupply(w,Workers.First(o=>o.Id==choice.Target))?new(true,"已安排送饭，送达后才记为完成"):new(false,"送饭条件已变化"),
            "treat" or "research"=>BeginResidentActivity(w,choice.Action,choice.Target,origin:"work"),
            "social" or "relax"=>BeginResidentActivity(w,choice.Action,choice.Target,origin:"personality"),
            _=>new(false,"不支持的选择")
        };
        if(result.Ok)w.Person.UsedMemories(evidence,SimulationTime);
        return result;
    }
    public void RestoreResidentMind(ResidentMindState? state)
    {
        if(state is null){ResidentMind=new();return;}
        static bool Text(string? s,int length)=>s is not null&&s.Length<=length;
        static bool Time(double t)=>double.IsFinite(t)&&t>=0;
        if(state.IntervalHours is <1 or >24||state.Cursor is <0 or >1000||!Time(state.NextAt)||!Text(state.Error,512)||state.History is null||state.History.Count>24||
            state.History.Any(h=>h is null||!Text(h.Id,100)||!Text(h.Resident,100)||!Time(h.Time)||h.Time>SimulationTime||!Text(h.Choice,150)||!Text(h.Reason,256)||!Text(h.Result,512)||h.Memories is null||h.Memories.Length>8||h.Memories.Any(m=>!Text(m,100))))
            throw new InvalidDataException("居民决策存档无效。");
        if(state.Turn is {} t)
        {
            if(!Guid.TryParseExact(t.Id,"N",out _)||!Text(t.Resident,100)||!Text(t.Stamp,64)||!Text(t.Model,128)||t.PromptVersion!=1||!Time(t.ObservedAt)||t.ObservedAt>SimulationTime||
                t.Status is not ("requesting" or "received" or "completed" or "waiting")||!Text(t.Observation,20000)||!Text(t.Response,4096)||!Text(t.Receipt,1024)||
                t.MemoryIds is null||t.MemoryIds.Length>8||t.MemoryIds.Any(m=>!Text(m,100))||t.Choices is null||t.Choices.Count>20||t.Choices.Any(c=>c is null||!Text(c.Id,150)||!Text(c.Target,100)||!Text(c.Label,200)||c.Action is not ("feed" or "treat" or "research" or "social" or "relax" or "wait")||!double.IsFinite(c.Preference))||
                t.Status=="received"&&t.Response==""||t.Status=="completed"&&t.Receipt=="")throw new InvalidDataException("居民 AI 会话无效。");
            if(t.Receipt!="")t.Status="completed";
            else if(t.Status=="requesting"){t.Status="waiting";state.Error="读档中断了居民请求，可重新决策；不会自动重发。";}
        }
        ResidentMind=state;
    }
}
