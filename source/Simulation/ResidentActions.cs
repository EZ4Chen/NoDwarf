using System.Text.Json;
namespace NoDwarf.Simulation;

public sealed record GearDefinition(string Item,string Slot,double Weight,int Protection,string Skill="",double Bonus=0,bool TwoHanded=false);
public static class ResidentGear
{
    public static readonly GearDefinition[] All = [
        new("shirt","body",2,8),new("trousers","legs",1,5),new("boots","feet",2,8),new("gloves","hands",1,8),
        new("cap","head",1,8),new("pickaxe","main",4,0,"mining",.15,true),new("hammer","main",2,0,"smithing",.15),
        new("sword","main",3,0,"combat",.15),new("shield","off",4,15)];
    public static GearDefinition? Get(string item)=>All.FirstOrDefault(g=>g.Item==item);
    public static double Weight(string item)=>Get(item)?.Weight??(item is "meal" or "bandage"?.25:1);
    public static string SlotForPart(string part)=>part switch
    {"head"=>"head","torso"=>"body","left_hand" or "right_hand"=>"hands","left_foot" or "right_foot"=>"feet",_=>part.EndsWith("leg")?"legs":"body"};
}
public sealed record ResidentResult(bool Ok,string Message);
// Compact local command contract shared by UI, fixed routes and future LLM callers.
// An AI caller must provide both versions; stale plans do not mutate the world.
public sealed record ResidentCommand(string Resident,string Action,string Target="",int ItemId=0,long? ExpectedVersion=null,long? ExpectedResearch=null,
    int Hour=-1,int Priority=-1,string[]? Hours=null);
