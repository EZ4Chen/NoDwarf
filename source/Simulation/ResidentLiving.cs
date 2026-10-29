namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private bool NeedsFeeding(DemoWorker w)=>!w.Person.Dead&&w.Person.Hunger<40&&(w.Person.Incapacitated||w.Person.MedicalBed is not null||w.Person.Mobility<.05&&!Inventory(w).Any(i=>i.Item=="meal"));
    private bool FoodCareBusy(string patient,DemoWorker except)=>RescueReserved(patient)||Workers.Any(w=>w!=except&&w.Person.Activity is {Kind:"food_supply" or "feed"} a&&a.Target==patient);
    private bool MakeMealRoom(DemoWorker w)
    {
        if(w.Job is not null)Release(w.Job);
        RefreshWeight(w);
        foreach(var item in Inventory(w).Where(i=>i.Slot==""&&i.Item!="meal").OrderByDescending(i=>ResidentGear.Weight(i.Item)*i.Count).ToArray())
        {
            if(w.Person.CarriedWeight+ResidentGear.Weight("meal")<=w.Person.Capacity)break;
            item.Owner="";item.Cell=w.Tile;RefreshWeight(w);Revision++;
        }
        return w.Person.CarriedWeight+ResidentGear.Weight("meal")<=w.Person.Capacity;
    }
    private bool BeginFoodSupply(DemoWorker helper,DemoWorker patient)
    {
        var p=helper.Person;
        if(p.Dead||p.Incapacitated||MilitaryControlled(helper)||FoodCareBusy(patient.Id,helper))return false;
        var available=Items.Where(s=>s.Item=="meal"&&s.Owner==""&&s.Count>Reserved(s.Id)).ToArray();
        if(available.Length==0){p.FoodIssue=Items.Any(s=>s.Item=="meal"&&s.Owner=="")?"公共口粮已被预约":"没有可用公共口粮，需要烹饪";return false;}
        var targets=available.SelectMany(s=>ItemPositions(s.Cell)).Where(c=>Walkable(c)&&Navigation.CanReach(c,helper.Tile)&&
            (helper==patient||CanReachAny(c,ItemPositions(patient.Tile).Where(Walkable).ToArray())));
        var route=Navigation.FindNearest(helper.Tile,targets,out var endpoint);
        if(route is null||route.Count>0&&p.Mobility<.05){p.FoodIssue="口粮或照料对象不可达，请检查通路";return false;}
        if(!MakeMealRoom(helper)){p.FoodIssue="装备负重过高，无法携带口粮";return false;}
        var meal=available.First(s=>ItemPositions(s.Cell).Contains(endpoint));
        var research=p.Activity?.Kind=="research"?p.Activity:p.PausedResearch;
        CancelResidentActivity(helper);
        p.Activity=new(){Kind="food_supply",Origin=helper==patient?"need":"work",Target=patient.Id,ItemId=meal.Id,Destination=endpoint};
        p.PausedResearch=research;p.FoodIssue="";
        foreach(var c in route)helper.Path.Enqueue(c);helper.CheckedNavigationVersion=Navigation.Version;
        helper.Status=helper==patient?"取一份口粮":"取口粮 → "+patient.Name;return true;
    }
    private bool TryAssignFeeding(DemoWorker helper,int ordinaryRank)
    {
        if(ResidentWork.Rank(helper.Person,"care")>=ordinaryRank||helper.Person.Hunger<25||helper.Person.Rest<10||_simulationTime<helper.NextCareCheck)return false;
        helper.NextCareCheck=_simulationTime+3;
        foreach(var patient in Workers.Where(w=>w!=helper&&NeedsFeeding(w)&&!FoodCareBusy(w.Id,helper)).OrderBy(w=>w.Person.Hunger).ThenByDescending(w=>helper.Person.Affinity(w.Id)).ThenBy(w=>w.Id))
            if(BeginFoodSupply(helper,patient))return true;
        return false;
    }
    private bool ValidFoodActivity(DemoWorker w,ResidentActivity a)
    {
        var patient=Workers.FirstOrDefault(o=>o.Id==a.Target&&!o.Person.Dead);
        if(patient is null||patient.Person.Hunger>=(patient==w?95:70)||patient!=w&&!NeedsFeeding(patient))return false;
        if(a.Kind=="food_supply")return Items.Any(i=>i.Id==a.ItemId&&i.Item=="meal"&&i.Owner==""&&i.Count>0&&ItemPositions(i.Cell).Contains(a.Destination));
        return Items.Any(i=>i.Id==a.ItemId&&i.Item=="meal"&&i.Owner==w.Id&&i.Count>0)&&ItemPositions(patient.Tile).Contains(a.Destination);
    }
    private bool CompleteFoodSupply(DemoWorker w,ResidentActivity a)
    {
        var source=Items.FirstOrDefault(i=>i.Id==a.ItemId&&i.Item=="meal"&&i.Owner=="");
        var patient=Workers.FirstOrDefault(o=>o.Id==a.Target&&!o.Person.Dead);
        if(source is null||patient is null||!ItemPositions(source.Cell).Contains(w.Tile)||!MakeMealRoom(w))
        {CancelResidentActivity(w);w.Person.FoodIssue="取粮中断，请检查物资和负重";return true;}
        source.Count--;if(source.Count==0)Items.Remove(source);
        var carried=new ItemStack{Id=++_nextItem,Item="meal",Count=1,Owner=w.Id,Cell=w.Tile,Properties=source.Properties};Items.Add(carried);RefreshWeight(w);Revision++;
        var research=w.Person.PausedResearch;CancelResidentActivity(w);
        if(w==patient)
        {BeginResidentActivity(w,"eat",origin:"need");w.Person.PausedResearch=research;return true;}
        var path=Navigation.FindNearest(w.Tile,ItemPositions(patient.Tile).Where(c=>Walkable(c)&&Navigation.CanReach(c,w.Tile)),out var destination);
        if(path is null){w.Person.FoodIssue="送饭断路，口粮保留在物品栏";return true;}
        w.Person.Activity=new(){Kind="feed",Origin="work",Target=patient.Id,ItemId=carried.Id,Destination=destination};
        foreach(var c in path)w.Path.Enqueue(c);w.CheckedNavigationVersion=Navigation.Version;w.Status="送饭 → "+patient.Name;return true;
    }
    private bool CompleteFeeding(DemoWorker helper,ResidentActivity a)
    {
        var patient=Workers.FirstOrDefault(w=>w.Id==a.Target&&!w.Person.Dead);
        var food=Inventory(helper).FirstOrDefault(i=>i.Id==a.ItemId&&i.Item=="meal");
        if(patient is not null&&food is not null&&patient.Person.Hunger<70&&ItemPositions(patient.Tile).Contains(helper.Tile))
        {
            food.Count--;if(food.Count==0)Items.Remove(food);RefreshWeight(helper);Revision++;
            patient.Person.Hunger=Math.Min(100,patient.Person.Hunger+45);patient.Person.FoodIssue="";
            patient.Person.Feeling("fed","得到同伴送饭照料",4,_simulationTime+180);
            patient.Person.Remember("fed:"+helper.Id,helper.Name+"送来了口粮。",_simulationTime,target:helper.Id,actor:helper.Id,importance:85);
            helper.Person.Remember("helped:"+patient.Id,"给"+patient.Name+"送达了一份口粮。",_simulationTime,target:patient.Id,actor:helper.Id,importance:75);
        }
        CancelResidentActivity(helper);return true;
    }
    private void ValidateHomeBed(DemoWorker w)
    {
        if(w.Person.HomeBed is Cell bed&&(!Buildings.TryGetValue(bed,out var b)||b.Kind!=BuildKind.Bed||IsMedicalBed(bed)||w.Person.Dead))w.Person.HomeBed=null;
    }
    private double RelaxRate(DemoWorker w)
    {
        var fire=Directions.Select(d=>Buildings.GetValueOrDefault(w.Tile+d)).FirstOrDefault(b=>b is not null&&b.Kind is BuildKind.Campfire or BuildKind.GameTable&&BedKey(b.Cell)==w.Person.Activity?.Target&&ItemPositions(b.Cell).Contains(w.Tile));
        if(fire is null||w.Person.Isolated)return .45;
        if(fire.Kind==BuildKind.GameTable){w.Status="在棋桌旁休闲";w.Person.Feeling("leisure","棋桌娱乐",4,_simulationTime+120);return 1.5;}
        bool burning=FireFuel.GetValueOrDefault(fire.Cell)>0;
        bool company=Workers.Any(o=>o!=w&&!o.Person.Dead&&o.Person.Activity is {Kind:"relax"} a&&a.Target==BedKey(fire.Cell)&&o.Path.Count==0);
        w.Status=burning?"篝火旁休闲":"营火处休闲（未点燃）";
        if(!w.Person.MoodEffects.Any(e=>e.Key=="leisure"&&e.Until>_simulationTime+90))w.Person.Feeling("leisure",company?"与同伴围坐休闲":"有休闲去处",company?4:2,_simulationTime+120);
        return burning?(company?1.5:1.2):.7;
    }
    private void RestComfort(DemoWorker w)
    {
        if(w.Person.HomeBed is not Cell bed||w.Person.Activity?.Target!=BedKey(bed)||!ItemPositions(bed).Contains(w.Tile))return;
        if(w.Person.MoodEffects.Any(e=>e.Key=="bedroom"&&e.Until>_simulationTime+150))return;
        var room=RoomAt(bed);
        w.Status=room.Enclosed?"在自己的室内床休息":"在自己的露天床休息";
        w.Person.Feeling("bedroom",room.Enclosed?"卧室休息 · "+room.Grade:"有床可睡",room.Enclosed?2+(int)Math.Min(4,room.Quality/25):1,_simulationTime+180);
    }
}
