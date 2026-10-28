namespace NoDwarf.Simulation;

public sealed record ItemDefinition(string Id,string Name,string Texture,string Color);
public static class ItemCatalog
{
    public static readonly ItemDefinition[] All={
        new("cobblestone","圆石","nodwarf:cobblestone","879087"),
        new("deep_rubble","深板岩碎石","nodwarf:deepslate","515563"),
        new("soil","土壤","nodwarf:soil","927158"),new("sand","沙子","nodwarf:sand","d7c18a"),
        new("raw_iron","粗铁","nodwarf:iron_ore","bb9476"),new("coal","煤","nodwarf:coal_ore","353942"),
        new("acacia_sapling","金合欢树苗","nodwarf:tree_acacia_log","8b9a53"),new("jungle_sapling","丛林树苗","nodwarf:tree_jungle_log","508e3a"),
        new("sapling","橡树苗","nodwarf:log","75a75b"),new("wood","木料","nodwarf:log","ab8050"),new("iron","铁锭","nodwarf:anvil","b5cbd0"),
        new("sword","铁剑","nodwarf:anvil","e7d49d"),
        new("shirt","布衣","nodwarf:planks","a9b69b"),new("trousers","布裤","nodwarf:planks","787b79"),
        new("boots","皮靴","nodwarf:log","876b50"),new("gloves","手套","nodwarf:planks","c3b393"),
        new("cap","皮帽","nodwarf:log","947b53"),new("pickaxe","矿镐","nodwarf:anvil","aab8c0"),
        new("hammer","锻锤","nodwarf:anvil","8e969c"),new("shield","木盾","nodwarf:log","b49a75"),
        new("axe","铁斧","nodwarf:anvil","aab8c0"),new("battleaxe","双刃战斧","nodwarf:anvil","aab8c0"),
        new("shovel","铁铲","nodwarf:anvil","aab8c0"),new("hoe","铁锄","nodwarf:anvil","aab8c0"),
        new("potato_seed","土豆种薯","nodwarf:soil","b48e60"),new("cotton_seed","棉花种子","nodwarf:soil","9b8f6d"),
        new("birch_sapling","桦树苗","nodwarf:log","c3c99f"),new("pine_sapling","松树苗","nodwarf:log","4d7756"),
        new("wheat","小麦","nodwarf:planks","d9bd69"),new("flour","面粉","nodwarf:planks","eee4be"),
        new("potato","土豆","nodwarf:soil","b58b59"),new("mushroom","蘑菇","nodwarf:soil","b28cc4"),
        new("cotton","棉絮","nodwarf:planks","eee9dc"),new("cloth","布","nodwarf:planks","d5c9b5"),
        new("grain_seed","小麦种子","nodwarf:planks","cbb675"),new("mushroom_spawn","蘑菇菌种","nodwarf:soil","b995c8"),new("raw_food","食材","nodwarf:planks","95ad60"),new("meal","口粮","nodwarf:planks","dfc48b"),new("bandage","绷带","nodwarf:planks","e9e2cd")};
    public static ItemDefinition Get(string id)=>All.FirstOrDefault(d=>d.Id==id)??throw new ArgumentException("Unknown item: "+id);
    public static string? Drop(string block)=>block switch {
        "nodwarf:iron_ore" or "nodwarf:deepslate_iron_ore"=>"raw_iron",
        "nodwarf:coal_ore" or "nodwarf:deepslate_coal_ore"=>"coal",
        "nodwarf:stone" or "nodwarf:stone_ramp" or "nodwarf:gravel" or "nodwarf:gravel_ramp" or
        "nodwarf:granite" or "nodwarf:granite_ramp" or "nodwarf:limestone" or "nodwarf:limestone_ramp" or "nodwarf:sandstone" or "nodwarf:sandstone_ramp"=>"cobblestone",
        "nodwarf:deepslate"=>"deep_rubble",
        "nodwarf:grass" or "nodwarf:soil" or "nodwarf:soil_ramp" or "nodwarf:humus" or "nodwarf:humus_ramp" or "nodwarf:dry_grass" or "nodwarf:dry_grass_ramp"=>"soil",
        "nodwarf:sand" or "nodwarf:sand_ramp"=>"sand",_=>null};
}
public sealed class ItemStack
{
    public ItemProperties Properties {get;set;}=new();
    [System.Text.Json.Serialization.JsonIgnore] public string EffectiveMaterial=>Properties.MaterialId!=""?Properties.MaterialId:EquipmentPhysicalCatalog.Find(Item)?.MaterialId??"";
    public int Id {get;set;}
    public string Item {get;set;}="cobblestone";
    public Cell Cell {get;set;}
    public int Count {get;set;}
    public string Owner {get;set;}="";
    public string Slot {get;set;}="";
}
public sealed class MaterialReservation
{
    public ItemProperties Properties {get;set;}=new();
    public int StackId {get;set;}
    public string Item {get;set;}="cobblestone";
    public int Count {get;set;}
    // 0: reserved at source, 1: physically carried by assigned worker, 2: delivered at worksite.
    public int Stage {get;set;}
}
public sealed partial class DemoWorld
{
    public List<ItemStack> Items {get;}=new();
    public HashSet<Cell> Stockpiles {get;}=new();
    private int _nextItem;
    public int Available(string item)=>Items.Where(s=>s.Item==item&&s.Owner=="").Sum(s=>Math.Max(0,s.Count-Reserved(s.Id)));
    public int Total(string item)=>Items.Where(s=>s.Item==item).Sum(s=>s.Count)+Jobs.SelectMany(j=>j.Materials).Where(r=>r.Stage==1&&r.Item==item).Sum(r=>r.Count);
    private int Reserved(int stack)
    {
        int total = Workers.Count(w=>w.Person.Activity is {Kind:"care_supply"} a&&a.ItemId==stack);
        foreach (var job in Jobs) foreach (var r in job.Materials)
            if (r.StackId == stack && r.Stage != 1) total += r.Count;
        return total;
    }
    public ItemStack SpawnItem(string item,int count,Cell cell,ItemProperties? properties=null)
    {
        ItemCatalog.Get(item);
        properties??=new();properties.Validate();
        if(count<=0||!Inside(cell))throw new ArgumentOutOfRangeException(nameof(count));
        var existing=Items.FirstOrDefault(s=>s.Item==item&&s.Cell==cell&&s.Owner==""&&Reserved(s.Id)==0&&s.Properties==properties);
        if(existing is not null){existing.Count+=count;Revision++;return existing;}
        var stack=new ItemStack{Id=++_nextItem,Item=item,Count=count,Cell=cell,Properties=properties};Items.Add(stack);Revision++;return stack;
    }
    private void SeedItems()
    {
        var c=Camp+new Cell(0,8);
        int offset=0;
        foreach(var (id,n) in new[]{("cobblestone",600),("wood",400),("iron",12),("coal",8)})SpawnItem(id,n,c+new Cell(offset++,0));
        foreach(string seed in new[]{"potato_seed","cotton_seed","birch_sapling","pine_sapling"})SpawnItem(seed,12,c+new Cell(5,0));
        SpawnItem("raw_food",30,c+new Cell(5,0));
        SpawnItem("grain_seed",12,c+new Cell(5,0));SpawnItem("mushroom_spawn",12,c+new Cell(5,0));
        for(int x=0;x<6;x++)Stockpiles.Add(c+new Cell(x,0));
    }
    public bool SetStockpile(Cell a,Cell b,bool remove=false)
    {
        if(a.Level!=b.Level||!Inside(a)||!Inside(b)||Math.Abs(a.X-b.X)>30||Math.Abs(a.Z-b.Z)>30)return false;
        for(int x=Math.Min(a.X,b.X);x<=Math.Max(a.X,b.X);x++)for(int z=Math.Min(a.Z,b.Z);z<=Math.Max(a.Z,b.Z);z++)
        {var c=new Cell(x,z,a.Level);if(remove)Stockpiles.Remove(c);else if(Walkable(c)&&!NaturalRamp(c)&&!Farms.ContainsKey(c))Stockpiles.Add(c);}
        foreach(var zone in StorageZones)zone.Cells.RemoveWhere(c=>!Stockpiles.Contains(c));
        StorageZones.RemoveAll(z=>z.Cells.Count==0);
        if(!remove)DefineStorageZone(Stockpiles.Where(c=>c.Level==a.Level&&c.X>=Math.Min(a.X,b.X)&&c.X<=Math.Max(a.X,b.X)&&c.Z>=Math.Min(a.Z,b.Z)&&c.Z<=Math.Max(a.Z,b.Z)));
        Revision++;return true;
    }
    private static Dictionary<string,int> BuildRecipe(BuildKind kind)
    {
        var c=BlockCatalog.Get(kind).Cost;var recipe=new Dictionary<string,int>();
        if(c.Stone>0)recipe["cobblestone"]=c.Stone;
        if(c.Wood>0)recipe["wood"]=c.Wood;
        if(c.Iron>0)recipe["iron"]=c.Iron;
        return recipe;
    }
    private bool Reserve(DemoJob job,Dictionary<string,int> recipe)
    {
        if(recipe.Any(p=>Available(p.Key)<p.Value))return false;
        foreach(var (item,amount) in recipe)
        {
            int left=amount;
            foreach(var s in Items.Where(s=>s.Item==item&&s.Owner=="").OrderBy(s=>Math.Abs(s.Cell.X-job.Cell.X)+Math.Abs(s.Cell.Z-job.Cell.Z)).ToArray())
            {
                int n=Math.Min(left,s.Count-Reserved(s.Id));if(n<=0)continue;
                job.Materials.Add(new(){StackId=s.Id,Item=item,Count=n});left-=n;if(left==0)break;
            }
        }
        return true;
    }
    private bool PrepareMaterials(DemoWorker worker,DemoJob job)
    {
        using var timing = Probe?.Measure("materials.prepare") ?? default;
        var access=JobAccess(job);
        if (!CanReachAny(worker.Tile, access)) return false;
        bool sourcesReachable = true;
        foreach (var reservation in job.Materials)
        {
            if (reservation.Stage != 0) continue;
            ItemStack? source = null;
            foreach (var stack in Items) if (stack.Id == reservation.StackId) { source = stack; break; }
            if (source is null || !CanSupplyFrom(worker.Tile,source.Cell,access)) { sourcesReachable = false; break; }
        }
        if (sourcesReachable) return true;
        if (job.Haul) return false;
        var pending=job.Materials.Where(r=>r.Stage==0).ToArray();
        // Replace source reservations atomically. Already carried/delivered stock stays put.
        var replacement=new List<MaterialReservation>();
        foreach(var group in pending.GroupBy(r=>r.Item))
        {
            int left=group.Sum(r=>r.Count);
            foreach(var stack in Items.Where(s=>s.Owner==""&&s.Item==group.Key)
                .OrderBy(s=>Math.Abs(s.Cell.X-job.Cell.X)+Math.Abs(s.Cell.Z-job.Cell.Z)+Math.Abs(s.Cell.Level-job.Cell.Level)))
            {
                int free=stack.Count-Reserved(stack.Id)+pending.Where(r=>r.StackId==stack.Id).Sum(r=>r.Count);
                if(free<=0||!CanSupplyFrom(worker.Tile,stack.Cell,access))continue;
                int n=Math.Min(left,free);replacement.Add(new(){StackId=stack.Id,Item=stack.Item,Count=n});left-=n;
                if(left==0)break;
            }
            if(left>0)return false;
        }
        job.Materials.RemoveAll(r=>r.Stage==0);job.Materials.AddRange(replacement);return true;
    }
    private bool CanSupplyFrom(Cell resident,Cell source,Cell[] destination)
    {
        foreach(var access in ItemAccess(source))
            if(Navigation.CanReach(resident,access)&&CanReachAny(access,destination))return true;
        return false;
    }
    public string OrderStatus(DemoJob job)
    {
        if(job.Worker>=0&&job.Worker<Workers.Count)
        {
            var next=job.Materials.FirstOrDefault(r=>r.Stage!=2);
            return next is null?$"加工中 · {job.Progress:0.0} 秒":next.Stage==1?"搬入"+ItemCatalog.Get(next.Item).Name:"取料："+ItemCatalog.Get(next.Item).Name;
        }
        if(!ProductionReady(job))return "等待：工坊未就绪";
        if(Workers.Count==0)return "等待：尚未启用居民";
        var enabled=Workers.Where(w=>ResidentWork.Rank(w.Person,ResidentWork.JobType(job))!=int.MaxValue).ToArray();
        if(enabled.Length==0)return "等待：无人启用这类工作";
        if(enabled.All(w=>ScheduledMode(w) is "sleep" or "recreation"))return "等待：居民处于休息或娱乐时段";
        var candidates=enabled.Where(w=>ActionPositions(job).Any(c=>Navigation.CanReach(w.Tile,c))).ToArray();
        if(candidates.Length==0)return "等待：工作位置不可达";
        if(!candidates.Any(w=>CanSupply(w,job)))return "等待：材料不可达或已被预订";
        if(Jobs.Any(j=>j!=job&&j.Cell==job.Cell&&j.Worker>=0))return "等待：设施使用中";
        return "等待：居民空闲";
    }
    private bool CanSupply(DemoWorker worker,DemoJob job)=>job.Materials.Where(r=>r.Stage==0).GroupBy(r=>r.Item).All(g=>
        Items.Where(s=>s.Owner==""&&s.Item==g.Key&&ItemPositions(s.Cell).Any(c=>Navigation.CanReach(worker.Tile,c)))
            .Sum(s=>Math.Max(0,s.Count-Reserved(s.Id)+g.Where(r=>r.StackId==s.Id).Sum(r=>r.Count)))>=g.Sum(r=>r.Count));