public sealed partial class DemoWorld
{
    public ColonyResearch Research {get;private set;}=new();
    private double _residentElapsed;
    private int _foodSearchCursor;
    public double SimulationTime=>_simulationTime;
    public IEnumerable<ItemStack> Inventory(DemoWorker worker)=>Items.Where(s=>s.Owner==worker.Id);
    public IEnumerable<ItemStack> NearbyItems(DemoWorker worker)=>Items.Where(s=>s.Owner==""&&s.Count>Reserved(s.Id)&&Math.Abs(s.Cell.X-worker.Tile.X)+Math.Abs(s.Cell.Z-worker.Tile.Z)+Math.Abs(s.Cell.Level-worker.Level)<=16);
    public string TalentIssue(DemoWorker worker,TalentNode node)
    {
        var p=worker.Person;
        if(p.Dead)return "居民已死亡";
        if(p.Talents.GetValueOrDefault(node.Skill)>=node.Rank)return "已学习";
        if(p.Talents.GetValueOrDefault(node.Skill)!=node.Rank-1)return "需要前一级专长";
        if(p.Level(node.Skill)<node.Level)return $"需要{ResidentCatalog.Skill(node.Skill).Name} {node.Level} 级";
        if(node.Research!=""&&!Research.Completed.Contains(node.Research))return "需要研究："+ResidentCatalog.Research.First(r=>r.Id==node.Research).Name;
        return p.Points<node.Cost?$"需要 {node.Cost} 技能点":"";
    }
    private ResidentResult BuyTalent(DemoWorker worker,string id)
    {
        var node=ResidentCatalog.Talents.FirstOrDefault(n=>n.Id==id);if(node is null)return new(false,"未知技能节点");
        string issue=TalentIssue(worker,node);if(issue!="")return new(false,issue);
        var p=worker.Person;p.SpentPoints+=node.Cost;p.Talents[node.Skill]=node.Rank;p.DecisionVersion++;
        p.Remember("talent:"+node.Skill,"学会了"+node.Name,_simulationTime);return new(true,"学会了"+node.Name);
    }
    private void TryAutoTalents(DemoWorker worker)
    {
        if(worker.Person.AutoSkill=="")return;
        foreach(var n in ResidentCatalog.Talents.Where(n=>n.Skill==worker.Person.AutoSkill))
            if(TalentIssue(worker,n)=="")BuyTalent(worker,n.Id);
    }
    private void AwardWork(DemoWorker worker,string skill,int amount)
    {
        if(!Workers.Contains(worker))return; // Creative completion never creates character XP.
        worker.Person.Earn(skill,amount);TryAutoTalents(worker);
    }
    private string JobSkill(DemoJob job)=>job.Processing?(ProcessingCatalog.Find(job.Recipe)?.Station switch {BuildKind.Stove=>"cooking",BuildKind.Anvil=>"smithing",_=>"woodwork"}):job.Farming||job.PlantTree?"growing":job.Chop?"woodwork":job.Demolish?"construction":job.Cook?"cooking":job.Dig?"mining":job.Smelt?"smelting":job.Forge?"smithing":
        job.Build is BuildKind.Log or BuildKind.Planks or BuildKind.PlankStairs or BuildKind.LogStairs?"woodwork":"construction";
    private double GearWorkBonus(DemoWorker worker,string skill)=>worker.Person.GearBonuses.GetValueOrDefault(skill);
    private void RefreshWeight(DemoWorker worker,IEnumerable<ItemStack>? owned=null)
    {
        var items=(owned??Inventory(worker)).ToArray();
        worker.Person.CarriedWeight=items.Sum(s=>ResidentGear.Weight(s.Item)*s.Count)+
            (worker.Job?.Materials.Where(r=>r.Stage==1).Sum(r=>ResidentGear.Weight(r.Item)*r.Count)??0);
        worker.Person.GearBonuses.Clear();
        foreach(var item in items.Where(s=>s.Slot!=""))
            if(ResidentGear.Get(item.Item) is {Skill:not ""} gear&&
                (!gear.TwoHanded||worker.Person.Integrity("left_hand")>=.2&&worker.Person.Integrity("right_hand")>=.2))
                worker.Person.GearBonuses[gear.Skill]=worker.Person.GearBonuses.GetValueOrDefault(gear.Skill)+gear.Bonus;
    }
    private void SeedResidentItems()
    {
        foreach(var worker in Workers)
        {
            foreach(var (item,slot,count) in new[]{("shirt","body",1),("trousers","legs",1),("boots","feet",1),("meal","",3),("bandage","",1)})
                Items.Add(new(){Id=++_nextItem,Item=item,Count=count,Cell=worker.Tile,Owner=worker.Id,Slot=slot});
            RefreshWeight(worker);
        }
        Stockpiles.Add(Camp+new Cell(4,8));
        foreach(var item in new[]{"pickaxe","hammer","cap","gloves","shield"})SpawnItem(item,1,Camp+new Cell(4,8));
        SpawnItem("meal",18,Camp+new Cell(4,8));SpawnItem("bandage",6,Camp+new Cell(4,8));
    }
    public ResidentResult ActResident(ResidentCommand c,bool ai=false)
    {
        if(c is null||string.IsNullOrWhiteSpace(c.Resident)||c.Resident.Length>100||string.IsNullOrWhiteSpace(c.Action)||c.Action.Length>40||c.Target is null||c.Target.Length>100)
            return new(false,"人物命令格式无效");
        var w=Workers.FirstOrDefault(w=>w.Id==c.Resident);if(w is null)return new(false,"居民不存在");
        var p=w.Person;
        if(ai&&(c.ExpectedVersion is null||c.ExpectedResearch is null))return new(false,"AI 操作需要人物和研究版本");
        if(c.ExpectedVersion is long v&&v!=p.DecisionVersion||c.ExpectedResearch is long r&&r!=Research.Version)return new(false,"状态已变化，请重新查看");
        if(p.Dead)return new(false,"居民已死亡");
        ResidentResult result;
        switch(c.Action)
        {
            case "schedule":result=SetResidentSchedule(w,c);break;
            case "work_priority":result=SetResidentPriority(w,c);break;
            case "talent":return BuyTalent(w,c.Target);
            case "route":
                if(c.Target!=""&&!ResidentCatalog.Skills.Any(s=>s.Id==c.Target))return new(false,"未知培养方向");
                p.AutoSkill=c.Target;p.DecisionVersion++;TryAutoTalents(w);return new(true,c.Target==""?"改为手动加点":"已启用固定培养路线（无模型调用）");
            case "equip":result=EquipResident(w,c.ItemId);break;
            case "unequip":
                var worn=Inventory(w).FirstOrDefault(s=>s.Id==c.ItemId&&s.Slot!="");if(worn is null)return new(false,"没有穿戴这件装备");
                worn.Slot="";result=new(true,"已卸下装备");break;
            case "drop":
                var owned=Inventory(w).FirstOrDefault(s=>s.Id==c.ItemId);if(owned is null)return new(false,"物品不在物品栏");
                owned.Owner="";owned.Slot="";owned.Cell=w.Tile;result=new(true,"已放到脚下");SettleItems();break;
            case "cancel":
                if(p.Activity is null&&p.PausedResearch is null)return new(false,"没有个人活动，当前工作保持不变");
                p.PausedResearch=null;if(p.Activity is not null)CancelResidentActivity(w);result=new(true,"已恢复普通工作");break;
            case "research_topic":
                var topic=ResidentCatalog.Research.FirstOrDefault(r=>r.Id==c.Target);
                if(topic is null||Research.Completed.Contains(topic.Id)||topic.Prerequisites.Any(r=>!Research.Completed.Contains(r)))return new(false,"研究已完成或前置未满足");
                Research.Active=topic.Id;Research.Version++;result=new(true,"当前研究："+topic.Name);break;
            case "rest":case "relax":case "research":case "pickup":case "eat":case "treat":case "social":
                return BeginResidentActivity(w,c.Action,c.Target,c.ItemId);
            default:return new(false,"不支持的人物操作");
        }
        if(result.Ok){p.DecisionVersion++;RefreshWeight(w);Revision++;}return result;
    }
    private ResidentResult EquipResident(DemoWorker worker,int id)
    {
        var item=Inventory(worker).FirstOrDefault(s=>s.Id==id);var gear=item is null?null:ResidentGear.Get(item.Item);
        if(item is null||gear is null||item.Count!=1)return new(false,"只能装备物品栏中的单件装备");
        if(item.Slot!="")return new(false,"已经装备");
        if(worker.Person.Manipulation<.05)return new(false,"双手无法操作");
        if(gear.TwoHanded&&(worker.Person.Integrity("left_hand")<.2||worker.Person.Integrity("right_hand")<.2))return new(false,"双手工具需要两只可用的手");
        foreach(var old in Inventory(worker).Where(s=>s.Slot==gear.Slot||gear.TwoHanded&&s.Slot=="off"||gear.Slot=="off"&&s.Slot=="main"&&ResidentGear.Get(s.Item)?.TwoHanded==true))old.Slot="";
        item.Slot=gear.Slot;return new(true,"已装备"+ItemCatalog.Get(item.Item).Name);
    }
    private ResidentResult BeginResidentActivity(DemoWorker w,string kind,string target="",int itemId=0,string origin="manual")
    {
        var p=w.Person;if(p.Incapacitated)return new(false,"居民失去行动能力");
        if(kind is "research" or "treat"&&p.Manipulation<.05)return new(false,"双手无法操作，不能进行这项工作");
        Cell destination=w.Tile;Queue<Cell>? path=new();
        if(kind=="rest"&&!p.Incapacitated)
        {
            var beds=Buildings.Values.Where(b=>b.Kind==BuildKind.Bed&&!Workers.Any(o=>o!=w&&o.Person.Activity?.Target==BedKey(b.Cell))).ToArray();
            var candidates=beds.SelectMany(b=>Directions.Select(d=>b.Cell+d)).Where(Walkable);
            var route=Navigation.FindNearest(w.Tile,candidates,out var bedside);
            if(route is not null){path=route;destination=bedside;target=BedKey(beds.First(b=>Directions.Any(d=>b.Cell+d==bedside)).Cell);}
        }
        if(kind=="research"&&Research.Active=="")return new(false,"先选择一个研究项目");
        if(kind=="eat"&&!Inventory(w).Any(s=>s.Item=="meal"))return new(false,"物品栏没有口粮，请先拾取");
        if(kind=="eat"&&p.Hunger>=95)return new(false,"现在不饿");
        if(kind=="pickup")
        {
            var item=Items.FirstOrDefault(s=>s.Id==itemId&&s.Owner==""&&s.Count>Reserved(s.Id));
            if(item is null)return new(false,"物品不存在或已被预约");
            RefreshWeight(w);if(p.CarriedWeight+ResidentGear.Weight(item.Item)>p.Capacity)return new(false,"负重不足");
            path=Navigation.FindNearest(w.Tile,ItemPositions(item.Cell).Where(Walkable),out destination);
        }
        if(kind is "treat" or "social")
        {
            var other=Workers.FirstOrDefault(o=>o.Id==target);if(other is null||other.Person.Dead)return new(false,"目标居民不存在或已死亡");
            if(kind=="treat"&&(!other.Person.Injuries.Any(i=>!i.Treated&&i.Kind!="missing")||!Inventory(w).Any(i=>i.Item=="bandage")))return new(false,"需要未处理的伤口和随身绷带");
            if(kind=="social"&&(other==w||other.Person.Incapacitated||other.Job is not null||other.Path.Count>0||other.Person.Activity is not null))return new(false,"请选择空闲且清醒的另一位居民");
            if(kind=="social"&&p.Relations.Count>=32&&!p.Relations.Any(r=>r.Target==target))return new(false,"关系记录已满");
            if(kind=="social"&&p.Relations.Any(r=>r.Target==target&&_simulationTime-r.LastSocial<120))return new(false,"刚交流过，稍后再聊");
            path=Navigation.FindNearest(w.Tile,ItemPositions(other.Tile).Where(Walkable),out destination);
        }
        if(path is null)return new(false,"没有可达路线");
        if(path.Count>0&&p.Mobility<.05)return new(false,"居民无法移动到目标位置");
        if(w.Job is not null)Release(w.Job);else StopMovement(w);
        foreach(var c in path)w.Path.Enqueue(c);w.CheckedNavigationVersion=Navigation.Version;
        p.PausedResearch=null;p.Activity=new(){Kind=kind,Origin=origin,Target=target,ItemId=itemId,Destination=destination};p.DecisionVersion++;
        w.Status=ActivityName(kind);return new(true,w.Status);
    }
    public static string ActivityName(string kind)=>kind switch
    {"rest"=>"休息","relax"=>"放松","research"=>"研究","pickup"=>"拾取物品","eat"=>"进食","treat"=>"包扎护理","social"=>"交流",_=>"等待"};
    private void CancelResidentActivity(DemoWorker worker)
    {worker.Person.Activity=null;StopMovement(worker);worker.Status="等待工作";worker.Person.DecisionVersion++;}
    private bool ConsumePersonal(DemoWorker worker,string item)
    {
        var s=Inventory(worker).FirstOrDefault(s=>s.Item==item);if(s is null)return false;
        s.Count--;if(s.Count==0)Items.Remove(s);RefreshWeight(worker);Revision++;return true;
    }
    private bool UpdateResidentActivity(DemoWorker w,double dt,ref int budget)
    {
        using var timing = Probe?.Measure("residents.activity") ?? default;
        var p=w.Person;
        if(p.Dead||p.Incapacitated)
        {
            if(w.Job is not null)Release(w.Job);
            if(p.Activity is not null)CancelResidentActivity(w);if(p.Dead)p.PausedResearch=null;StopMovement(w);
            w.Status=p.Dead?"已死亡":"无法行动 · 需要照料";return true;
        }
        // Do not hold a production reservation forever when work capacity becomes zero.
        if(w.Job is not null&&(p.Manipulation<.05||p.Mobility<.05))Release(w.Job);
        var a=p.Activity;
        if(a is null&&(p.Manipulation<.05||p.Mobility<.05))
        {
            if(p.Mobility>=.05&&w.Path.Count>0)return false;
            StopMovement(w);w.Status="无法工作 · 需要照料";return true;
        }
        if(a is null)return false;
        if(a.Kind is "research" or "treat"&&p.Manipulation<.05||w.Path.Count>0&&p.Mobility<.05)
        {CancelResidentActivity(w);w.Status="活动能力不足";return true;}
        if(w.Path.Count>0)
        {
            if(!EnsureWorkerRoute(w,ref budget))
            {if(w.Path.Count==0){CancelResidentActivity(w);w.Status="活动路线受阻";}return true;}
            if(w.Path.Count>0)Move(w,dt);return true;
        }
        bool Near(Cell c)=>Math.Abs(w.Tile.X-c.X)+Math.Abs(w.Tile.Z-c.Z)<=1&&Math.Abs(w.Level-c.Level)<=1;
        var target=Workers.FirstOrDefault(o=>o.Id==a.Target);
        if(a.Kind is "treat" or "social"&&(target is null||target.Person.Dead||!Near(target.Tile)))
        {CancelResidentActivity(w);w.Status="对象已离开，请重新安排";return true;}
        w.Status=ActivityName(a.Kind);
        if(a.Kind=="rest") {p.Rest=Math.Min(100,p.Rest+dt*RestRate(w));if(p.Rest>=99)CancelResidentActivity(w);return true;}
        if(a.Kind=="relax") {p.Recreation=Math.Min(100,p.Recreation+dt);if(p.Recreation>=99)CancelResidentActivity(w);return true;}
        if(a.Kind=="research")
        {
            if(Research.Active==""){CancelResidentActivity(w);return true;}
            var topic=ResidentCatalog.Research.First(r=>r.Id==Research.Active);
            double gained=Math.Min(topic.Cost-Research.Progress.GetValueOrDefault(topic.Id),dt*p.WorkRate("research"));
            if(gained<=0)return true;
            Research.Progress[topic.Id]=Research.Progress.GetValueOrDefault(topic.Id)+gained;
            // Award fixed chunks of actual progress, not elapsed idle time or repeated cancellation.
            a.Progress+=gained;
            while(a.Progress>=1){a.Progress-=1;p.ResearchWork++;AwardWork(w,"research",4);}
            if(Research.Progress[topic.Id]>=topic.Cost-0.00001)
            {
                Research.Completed.Add(topic.Id);Research.Active="";Research.Version++;
                foreach(var other in Workers){TryAutoTalents(other);other.Person.DecisionVersion++;}
                p.Remember("research:"+topic.Id,"完成研究："+topic.Name,_simulationTime);Log("研究完成："+topic.Name);CancelResidentActivity(w);
            }
            return true;
        }
        a.Progress+=dt*(a.Kind=="treat"?p.WorkRate("medicine"):a.Kind=="social"?p.WorkRate("social"):1);
        if(a.Progress<(a.Kind is "treat" or "social"?8:1))return true;
        switch(a.Kind)
        {
            case "pickup":
                var stack=Items.FirstOrDefault(s=>s.Id==a.ItemId&&s.Owner=="");RefreshWeight(w);
                if(stack is not null&&Near(stack.Cell))
                {
                    int count=Math.Min(ResidentGear.Get(stack.Item) is null?5:1,Math.Min(stack.Count-Reserved(stack.Id),(int)Math.Floor((p.Capacity-p.CarriedWeight)/ResidentGear.Weight(stack.Item))));
                    if(count>0)
                    {
                        stack.Count-=count;Items.Add(new(){Id=++_nextItem,Item=stack.Item,Count=count,Cell=w.Tile,Owner=w.Id});
                        if(stack.Count==0)Items.Remove(stack);RefreshWeight(w);Revision++;
                    }
                }
                break;
            case "eat":
                if(p.Hunger<95&&ConsumePersonal(w,"meal")){p.Hunger=Math.Min(100,p.Hunger+45);p.Feeling("meal","吃了口粮",4,_simulationTime+180);}break;
            case "treat":
                if(target is not null&&target.Person.Injuries.Any(i=>!i.Treated&&i.Kind!="missing")&&ConsumePersonal(w,"bandage"))
                {
                    var injury=target.Person.Injuries.First(i=>!i.Treated&&i.Kind!="missing");injury.Treated=true;AwardWork(w,"medicine",40);
                    target.Person.Remember("care:"+w.Id,$"{w.Name}处理了{ResidentCatalog.PartName(injury.Part)}的伤势。",_simulationTime);
                    if(target!=w)ChangeOpinion(target,w,8,"照料");target.Person.DecisionVersion++;
                }break;
            case "social":
                if(target is not null&&!target.Person.Incapacitated&&target.Job is null&&target.Person.Activity is null&&target.Path.Count==0&&
                    !p.Relations.Any(r=>r.Target==target.Id&&_simulationTime-r.LastSocial<120))
                {
                    ChangeOpinion(w,target,3,"交流");ChangeOpinion(target,w,3,"交流");
                    p.Recreation=Math.Min(100,p.Recreation+(p.Traits.Contains("sociable")?30:20));target.Person.Recreation=Math.Min(100,target.Person.Recreation+20);
                    p.Feeling("social","愉快的交流",5,_simulationTime+240);target.Person.Feeling("social","愉快的交流",5,_simulationTime+240);AwardWork(w,"social",25);
                }break;
        }
        CancelResidentActivity(w);return true;
    }
    private void ChangeOpinion(DemoWorker from,DemoWorker to,int delta,string reason)
    {
        var r=from.Person.Relations.FirstOrDefault(r=>r.Target==to.Id);
        if(r is null){if(from.Person.Relations.Count>=32)return;r=new(){Target=to.Id};from.Person.Relations.Add(r);}
        r.Opinion=Math.Clamp(r.Opinion+delta,-100,100);if(reason=="交流")r.LastSocial=_simulationTime;
        from.Person.Remember(reason+":"+to.Id,$"与{to.Name}：{reason}，好感 {delta:+0;-0}。",_simulationTime);from.Person.DecisionVersion++;
    }
    private void UpdateResidentNeeds(double elapsed)
    {
        using var timing = Probe?.Measure("residents.needs") ?? default;
        var ownedItems=Items.Where(i=>i.Owner!="").ToLookup(i=>i.Owner);
        int foodSearchBudget=4; // Up to four food requests per 2s batch, rotating fairly across residents.
        int first=_foodSearchCursor;
        _foodSearchCursor=(_foodSearchCursor+4)%Math.Max(1,Workers.Count);
        for(int offset=0;offset<Workers.Count;offset++)
        {
            var w=Workers[(first+offset)%Workers.Count];
            var p=w.Person;if(p.Dead)
            {
                if(!p.Memories.Any(m=>m.Key=="death")){p.Remember("death","因严重伤势死亡。",_simulationTime);Log(w.Name+"因严重伤势死亡。");}
                continue;
            }
            p.Hunger=Math.Max(0,p.Hunger-elapsed*.035);p.Rest=Math.Max(0,p.Rest-elapsed*.025);p.Recreation=Math.Max(0,p.Recreation-elapsed*.02);
            p.MoodEffects.RemoveAll(e=>e.Until<=_simulationTime);
            double bleeding=p.Injuries.Where(i=>i.Kind=="cut"&&!i.Treated).Sum(i=>i.Severity)*.001;
            p.Blood=Math.Clamp(p.Blood+(bleeding>0?-bleeding:.015)*elapsed,0,100);
            foreach(var i in p.Injuries.Where(i=>i.Kind!="missing"))
                i.Severity=Math.Max(0,i.Severity-elapsed*(i.Treated?.035:i.Kind=="fracture"?0:.002)*(p.Traits.Contains("resilient")?1.2:1));
            p.Injuries.RemoveAll(i=>i.Severity<=0);RefreshWeight(w,ownedItems[w.Id]);
            if(p.Dead){p.Remember("death","因严重伤势死亡。",_simulationTime);Log(w.Name+"因严重伤势死亡。");}
            // Self-care is deterministic and cheap. No API requests, no full-history scans.
            if((p.Activity is null||p.Activity.Kind is "research" or "rest" or "relax")&&!p.Incapacitated)
            {
                var research=p.Activity?.Kind=="research"?p.Activity:p.PausedResearch;
                bool BeginNeed(string kind)
                {
                    bool ok=BeginResidentActivity(w,kind,origin:"need").Ok;
                    if(ok)p.PausedResearch=research;
                    return ok;
                }
                bool food=false;
                if(p.Hunger<25)
                {
                    if(ownedItems[w.Id].Any(i=>i.Item=="meal"))food=BeginNeed("eat");
                    else if(foodSearchBudget>0){foodSearchBudget--;food=TryFindFood(w);if(food)p.PausedResearch=research;}
                }
                if(food)continue;
                bool workPeriod=ScheduledMode(w)=="work";
                if(p.Rest<(workPeriod?5:15)&&p.Activity?.Kind!="rest"){if(BeginNeed("rest"))continue;}
                else if(!workPeriod&&p.Recreation<15&&p.Activity?.Kind is not ("rest" or "relax")){if(BeginNeed("relax"))continue;}
                if(p.Activity is null&&p.PausedResearch is not null)
                {
                    if(Research.Active=="")p.PausedResearch=null;
                    else if(p.PausedResearch.Origin!="work"&&ScheduledMode(w) is "work" or "anything"&&p.Hunger>=25&&p.Rest>=15&&(workPeriod||p.Recreation>=15))
                    {
                        double fraction=p.PausedResearch.Progress;
                        if(BeginResidentActivity(w,"research").Ok)p.Activity!.Progress=fraction;
                    }
                }
            }
        }
    }
    private bool TryFindFood(DemoWorker w)
    {
        if(Inventory(w).Any(s=>s.Item=="meal"))return BeginResidentActivity(w,"eat",origin:"need").Ok;
        var available=Items.Where(s=>s.Item=="meal"&&s.Owner==""&&s.Count>Reserved(s.Id)).ToArray();
        if(available.Length==0)return false;
        var targets=available.SelectMany(s=>ItemPositions(s.Cell)).Where(Walkable);
        var route=Navigation.FindNearest(w.Tile,targets,out var endpoint);
        if(route is null)return false;
        var meal=available.First(s=>ItemPositions(s.Cell).Contains(endpoint));
        return BeginResidentActivity(w,"pickup",itemId:meal.Id,origin:"need").Ok;
    }
    public ResidentResult InjureResident(string id,string part,string kind,double severity)
    {
        var w=Workers.FirstOrDefault(w=>w.Id==id);
        if(w is null||!ResidentCatalog.Parts.Any(p=>p.Id==part)||kind is not ("cut" or "burn" or "fracture" or "missing")||!double.IsFinite(severity)||severity<=0||severity>100)return new(false,"伤势参数无效");
        if(w.Person.Injuries.Count>=40||w.Person.Dead)return new(false,"无法增加伤势");
        double armor=Inventory(w).Where(s=>s.Slot==ResidentGear.SlotForPart(part)||s.Slot=="off").Sum(s=>ResidentGear.Get(s.Item)?.Protection??0);
        double damage=kind=="missing"?100:severity*(1-Math.Min(.8,armor/100));
        w.Person.Injuries.Add(new(){Part=part,Kind=kind,Severity=damage});w.Person.DecisionVersion++;RefreshWeight(w);
        w.Person.Remember("injury:"+part,$"{ResidentCatalog.PartName(part)}受到{ResidentCatalog.InjuryName(kind)}。",_simulationTime);
        return new(true,"伤势已记录");
    }
    public string ResidentDecisionBatch(int limit=10)
    {
        // Callers opt in. This function never contacts a model. Only affordable actions are sent.
        var candidates=Workers.Where(w=>w.Person.Points>0&&!w.Person.Dead&&w.Person.AutoSkill=="")
            .Select(w=>new{resident=w.Id,version=w.Person.DecisionVersion,researchVersion=Research.Version,points=w.Person.Points,
                traits=w.Person.Traits,choices=ResidentCatalog.Talents.Where(n=>TalentIssue(w,n)=="").Select(n=>new{id=n.Id,cost=n.Cost,effect=n.Effect}).ToArray()})
            .Where(w=>w.choices.Length>0).Take(Math.Clamp(limit,0,10));
        return JsonSerializer.Serialize(candidates,ResidentObservationJsonOptions);
    }
}
