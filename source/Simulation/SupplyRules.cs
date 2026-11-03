namespace NoDwarf.Simulation;
public sealed record SupplyReserve
{
    public string Item {get;init;}="";
    public int Minimum {get;init;}
    public HashSet<string> AllowedUses {get;init;}=new();
}
public sealed partial class DemoWorld
{
    public List<SupplyReserve> SupplyReserves {get;}=new();
    public IEnumerable<(string Id,string Name)> SupplyUses=>new[]{("food","进食／送饭"),("care","医疗／饮水"),("construction","建筑／放置"),("growing","种植"),("heating","添柴"),("pickup","手动拾取")}.Concat(Content.Recipes.Select(r=>("recipe:"+r.Id,r.Name)));
    private string JobUse(DemoJob j)=>j.Haul?"haul":j.Production?"recipe:"+JobRecipe(j).Id:j.Refuel?"heating":j.Farming||j.PlantTree?"growing":"construction";
    private bool Fresh(ItemStack s)=>s.Properties.ExpiresAt is not double t||t>SimulationTime;
    private bool UsableSource(ItemStack s)=>!s.Forbidden&&Fresh(s)&&s.Owner==""&&SafetySupplyAllowed(s.Cell);
    public int AvailableForUse(string item,string use)
    {
        int free=Items.Where(s=>s.Item==item&&s.Owner==""&&!s.Forbidden&&Fresh(s)).Sum(s=>Math.Max(0,s.Count-Reserved(s.Id)));
        var reserve=SupplyReserves.FirstOrDefault(r=>r.Item==item);
        return use=="haul"||reserve is null||reserve.AllowedUses.Contains(use)?free:Math.Max(0,free-reserve.Minimum);
    }
    private int FreeForUse(ItemStack s,string use)=>UsableSource(s)?Math.Min(Math.Max(0,s.Count-Reserved(s.Id)),AvailableForUse(s.Item,use)):0;
    public ResidentResult ConfigureSupplyReserve(string item,int minimum,IEnumerable<string> allowedUses)
    {
        var uses=allowedUses.ToHashSet();if(!Content.ItemsById.ContainsKey(item)||minimum is <0 or >1000000||uses.Any(u=>!SupplyUses.Any(p=>p.Id==u)))return new(false,"保留数量或用途无效");
        SupplyReserves.RemoveAll(r=>r.Item==item);if(minimum>0)SupplyReserves.Add(new(){Item=item,Minimum=minimum,AllowedUses=uses});
        Revision++;foreach(var w in Workers){w.NextJobSearch=w.NextCareCheck=w.NextMedicalCheck=0;}RefreshLifeAlerts();
        return new(true,minimum==0?"已取消保留，未预约物资可由所有用途使用":"保留量已应用于新预约；既有预约和已经搬运的材料不撤回");
    }
    public ResidentResult SetItemForbidden(int id,bool forbidden)
    {
        var stack=Items.FirstOrDefault(s=>s.Id==id);if(stack is null)return new(false,"物品堆已不存在");
        stack.Forbidden=forbidden;
        foreach(var w in Workers)
        {
            if(forbidden&&w.Person.Activity is {} a&&a.ItemId==id)CancelResidentActivity(w,"所用物品已被禁用");
            w.NextJobSearch=w.NextCareCheck=w.NextMedicalCheck=0;
        }
        RefreshSafetyJobs();Revision++;RefreshLifeAlerts();return new(true,forbidden?"已禁用：停止新取料、取食、照料和搬运；物品保留":"已恢复使用，将重新检查任务和补给");
    }
    public string ItemAvailability(string item)
    {
        int held=Items.Where(s=>s.Item==item&&s.Owner!="").Sum(s=>s.Count),blocked=Items.Where(s=>s.Item==item&&s.Owner==""&&s.Forbidden).Sum(s=>s.Count);
        int reserved=Items.Where(s=>s.Item==item&&s.Owner=="").Sum(s=>Reserved(s.Id)),carried=Jobs.SelectMany(j=>j.Materials).Where(m=>m.Item==item&&m.Stage==1).Sum(m=>m.Count);
        int minimum=SupplyReserves.FirstOrDefault(r=>r.Item==item)?.Minimum??0;
        return $"总量 {Total(item)} · 普通用途可用 {AvailableForUse(item,"general")}\n个人 {held} · 预约 {reserved} · 在途 {carried} · 禁用 {blocked} · 最低保留 {minimum}";
    }
    private void AddSupplyAlerts()
    {
        foreach(var item in Items.Where(s=>s.Owner==""&&(Content.Item(s.Item).Edible is not null||s.Item is "bandage" or "water_bucket")).Select(s=>s.Item).Distinct())
        {
            string use=Content.Item(item).Edible is not null?"food":"care";
            if(AvailableForUse(item,use)>0||!Items.Any(s=>s.Item==item&&(s.Forbidden||SupplyReserves.Any(r=>r.Item==item&&r.Minimum>0))))continue;
            LifeAlerts.Add(new("supply:"+item,Content.Item(item).Name+"被禁用或保留规则限制，调整物资使用后恢复",1,Location:Items.First(s=>s.Item==item).Cell));
        }
        foreach(var j in Jobs.Where(j=>j.SupplyPaused).Take(8))LifeAlerts.Add(new("supply-job:"+j.Id,$"任务 #{j.Id} 等待允许使用的物资",2,Location:j.Cell));
    }
}
