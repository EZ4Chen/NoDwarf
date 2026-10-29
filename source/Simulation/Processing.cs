namespace NoDwarf.Simulation;
public sealed record ProcessingRecipe(string Id,string Name,BuildKind Station,Dictionary<string,int> Inputs,string Output,int Count);
public static class ProcessingCatalog
{
    public static readonly ProcessingRecipe[] All={
        new("splint","夹板：1 木料 + 1 布 → 2 夹板",BuildKind.Workbench,new(){{"wood",1},{"cloth",1}},"splint",2),
        new("prosthesis","木制义肢：3 木料 + 1 布 → 义肢",BuildKind.Workbench,new(){{"wood",3},{"cloth",1}},"prosthesis",1),
        new("bucket","水桶：2 木料 → 水桶",BuildKind.Workbench,new(){{"wood",2}},"bucket",1),
        new("compost","处理腐败物：2 腐败物 → 土壤",BuildKind.Workbench,new(){{"rot",2}},"soil",1),
        new("flour","磨粉：2 小麦 → 2 面粉",BuildKind.Mill,new(){{"wheat",2}},"flour",2),
        new("bread","烘焙：2 面粉 + 1 木料 → 3 口粮",BuildKind.Stove,new(){{"flour",2},{"wood",1}},"meal",3),
        new("potato_meal","煮土豆：2 土豆 + 1 木料 → 2 口粮",BuildKind.Stove,new(){{"potato",2},{"wood",1}},"meal",2),
        new("mushroom_meal","蘑菇餐：2 蘑菇 + 1 木料 → 2 口粮",BuildKind.Stove,new(){{"mushroom",2},{"wood",1}},"meal",2),
        new("cloth","织布：4 棉絮 → 2 布",BuildKind.Loom,new(){{"cotton",4}},"cloth",2),
        new("bandages","裁绷带：1 布 → 3 绷带",BuildKind.Loom,new(){{"cloth",1}},"bandage",3),
        new("shirt","裁布衣：2 布 → 布衣",BuildKind.Loom,new(){{"cloth",2}},"shirt",1),
        new("trousers","裁布裤：2 布 → 布裤",BuildKind.Loom,new(){{"cloth",2}},"trousers",1),
        new("shield","木盾：4 木料 → 木盾",BuildKind.Workbench,new(){{"wood",4}},"shield",1),
        new("rubble","碎石整形：2 深板岩碎石 → 2 圆石",BuildKind.Workbench,new(){{"deep_rubble",2}},"cobblestone",2),
        new("pickaxe","锻镐：2 铁锭 + 1 木料 → 矿镐",BuildKind.Anvil,new(){{"iron",2},{"wood",1}},"pickaxe",1),
        new("hammer","锻锤：1 铁锭 + 1 木料 → 锻锤",BuildKind.Anvil,new(){{"iron",1},{"wood",1}},"hammer",1),
        new("axe","铁斧：2 铁锭 + 1 木料 → 铁斧",BuildKind.Anvil,new(){{"iron",2},{"wood",1}},"axe",1),
        new("battleaxe","双刃战斧：3 铁锭 + 2 木料 → 双刃战斧",BuildKind.Anvil,new(){{"iron",3},{"wood",2}},"battleaxe",1),
        new("shovel","铁铲：1 铁锭 + 2 木料 → 铁铲",BuildKind.Anvil,new(){{"iron",1},{"wood",2}},"shovel",1),
        new("hoe","铁锄：2 铁锭 + 2 木料 → 铁锄",BuildKind.Anvil,new(){{"iron",2},{"wood",2}},"hoe",1)};
    public static ProcessingRecipe? Find(string id)=>All.FirstOrDefault(r=>r.Id==id);
}
public sealed partial class DemoWorld
{
    private bool ProcessingReady(DemoJob j)=>ProcessingCatalog.Find(j.Recipe) is {} r&&Buildings.TryGetValue(j.Cell,out var b)&&b.Kind==r.Station&&(r.Station!=BuildKind.Stove||KitchenReady(j.Cell));
    public bool QueueProcessing(string recipe)
    {
        if(ProcessingCatalog.Find(recipe) is not {} r)return false;
        var station=Buildings.Values.FirstOrDefault(b=>b.Kind==r.Station&&(r.Station!=BuildKind.Stove||KitchenReady(b.Cell)));
        if(station is null){Log("需要可用的"+NameOf(r.Station)+"。");return false;}
        if(Jobs.Count(j=>j.Recipe==recipe)>=3){Log("同一配方最多排队 3 单。");return false;}
        var job=new DemoJob{Id=++_nextJob,Cell=station.Cell,Recipe=recipe};
        if(!Reserve(job,r.Inputs)){Log("材料不足："+r.Name);return false;}
        Jobs.Add(job);Revision++;Log("已安排"+r.Name);return true;
    }
    private void CompleteProcessing(DemoJob j,DemoWorker w)
    {var r=ProcessingCatalog.Find(j.Recipe)!;ConsumeMaterials(j);SpawnItem(r.Output,r.Count,w.Tile);Log(w.Name+"完成"+r.Name);}
}
