namespace NoDwarf.Simulation;

// A bounded, factual archive. No model-generated statement becomes a world event.
public sealed class ResidentMemorySummary
{
    public string Kind {get;set;}="";
    public string Target {get;set;}="";
    public int Count {get;set;}
    public double From {get;set;}
    public double Until {get;set;}
    public List<ResidentMemory> Evidence {get;set;}=new();
    public string Text => $"{Kind}：累计 {Count} 次；最近记录：{Evidence.LastOrDefault()?.Text}";
}
public sealed record ResidentPersona(int Sociability,int Curiosity,int Duty,int Caution,string Voice,string Preference);

public sealed partial class ResidentProfile
{
    public ResidentPersona Personality()=>new(Traits.Contains("sociable")?80:35,Traits.Contains("studious")?85:35,
        Traits.Contains("diligent")?85:50,Traits.Contains("calm")?70:45,
        Traits.Contains("sociable")?"主动、亲切，简短直接":Traits.Contains("calm")?"平静、克制，简短具体":"朴实、简短，用具体经历说话",
        Traits.Contains("studious")?"研究":Traits.Contains("sociable")?"与熟悉的人交流":Traits.Contains("diligent")?"完成工作":"适时休息");

    public void Remember(string key,string text,double time,string target="",string actor="",int importance=0,string eventId="")
    {
        string id=eventId==""?Guid.NewGuid().ToString("N"):eventId;
        if(Memories.Any(m=>m.EventId==id)||MemorySummaries.Any(s=>s.Evidence.Any(m=>m.EventId==id)))return;
        var prior=Memories.FirstOrDefault(m=>m.Key==key);
        string kind=key.Split(':')[0];
        if(target==""&&kind is "care" or "照料" or "交流" or "food" or "fed" or "helped")target=key.Contains(':')?key[(key.IndexOf(':')+1)..]:"";
        int weight=importance>0?importance:kind is "care" or "照料" or "food" or "fed"?85:kind is "death" or "injury"?90:kind is "talent" or "research"?75:kind=="交流"?20:30;
        var memory=new ResidentMemory(key,text,time){EventId=id,Kind=kind,Target=target,Actor=actor,Importance=weight,
            Perspective=kind=="helped"?"actor":kind is "care" or "照料" or "food" or "fed"?"recipient":"self",
            Count=Math.Min(1000000,(prior?.Count??0)+1),FirstTime=prior?.FirstTime??time};
        if(prior is not null)Memories.Remove(prior);
        Memories.Insert(0,memory);
        if(kind=="helped"&&target!="")
        {
            HelpedAt[target]=time;
            if(HelpedAt.Count>32)HelpedAt.Remove(HelpedAt.MinBy(p=>p.Value).Key);
        }
        while(Memories.Count>24)
        {
            // Keep six strongest milestones even after a long run of routine events.
            var protectedIds=Memories.Where(m=>m.Importance>=75).OrderByDescending(m=>m.Importance).ThenByDescending(m=>m.Time).Take(6).Select(m=>m.EventId).ToHashSet();
            var drop=Memories.Where(m=>!protectedIds.Contains(m.EventId)).OrderBy(m=>m.Importance).ThenBy(m=>m.Time).First();
            Memories.Remove(drop);ArchiveMemory(drop);
        }
        DecisionVersion++;
    }
    private void ArchiveMemory(ResidentMemory memory)
    {
        var summary=MemorySummaries.FirstOrDefault(s=>s.Kind==memory.Kind&&s.Target==memory.Target);
        if(summary is null){summary=new(){Kind=memory.Kind,Target=memory.Target,From=memory.FirstTime};MemorySummaries.Add(summary);}
        summary.Count=Math.Min(1000000,summary.Count+memory.Count);summary.From=Math.Min(summary.From,memory.FirstTime);summary.Until=Math.Max(summary.Until,memory.Time);
        summary.Evidence.Add(memory);summary.Evidence=summary.Evidence.OrderBy(m=>m.Time).TakeLast(3).ToList();
        if(MemorySummaries.Count>8)MemorySummaries.Remove(MemorySummaries.OrderBy(s=>s.Until).First());
    }
    public ResidentMemory[] Recall(double now,IEnumerable<string> targets,int limit=6)
    {
        var ids=targets.ToHashSet();
        return Memories.Concat(MemorySummaries.SelectMany(s=>s.Evidence)).GroupBy(m=>m.EventId).Select(g=>g.First())
            .OrderByDescending(m=>(ids.Contains(m.Target)?100:0)+m.Importance/(1+Math.Max(0,now-m.Time)/(GameCalendar.SecondsPerDay*8))-(m.LastUsed>0&&now-m.LastUsed<120?15:0))
            .ThenByDescending(m=>m.Time).Take(Math.Clamp(limit,0,8)).ToArray();
    }
    public double Gratitude(string target)
    {
        double helped=HelpedAt.GetValueOrDefault(target,-1);
        return Memories.Concat(MemorySummaries.SelectMany(s=>s.Evidence)).Any(m=>m.Target==target&&m.Kind is "care" or "照料" or "food" or "fed"&&m.Time>helped)?25:0;
    }
    public double Affinity(string target)=>(Relations.FirstOrDefault(r=>r.Target==target)?.Opinion??0)*.2+Gratitude(target);
    public void UsedMemories(IEnumerable<string> ids,double now)
    {
        var used=ids.ToHashSet();
        for(int i=0;i<Memories.Count;i++)if(used.Contains(Memories[i].EventId))Memories[i]=Memories[i] with{LastUsed=now};
    }
    public void MigrateMemories()
    {
        // Old dossiers have no structured source. Do not infer actors from prose.
        for(int i=0;i<Memories.Count;i++)
        {
            var m=Memories[i];if(m is null||m.Key is null)continue;
            if(m.EventId=="")
            {
                string kind=m.Key.Split(':')[0];bool related=kind is "care" or "照料" or "交流" or "food" or "fed";
                Memories[i]=m with{EventId=Guid.NewGuid().ToString("N"),Kind=kind,FirstTime=m.Time,Source="legacy",
                    Target=related&&m.Key.Contains(':')?m.Key[(m.Key.IndexOf(':')+1)..]:"",Importance=kind is "care" or "照料" or "food" or "fed"?85:30};
            }
        }
    }
    private void ValidateMemory()
    {
        static bool Text(string? s,int n)=>s is not null&&s.Length<=n;
        static bool Time(double t)=>double.IsFinite(t)&&t>=0;
        static bool Entry(ResidentMemory? m)=>m is not null&&Text(m.EventId,100)&&Text(m.Key,100)&&Text(m.Text,400)&&Text(m.Kind,100)&&Text(m.Target,100)&&Text(m.Actor,100)&&
            m.Source is "simulation" or "legacy"&&m.Perspective is "self" or "actor" or "recipient"&&
            m.Importance is >=0 and <=100&&m.Count is >=1 and <=1000000&&Time(m.Time)&&Time(m.FirstTime)&&m.FirstTime<=m.Time&&Time(m.LastUsed);
        if(HelpedAt is null||HelpedAt.Count>32||HelpedAt.Any(p=>!Text(p.Key,100)||!Time(p.Value))||
            Memories is null||Memories.Any(m=>!Entry(m))||Memories.Where(m=>m.EventId!="").GroupBy(m=>m.EventId).Any(g=>g.Count()>1)||MemorySummaries is null||MemorySummaries.Count>8||
            MemorySummaries.Any(s=>s is null||!Text(s.Kind,100)||!Text(s.Target,100)||s.Count is <1 or >1000000||!Time(s.From)||!Time(s.Until)||s.From>s.Until||s.Evidence is null||s.Evidence.Count is <1 or >3||s.Evidence.Any(m=>!Entry(m))))
            throw new InvalidDataException("居民记忆无效。");
    }
}
