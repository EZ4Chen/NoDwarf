using System.Text.Json;
namespace NoDwarf.Simulation;

public sealed record DialogueLine(string Speaker,string Text);
public sealed class DialogueRecord
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string[] Participants {get;set;}=[];
    public double Time {get;set;}
    public string Location {get;set;}="";
    public List<DialogueLine> Lines {get;set;}=new();
    public string Summary {get;set;}="";
    public bool IsLongTerm {get;set;}
}
public sealed class DialogueTurn
{
    public string Kind {get;set;}="dialogue";
    public string Status {get;set;}="ready";
    public DialogueRecord Record {get;set;}=new();
    public string Prompt {get;set;}="";
    public string Response {get;set;}="";
}
public sealed class DialogueState
{
    public List<DialogueRecord> Records {get;set;}=new();
    public List<string[]> Encounters {get;set;}=new();
    public DialogueTurn? Turn {get;set;}
    public double NextAt {get;set;}
    public int Cursor {get;set;}
    public string Error {get;set;}="";
}
public sealed partial class DemoWorld
{
    public DialogueState Dialogues {get;private set;}=new();
    public DialogueOptions DialogueSettings {get;set;}=new();
    public void QueueConversation(DemoWorker a,DemoWorker b)
    {
        if(!DialogueSettings.Enabled||Dialogues.Encounters.Count>=8||Dialogues.Encounters.Any(p=>p.Contains(a.Id)&&p.Contains(b.Id)))return;
        Dialogues.Encounters.Add([a.Id,b.Id]);
    }
    public object[] RecallDialogue(string resident,string partner)
    {
        var settings=DialogueSettings;
        var candidates=Dialogues.Records.Where(r=>r.Participants.Contains(resident)&&(settings.RecallHours==0||SimulationTime-r.Time<=settings.RecallHours*StoryHour))
            .OrderByDescending(r=>r.Participants.Contains(partner)).ThenByDescending(r=>r.Time).Take(settings.RecallRounds).ToArray();
        var detailed=candidates.Where(r=>!r.IsLongTerm).OrderByDescending(r=>r.Time).Take(settings.DetailedRounds).Select(r=>r.Id).ToHashSet();
        return candidates.Select(r=>(object)new{r.Id,r.Time,r.Location,participants=r.Participants,
            source="conversation; speech is not proof of world facts",summary=r.Summary,
            lines=detailed.Contains(r.Id)?r.Lines.ToArray():Array.Empty<DialogueLine>(),
            excerpt=!detailed.Contains(r.Id)&&r.Summary==""?string.Join(" / ",r.Lines.Select(l=>l.Speaker+": "+l.Text))[..Math.Min(240,string.Join(" / ",r.Lines.Select(l=>l.Speaker+": "+l.Text)).Length)]:""}).ToArray();
    }
    private bool CanTalk(DemoWorker w)=>!w.Person.Dead&&!w.Person.Incapacitated&&!w.Person.Isolated&&!MilitaryControlled(w)&&!FloodThreat(w)&&w.Person.Can("social");
    public bool ConversationValid(DialogueRecord record)
    {
        var a=Workers.FirstOrDefault(w=>w.Id==record.Participants[0]);var b=Workers.FirstOrDefault(w=>w.Id==record.Participants[1]);
        return a is not null&&b is not null&&CanTalk(a)&&CanTalk(b)&&Math.Abs(a.Tile.X-b.Tile.X)+Math.Abs(a.Tile.Z-b.Tile.Z)+Math.Abs(a.Level-b.Level)<=8&&SimulationTime-record.Time<=2*StoryHour;
    }
    public DialogueTurn? BeginConversation()
    {
        if(!DialogueSettings.Enabled||SimulationTime<Dialogues.NextAt)return null;
        string[]? pair=null;
        while(Dialogues.Encounters.Count>0)
        {
            var candidate=Dialogues.Encounters[0];Dialogues.Encounters.RemoveAt(0);
            if(ConversationValid(new(){Participants=candidate,Time=SimulationTime})){pair=candidate;break;}
        }
        var available=Workers.Where(w=>CanTalk(w)&&w.Job is null&&w.Path.Count==0&&w.Person.Activity is null&&ScheduledMode(w)=="anything"&&w.Person.Hunger>=40&&w.Person.Thirst>=40&&w.Person.Rest>=30).ToArray();
        for(int i=0;pair is null&&i<available.Length;i++)
        {
            var a=available[(Dialogues.Cursor+i)%available.Length];
            var b=available.Where(b=>b!=a&&ConversationValid(new(){Participants=[a.Id,b.Id],Time=SimulationTime}))
                .Where(b=>!Dialogues.Records.Any(r=>r.Participants.Contains(a.Id)&&r.Participants.Contains(b.Id)&&SimulationTime-r.Time<DialogueSettings.IntervalHours*StoryHour))
                .OrderByDescending(b=>a.Person.Affinity(b.Id)).FirstOrDefault();
            if(b is not null){pair=[a.Id,b.Id];Dialogues.Cursor=(Dialogues.Cursor+i+1)%available.Length;}
        }
        Dialogues.NextAt=SimulationTime+(pair is null?1:DialogueSettings.IntervalHours)*StoryHour;
        if(pair is null)return null;
        var worker=Workers.First(w=>w.Id==pair[0]);
        var turn=new DialogueTurn{Record=new(){Participants=pair,Time=SimulationTime,Location=$"{(RoomAt(worker.Tile).Enclosed?"室内":"室外")} ({worker.Tile.X}, {worker.Tile.Z}, {worker.Level})"}};
        Dialogues.Turn=turn;Dialogues.Error="";return turn;
    }
    public string ConversationPrompt(DialogueTurn turn)
    {
        var r=turn.Record;string speaker=r.Participants[r.Lines.Count%2],partner=r.Participants[(r.Lines.Count+1)%2];
        var w=Workers.First(w=>w.Id==speaker);
        return JsonSerializer.Serialize(new{speaker,name=w.Name,partner,partner_name=Workers.First(w=>w.Id==partner).Name,
            context=MindContext(w,[partner]),needs=new{w.Person.Hunger,w.Person.Thirst,w.Person.Rest,w.Person.Mood},
            r.Location,conversation=r.Lines,closing=r.Lines.Count==3},ResidentObservationJsonOptions);
    }
    public void FinishConversation(DialogueTurn turn)
    {
        if(Dialogues.Records.All(r=>r.Id!=turn.Record.Id))
        {
            Dialogues.Records.Add(turn.Record);
            Log(string.Join("  ",turn.Record.Lines.Select(l=>(Workers.FirstOrDefault(w=>w.Id==l.Speaker)?.Name??l.Speaker)+"：“"+l.Text+"”")));
            TrimConversations();Revision++;
        }
        turn.Status="completed";
    }
    public void TrimConversations()
    {
        var recent=Dialogues.Records.AsEnumerable().Reverse().OrderByDescending(r=>r.Time).Take(DialogueSettings.ShortTermRounds).Select(r=>r.Id).ToHashSet();
        foreach(var r in Dialogues.Records)
        {
            r.IsLongTerm=!recent.Contains(r.Id);
            if(r.IsLongTerm&&r.Summary!="")r.Lines.Clear();
        }
        int cap=DialogueSettings.ShortTermRounds+DialogueSettings.LongTermRounds;
        if(Dialogues.Records.Count>cap)Dialogues.Records.RemoveRange(0,Dialogues.Records.Count-cap);
    }
    public void RestoreDialogues(DialogueState? state)
    {
        static bool Text(string? s,int max)=>s is not null&&s.Length<=max;
        bool Record(DialogueRecord? r)=>r is not null&&Guid.TryParseExact(r.Id,"N",out _)&&double.IsFinite(r.Time)&&r.Time>=0&&r.Time<=SimulationTime&&Text(r.Location,100)&&
            r.Participants is {Length:2}&&r.Participants.Distinct().Count()==2&&r.Participants.All(p=>!string.IsNullOrEmpty(p)&&p.Length<=100)&&Text(r.Summary,400)&&r.Lines is not null&&r.Lines.Count<=4&&r.Lines.Select((l,i)=>l is not null&&l.Speaker==r.Participants[i%2]&&!string.IsNullOrWhiteSpace(l.Text)&&l.Text.Length<=160).All(x=>x);
        if(state is null||state.Records is null||state.Records.Count>224||state.Records.Any(r=>!Record(r)||r.Summary==""&&r.Lines.Count!=4)||state.Records.Select(r=>r.Id).Distinct().Count()!=state.Records.Count||state.Encounters is null||state.Encounters.Count>8||state.Encounters.Any(p=>p is not {Length:2}||p.Any(s=>string.IsNullOrEmpty(s)||s.Length>100)||p[0]==p[1])||!double.IsFinite(state.NextAt)||state.NextAt<0||state.Cursor is <0 or >1000||!Text(state.Error,512))throw new InvalidDataException("对话记忆存档无效。");
        if(state.Turn is {} t)
        {
            if(!Record(t.Record)||t.Kind is not ("dialogue" or "summary")||t.Status is not ("ready" or "requesting" or "received" or "waiting" or "completed")||!Text(t.Prompt,24000)||!Text(t.Response,4096)||t.Status=="received"&&t.Response=="")throw new InvalidDataException("对话请求存档无效。");
            if(t.Status=="requesting"){t.Status="waiting";state.Error="读档中断了对话请求，请手动重试。";}
        }
        Dialogues=state;
    }
}
