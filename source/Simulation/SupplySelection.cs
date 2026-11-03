namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private static int SupplyDistance(Cell a,Cell b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Z-b.Z)+Math.Abs(a.Level-b.Level);
    // Permissions and reachability are filtered by the caller first. Prefer expiry only within
    // a local detour of the nearest candidate, so a distant old batch cannot monopolize supply.
    private IEnumerable<ItemStack> OrderSupplies(IEnumerable<ItemStack> eligible,Cell from)
    {
        return OrderSupplyOptions(eligible,s=>SupplyDistance(from,s.Cell),s=>s.Properties.ExpiresAt,s=>s.Id);
    }
    private static IEnumerable<T> OrderSupplyOptions<T>(IEnumerable<T> eligible,Func<T,int> distance,Func<T,double?> expiry,Func<T,int> id,int detour=8)
    {
        var options=eligible.ToArray();if(options.Length==0)return options;int nearest=options.Min(distance);
        return options.OrderBy(s=>distance(s)>nearest+detour?1:0).ThenBy(s=>distance(s)<=nearest+detour?expiry(s)??double.MaxValue:double.MaxValue).ThenBy(distance).ThenBy(id);
    }
    private bool FoodSourceReachable(DemoJob job,ItemStack stack)
    {
        if(Content.Item(stack.Item).ShelfLife==0)return true;
        var destination=JobAccess(job);
        return ItemAccess(stack.Cell).Any(c=>CanReachAny(c,destination)&&(Workers.Count==0||Workers.Any(w=>!w.Person.Dead&&BillAllowsWorker(job,w)&&ResidentNavigation.CanReach(w.Tile,c))));
    }
}
