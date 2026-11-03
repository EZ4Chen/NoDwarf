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
    private void CompleteProcessing(DemoJob j,DemoWorker w)
    {var r=JobRecipe(j);var output=j.OutputCell??throw new InvalidOperationException("生产缺少输出预约");ConsumeMaterials(j);SpawnItem(r.Output,r.Count,output,destinationZone:Bill(j)?.DestinationZone??0);Log(w.Name+"完成"+r.Name);}
}
