namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private bool LifeReserved(string patient)=>Workers.Any(w=>w.Person.Activity is {Kind:"life_supply" or "life_use" or "water_fill"} a&&a.Target==patient);
    public ResidentResult SupplyCare(string helperId,string patientId,string supply,string part="",string origin="manual")
    {
        var helper=Workers.FirstOrDefault(w=>w.Id==helperId);var patient=Workers.FirstOrDefault(w=>w.Id==patientId);
        if(helper is null||patient is null||helper.Person.Dead||patient.Person.Dead||helper.Person.Incapacitated||helper.Person.Manipulation<.05||MilitaryControlled(helper))return new(false,"需要能够照料的居民");
        if(helper.WaterEvacuating||FloodThreat(helper))return new(false,"照料者需要先撤离水患");
        if(supply is not ("water_bucket" or "splint" or "prosthesis"))return new(false,"未知用品");
        if(supply=="water_bucket"&&patient.Person.Thirst>=95)return new(false,"暂不口渴");
        if(supply=="splint")part=patient.Person.Injuries.FirstOrDefault(i=>i.Kind=="fracture"&&!i.Fixed&&i.Severity>i.PermanentSeverity)?.Part??"";
        if(supply=="splint"&&part=="")return new(false,"没有待固定的骨折");
        if(supply=="prosthesis"&&(part is "head" or "torso"||!patient.Person.Injuries.Any(i=>i.Kind=="missing"&&i.Part==part&&i.Treated)||patient.Person.Prostheses.ContainsKey(part)))return new(false,"请选择已止血且尚无义肢的缺失肢体");
        if(LifeReserved(patientId)||RescueReserved(patientId)||Workers.Any(w=>w.Person.Activity is {Kind:"treat" or "care_supply"} a&&a.Target==patientId))return new(false,"该居民正在接受照料或搬运");
        var carried=Inventory(helper).FirstOrDefault(s=>s.Item==supply);
        string item=supply;
        if(carried is null&&supply=="water_bucket"&&Available(supply)==0){item="bucket";carried=Inventory(helper).FirstOrDefault(s=>s.Item==item);}
        if(carried is not null)
        {
            if(helper.Job is not null)Release(helper.Job);CancelResidentActivity(helper);
            helper.Person.Activity=new(){Kind="life_use",Target=patientId,Supply=item,BodyPart=part,ItemId=carried.Id,Origin=origin,Destination=helper.Tile};
            return RouteLifeUse(helper)?new(true,"正在送用品"):new(false,"水源或照料对象不可达，用品仍保留");
        }
        var stacks=Items.Where(s=>s.Owner==""&&s.Item==item&&s.Count>Reserved(s.Id)).ToArray();
        if(stacks.Length==0)return new(false,"缺少"+ItemCatalog.Get(item).Name);
        var route=Navigation.FindNearest(helper.Tile,stacks.SelectMany(s=>ItemPositions(s.Cell)).Where(c=>Walkable(c)&&Navigation.CanReach(c,helper.Tile)),out var dest);
        if(route is null)return new(false,"用品不可达");
        RefreshWeight(helper);if(helper.Person.CarriedWeight+ResidentGear.Weight(item)>helper.Person.Capacity)return new(false,"负重不足，请先卸货");
        var stack=stacks.First(s=>ItemPositions(s.Cell).Contains(dest));
        if(helper.Job is not null)Release(helper.Job);CancelResidentActivity(helper);
        helper.Person.Activity=new(){Kind="life_supply",Target=patientId,Supply=item,BodyPart=part,ItemId=stack.Id,Destination=dest,Origin=origin};
        foreach(var c in route)helper.Path.Enqueue(c);helper.CheckedNavigationVersion=Navigation.Version;
        return new(true,"取用"+ItemCatalog.Get(item).Name);
    }
    private bool RouteLifeUse(DemoWorker helper)
    {
        var a=helper.Person.Activity!;Queue<Cell>? path;
        if(a.Supply=="bucket")
        {
            var sources=WaterPoints.Where(c=>Fluids.Amount(c)>=5&&Fluids.Cells[c].Temperature is >=0 and <=40).ToArray();
            path=Navigation.FindNearest(helper.Tile,sources.SelectMany(c=>Directions.Select(d=>c+d)).Where(c=>Walkable(c)&&Fluids.Depth(c)<.2&&Navigation.CanReach(c,helper.Tile)),out var dest);
            if(path is not null){a.Kind="water_fill";a.SourceCell=sources.First(c=>Directions.Any(d=>c+d==dest));a.Destination=dest;}
        }
        else
        {
            var patient=Workers.FirstOrDefault(w=>w.Id==a.Target&&!w.Person.Dead);
            path=patient is null?null:Navigation.FindNearest(helper.Tile,ItemPositions(patient.Tile).Where(c=>Walkable(c)&&Navigation.CanReach(c,helper.Tile)),out _);
            if(path is not null){a.Kind="life_use";a.Destination=path.Count>0?path.Last():helper.Tile;}
        }
        if(path is null){CancelResidentActivity(helper);return false;}
        StopMovement(helper);foreach(var c in path)helper.Path.Enqueue(c);helper.CheckedNavigationVersion=Navigation.Version;a.Progress=0;return true;
    }
    private bool UpdateLifeCare(DemoWorker helper,double dt,ref int budget)
    {
        var a=helper.Person.Activity!;var patient=Workers.FirstOrDefault(w=>w.Id==a.Target&&!w.Person.Dead);
        var stack=Items.FirstOrDefault(s=>s.Id==a.ItemId&&s.Item==a.Supply&&(a.Kind=="life_supply"?s.Owner=="":s.Owner==helper.Id));
        if(patient is null||stack is null||helper.Person.Manipulation<.05){CancelResidentActivity(helper);return true;}
        helper.Status=a.Kind=="water_fill"?"在安全岸边装水":a.Supply=="splint"?"骨折固定":a.Supply=="prosthesis"?"安装木制义肢":"取水／送水照料";
        if(helper.Path.Count>0){if(!EnsureWorkerRoute(helper,ref budget)){if(helper.Path.Count==0)CancelResidentActivity(helper);return true;}Move(helper,dt);return true;}
        if(helper.Tile!=a.Destination){CancelResidentActivity(helper);return true;}
        a.Progress+=dt;if(a.Progress<(a.Kind=="life_use"&&a.Supply!="water_bucket"?8:2))return true;
        if(a.Kind=="life_supply")
        {
            if(!ItemPositions(stack.Cell).Contains(helper.Tile)||helper.Person.CarriedWeight+ResidentGear.Weight(stack.Item)>helper.Person.Capacity){CancelResidentActivity(helper);return true;}
            stack.Count--;if(stack.Count==0)Items.Remove(stack);
            var carried=new ItemStack{Id=++_nextItem,Item=stack.Item,Count=1,Owner=helper.Id,Cell=helper.Tile,Properties=stack.Properties};Items.Add(carried);a.ItemId=carried.Id;RefreshWeight(helper);RouteLifeUse(helper);return true;
        }
        if(a.Kind=="water_fill")
        {
            if(Fluids.Depth(helper.Tile)>=.2||a.SourceCell is not Cell source||!WaterPoints.Contains(source)||!Directions.Any(d=>source+d==helper.Tile)||!Fluids.Cells.TryGetValue(source,out var fluid)||fluid.Temperature is <0 or >40||fluid.Amount<5){CancelResidentActivity(helper);return true;}
            // Five litres from the finite fluid simulation fill exactly one physical bucket.
            Fluids.Withdraw(source,5);stack.Count--;if(stack.Count==0)Items.Remove(stack);
            var filled=new ItemStack{Id=++_nextItem,Item="water_bucket",Count=1,Owner=helper.Id,Cell=helper.Tile};Items.Add(filled);a.ItemId=filled.Id;a.Supply=filled.Item;RefreshWeight(helper);RouteLifeUse(helper);Revision++;return true;
        }
        if(!ItemPositions(patient.Tile).Contains(helper.Tile)){CancelResidentActivity(helper);return true;}
        bool used=false;
        if(a.Supply=="water_bucket"&&patient.Person.Thirst<95){patient.Person.Thirst=Math.Min(100,patient.Person.Thirst+60);SpawnItem("bucket",1,helper.Tile);used=true;}
        if(a.Supply=="splint"&&patient.Person.Injuries.FirstOrDefault(i=>i.Kind=="fracture"&&i.Part==a.BodyPart&&!i.Fixed) is {} wound){wound.Fixed=true;wound.Treated=true;wound.Treatment=new(){Quality=HealthCare.Quality(helper.Person),Remaining=1200};used=true;}
        if(a.Supply=="prosthesis"&&!patient.Person.Prostheses.ContainsKey(a.BodyPart)&&patient.Person.Injuries.Any(i=>i.Kind=="missing"&&i.Part==a.BodyPart&&i.Treated))
        {patient.Person.Prostheses[a.BodyPart]=.5;used=true;}
        if(used){stack.Count--;if(stack.Count==0)Items.Remove(stack);RefreshWeight(helper);Revision++;}
        CancelResidentActivity(helper);return true;
    }
    private int _lifeCursor;
    private void AssignLifeNeeds()
    {
        int requests=0;var ordered=Workers.Skip(_lifeCursor).Concat(Workers.Take(_lifeCursor)).ToArray();_lifeCursor=(_lifeCursor+4)%Math.Max(1,Workers.Count);
        foreach(var patient in ordered.Where(w=>!w.WaterEvacuating&&!FloodThreat(w)&&!w.Person.Dead&&!RescueReserved(w.Id)&&!LifeReserved(w.Id)))
        {
            if(requests>=4)break;var p=patient.Person;
            if(p.Thirst<30||p.ColdExposure>10||p.HeatExposure>10||p.Injuries.Any(i=>i.Kind=="fracture"&&!i.Fixed))requests++;
            if(p.ColdExposure>10||p.HeatExposure>10)SeekComfort(patient);
            if(p.Thirst<30)
            {
                var helpers=Workers.Where(h=>!h.WaterEvacuating&&!FloodThreat(h)&&!h.Person.Dead&&!h.Person.Incapacitated&&!MilitaryControlled(h)&&h.Person.Activity?.Kind is not ("treat" or "care_supply" or "rescue_carry" or "life_supply" or "life_use" or "water_fill")&&(h==patient&&p.MedicalBed is null&&p.Mobility>=.05||h!=patient&&h.Person.WorkPriorities.GetValueOrDefault("care")>0&&h.Job is null)).OrderBy(h=>h==patient?0:1);
                foreach(var h in helpers.Take(4))if(SupplyCare(h.Id,patient.Id,"water_bucket",origin:h==patient?"need":"work").Ok)break;
            }
            else if(p.Injuries.Any(i=>i.Kind=="fracture"&&!i.Fixed&&i.Severity>i.PermanentSeverity)&&Available("splint")>0)
            {
                foreach(var h in Workers.Where(h=>!h.WaterEvacuating&&!FloodThreat(h)&&!h.Person.Dead&&!h.Person.Incapacitated&&!MilitaryControlled(h)&&h.Person.WorkPriorities.GetValueOrDefault("doctor")>0&&h.Job is null&&h.Person.Activity is null))
                    if(SupplyCare(h.Id,patient.Id,"splint",origin:"work").Ok)break;
            }
        }
    }
}
public sealed partial class DemoWorld
{
    private IEnumerable<Cell> DiningSeats(DemoWorker w)=>Buildings.Values.Where(b=>b.Kind==BuildKind.Chair&&Directions.Any(d=>Buildings.TryGetValue(b.Cell+d,out var t)&&t.Kind==BuildKind.Table)).SelectMany(b=>ItemPositions(b.Cell)).Where(c=>Walkable(c)&&!Workers.Any(o=>o!=w&&o.Person.Activity is {Kind:"eat"} a&&a.Destination==c));
}
public sealed partial class DemoWorld
{
    private void SeekComfort(DemoWorker w)
    {
        if(w.Person.Incapacitated||MilitaryControlled(w)||w.Person.Mobility<.05||w.Person.MedicalBed is not null||w.Person.Activity?.Kind is "treat" or "care_supply" or "rescue_carry" or "life_supply" or "life_use" or "water_fill")return;
        if(TemperatureAt(w.Tile) is >=5 and <=35)return;
        var route=Navigation.FindNearest(w.Tile,Buildings.Values.Where(b=>b.Kind is BuildKind.Bed or BuildKind.Campfire).SelectMany(b=>ItemPositions(b.Cell)).Where(c=>Walkable(c)&&TemperatureAt(c) is >=5 and <=35&&Navigation.CanReach(c,w.Tile)),out var safe);
        if(route is null||route.Count==0)return;
        if(w.Job is not null)Release(w.Job);CancelResidentActivity(w);w.Person.Activity=new(){Kind="rest",Origin="need",Destination=safe};foreach(var c in route)w.Path.Enqueue(c);w.CheckedNavigationVersion=Navigation.Version;w.Status="前往舒适温度处休息";
    }
}
