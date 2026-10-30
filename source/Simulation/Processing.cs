namespace NoDwarf.Simulation;
public sealed record ProcessingRecipe(string Id,string Name,BuildKind Station,IReadOnlyDictionary<string,int> Inputs,string Output,int Count)
{
    public double Work {get;init;}=5;
}
public static class ProcessingCatalog
{
    public static IReadOnlyList<ProcessingRecipe> All=>ProductionDefinitions.Recipes;
    public static ProcessingRecipe? Find(string id)=>ContentRuntime.Current.RecipesById.GetValueOrDefault(id);
}
public sealed partial class DemoWorld
{
    private bool ProcessingReady(DemoJob j)=>JobRecipe(j) is {} r&&Buildings.TryGetValue(j.Cell,out var b)&&b.Kind==r.Station&&(r.Station!=BuildKind.Stove||KitchenReady(j.Cell));
    public bool QueueProcessing(string recipe)
    {
        if(Content.FindRecipe(recipe) is not {} r)return false;
        var station=Buildings.Values.FirstOrDefault(b=>b.Kind==r.Station&&(r.Station!=BuildKind.Stove||KitchenReady(b.Cell)));
        if(station is null){Log("需要可用的"+NameOf(r.Station)+"。");return false;}
        if(Jobs.Count(j=>j.Recipe==recipe)>=3){Log("同一配方最多排队 3 单。");return false;}
        var job=new DemoJob{Id=++_nextJob,Cell=station.Cell,Recipe=recipe};
        if(!Reserve(job,r.Inputs)){Log("材料不足："+r.Name);return false;}
        job.ProductionOrderId=AddProductionOrder(recipe,station:job.Cell).Id;Jobs.Add(job);Revision++;Log("已安排"+r.Name);return true;
    }
    private void CompleteProcessing(DemoJob j,DemoWorker w)
    {var r=JobRecipe(j);ConsumeMaterials(j);SpawnItem(r.Output,r.Count,w.Tile,destinationZone:Bill(j)?.DestinationZone??0);Log(w.Name+"完成"+r.Name);}
}