    private IEnumerable<Cell> ItemPositions(Cell c)
    {
        yield return c;
        foreach(var d in Directions)for(int y=-1;y<=1;y++)yield return c+d+new Cell(0,0,y);
    }
    private IEnumerable<Cell> WorkPositions(DemoJob job)
    {
        var next=job.Materials.FirstOrDefault(r=>r.Stage!=2);
        if(next is {Stage:0})
        {var stack=Items.FirstOrDefault(s=>s.Id==next.StackId);return stack is null?Enumerable.Empty<Cell>():ItemPositions(stack.Cell);}
        return job.Haul?ItemPositions(job.Cell):ActionPositions(job);
    }
    private bool RouteJob(DemoWorker worker,DemoJob job)
    {
        var route=Navigation.FindNearest(worker.Tile,WorkPositions(job).Where(c=>Walkable(c)&&Navigation.CanReach(c,worker.Tile)),out _);
        if(route is null){Release(job);worker.Status="搬运路线受阻";return false;}
        foreach(var c in route)worker.Path.Enqueue(c);worker.CheckedNavigationVersion=Navigation.Version;return true;
    }
    private bool AdvanceDelivery(DemoWorker worker,DemoJob job)
    {
        using var timing = Probe?.Measure("materials.advance-delivery") ?? default;
        var next=job.Materials.FirstOrDefault(r=>r.Stage!=2);
        if(next is null)return false;
        // A changed worksite/source requires a fresh route, even when the old path reached its end.
        if(!WorkPositions(job).Contains(worker.Tile)){RouteJob(worker,job);return true;}
        if(next.Stage==0)
        {
            var stack=Items.FirstOrDefault(s=>s.Id==next.StackId);
            if(stack is null||stack.Count<next.Count){Release(job);return true;}
            RefreshWeight(worker);
            int capacity=(int)Math.Floor((worker.Person.Capacity-worker.Person.CarriedWeight)/ResidentGear.Weight(next.Item));
            if(capacity<=0){Release(job);worker.Status="负重已满，请放下物品";return true;}
            if(next.Count>capacity)
            {
                int remainder=next.Count-capacity;next.Count=capacity;
                job.Materials.Insert(job.Materials.IndexOf(next)+1,new(){StackId=stack.Id,Item=next.Item,Count=remainder,Properties=stack.Properties});
            }
            next.Properties=stack.Properties;
            stack.Count-=next.Count;if(stack.Count==0)Items.Remove(stack);
            next.Stage=1;next.StackId=0;RefreshWeight(worker);Revision++;worker.Status="搬运"+ItemCatalog.Get(next.Item).Name;
            RouteJob(worker,job);return true;
        }
        // Install a ladder from carried wood: a rung is not a floor for staging materials.
        if(job.Build==BuildKind.Ladder&&next.Stage==1)return false;
        if(job.Haul&&StoragePriority(job.Cell,next.Item)==0){CancelJob(job);return true;}
        var placed=SpawnItem(next.Item,next.Count,job.Haul?job.Cell:worker.Tile,next.Properties);
        next.StackId=placed.Id;next.Stage=2;RefreshWeight(worker);Revision++;
        if(job.Haul){job.Materials.Clear();Release(job);Jobs.Remove(job);return true;}
        RouteJob(worker,job);return true;
    }
    private void DropCargo(DemoJob job,Cell cell)
    {
        foreach(var r in job.Materials.Where(r=>r.Stage==1))
        {var stack=SpawnItem(r.Item,r.Count,cell,r.Properties);r.StackId=stack.Id;r.Stage=0;}
    }
    private void ConsumeMaterials(DemoJob job)
    {
        foreach(var r in job.Materials)
        {
            if(r.Stage==1)continue;
            var stack=Items.FirstOrDefault(s=>s.Id==r.StackId)??throw new InvalidOperationException("Reserved stack missing");
            if(stack.Count<r.Count)throw new InvalidOperationException("Reserved stack insufficient");
            stack.Count-=r.Count;if(stack.Count==0)Items.Remove(stack);
        }
        job.Materials.Clear();Revision++;
    }
    public void CancelJob(DemoJob job)
    {Release(job);Jobs.Remove(job);Revision++;Log("任务取消，材料保留在实际位置。");}
    private void ScheduleHauling()
    {
        using var timing = Probe?.Measure("hauling.schedule") ?? default;
        // Reachability is shared by residents in the same connected component.
        // These snapshots live for this batch only; mutable reservations stay authoritative.
        var haulers=Workers.Where(w=>w.Person.WorkPriorities["hauling"]>0&&!w.Person.Dead&&!w.Person.Incapacitated&&w.Person.Mobility>=.05&&w.Person.Manipulation>=.05).ToArray();
        var representatives = haulers.GroupBy(w => Navigation.TravelRegionAt(w.Tile)).Where(g => g.Key != 0).Select(g => g.First()).ToArray();
        foreach(var stale in Jobs.Where(j=>j.Haul&&j.Worker<0&&(j.Materials.Any(r=>StoragePriority(j.Cell,r.Item)==0)||!representatives.Any(w=>PrepareMaterials(w,j)))).ToArray())
        {Jobs.Remove(stale);Revision++;}
        if(haulers.Length==0||Stockpiles.Count==0)return;
        if(Jobs.Count(j=>!j.Haul&&j.Worker>=0)>=Workers.Count)return;
        int hauling = Jobs.Count(j=>j.Haul);
        var used = Jobs.Where(j=>j.Haul).Select(j=>j.Cell).ToHashSet();
        var reserved = new Dictionary<int,int>();
        foreach(var job in Jobs) foreach(var r in job.Materials)
            if(r.Stage!=1) reserved[r.StackId]=reserved.GetValueOrDefault(r.StackId)+r.Count;
        var residentComponents = representatives.Select(w=>Navigation.ComponentAt(w.Tile)).ToHashSet();
        var stock = Items.GroupBy(i=>i.Cell).ToDictionary(g=>g.Key,g=>g.Select(i=>i.Item).ToHashSet());
        var destinations = Stockpiles.Where(Walkable).Select(c=>(Cell:c,Component:Navigation.ComponentAt(c))).ToArray();
        var sourceComponents = new HashSet<int>();
        foreach(var stack in Items)
        {
            if(hauling>=Workers.Count)return;
            int available=stack.Count-reserved.GetValueOrDefault(stack.Id);
            if(stack.Owner!=""||available<=0)continue;
            int currentPriority=StoragePriority(stack.Cell,stack.Item);
            sourceComponents.Clear();
            foreach(var access in ItemAccess(stack.Cell))
            {
                int component=Navigation.ComponentAt(access);
                if(residentComponents.Contains(component))sourceComponents.Add(component);
            }
            if(sourceComponents.Count==0)continue;
            Cell? destination=null; int bestRank=int.MaxValue,bestDistance=int.MaxValue,bestPriority=0;
            foreach(var candidate in destinations)
            {
                if(used.Contains(candidate.Cell)||!sourceComponents.Contains(candidate.Component))continue;
                if(!ItemAccess(stack.Cell).Any(a=>representatives.Any(w=>Navigation.CanReach(w.Tile,a))&&Navigation.CanReach(a,candidate.Cell)))continue;
                int priority=StoragePriority(candidate.Cell,stack.Item);
                if(priority<=currentPriority||priority<bestPriority)continue;
                int rank=stock.TryGetValue(candidate.Cell,out var items)?items.Contains(stack.Item)?0:2:1;
                int distance=Math.Abs(candidate.Cell.X-stack.Cell.X)+Math.Abs(candidate.Cell.Z-stack.Cell.Z);
                if(priority==bestPriority&&(rank>bestRank||(rank==bestRank&&distance>=bestDistance)))continue;
                destination=candidate.Cell;bestPriority=priority;bestRank=rank;bestDistance=distance;
            }
            if(destination is null)continue;
            var job=new DemoJob{Id=++_nextJob,Cell=destination.Value,Haul=true};
            int count=Math.Min(20,available);
            job.Materials.Add(new(){StackId=stack.Id,Item=stack.Item,Count=count});Jobs.Add(job);
            reserved[stack.Id]=reserved.GetValueOrDefault(stack.Id)+count;
            used.Add(destination.Value);hauling++;
        }
    }
    public bool QueueSmelt()
    {
        ValidateWorkshop();
        if(!WorkshopReady||_forgeFurnace is null){Log("需要就绪工坊中的熔炉。");return false;}
        if(Jobs.Count(j=>j.Smelt)>=3)return false;
        var job=new DemoJob{Id=++_nextJob,Cell=_forgeFurnace.Value,Smelt=true};
        if(!Reserve(job,new(){{"raw_iron",2},{"coal",1}})){Log("熔炼需要 2 粗铁和 1 煤。");return false;}
        Jobs.Add(job);Revision++;Log("熔炼订单：搬入 2 粗铁 + 1 煤，产出 2 铁锭。");return true;
    }
    public bool QueueCook()
    {
        ValidateWorkshop();
        var stove=Buildings.Values.FirstOrDefault(b=>KitchenReady(b.Cell));
        if(stove is null){Log("需要有围墙和门的厨房，并放置炊炉。");return false;}
        if(Jobs.Count(j=>j.Cook)>=3)return false;
        var job=new DemoJob{Id=++_nextJob,Cell=stove.Cell,Cook=true};
        if(!Reserve(job,new(){{"raw_food",2},{"wood",1}})){Log("烹饪需要 2 食材和 1 木料。");return false;}
        Jobs.Add(job);Revision++;Log("烹饪订单：2 食材 + 1 木料 → 2 口粮。");return true;
    }
    private bool ProductionReady(DemoJob job)=>job.Processing?ProcessingReady(job):job.Cook?KitchenReady(job.Cell):WorkshopReady&&job.Cell==(job.Smelt?_forgeFurnace:_forgeAnvil);
    private void MineWithDrop(Cell cell)
    {
        _blockDamage.Remove(cell);
        string? drop=ItemCatalog.Drop(Terrain.BlockAt(cell));
        if(!Terrain.Excavate(cell))return;
        if(drop is not null)SpawnItem(drop,1,cell);
        SettleItems();Revision++;
    }
    private void DisplaceItems(Cell cell,Cell destination,int height=1)
    {
        foreach(var stack in Items.Where(s=>s.Owner==""&&s.Cell.X==cell.X&&s.Cell.Z==cell.Z&&s.Cell.Level>=cell.Level&&s.Cell.Level<cell.Level+height))
        {stack.Cell=destination;Revision++;}
    }
    private void SettleItems()
    {
        using var timing = Probe?.Measure("items.settle") ?? default;
        foreach(var stack in Items.Where(s=>s.Owner==""))
        {
            var c=stack.Cell;
            while(c.Level>TerrainMap.MinLevel+1&&!Solid(c+new Cell(0,0,-1)))c+=new Cell(0,0,-1);
            if(stack.Cell!=c){stack.Cell=c;Revision++;}
        }
        foreach(var j in Jobs.Where(j=>!j.Haul))foreach(var r in j.Materials.Where(r=>r.Stage==2))
        {
            var stack=Items.FirstOrDefault(s=>s.Id==r.StackId);
            if(stack is not null&&!ActionPositions(j).Contains(stack.Cell))r.Stage=0;
        }
    }
}
