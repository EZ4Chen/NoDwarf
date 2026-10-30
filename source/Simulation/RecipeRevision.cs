using System.Collections.ObjectModel;
using System.Globalization;
namespace NoDwarf.Simulation;

public static class RecipeRevision
{
    public static string Hash(ProcessingRecipe recipe)=>ContentCatalog.Hash(System.Text.Json.JsonSerializer.Serialize(new
    {
        recipe.Id,Station=recipe.Station.ToString(),recipe.Output,recipe.Count,
        Work=recipe.Work.ToString("R",CultureInfo.InvariantCulture),
        Inputs=recipe.Inputs.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new{p.Key,p.Value}).ToArray()
    }));
    public static ProcessingRecipe Validate(ProcessingRecipe recipe,ContentCatalog catalog)
    {
        if(recipe is null||string.IsNullOrEmpty(recipe.Id)||string.IsNullOrEmpty(recipe.Name)||recipe.Name.Length>256||string.IsNullOrEmpty(recipe.Output)||
           !Enum.IsDefined(recipe.Station)||!double.IsFinite(recipe.Work)||recipe.Work<=0||recipe.Work>10000||
           recipe.Count is <1 or >10000||recipe.Inputs is null||recipe.Inputs.Count is <1 or >256||
           recipe.Inputs.Any(p=>p.Value is <1 or >10000||!catalog.ItemsById.ContainsKey(p.Key))||!catalog.ItemsById.ContainsKey(recipe.Output))
            throw new InvalidDataException("在制配方快照无效");
        return recipe with {Inputs=new ReadOnlyDictionary<string,int>(new Dictionary<string,int>(recipe.Inputs,StringComparer.Ordinal))};
    }
}
public sealed partial class DemoWorld
{
    private void BindJobRecipe(DemoJob job)
    {
        if(!job.Production||job.RecipeSnapshot is not null)return;
        if(Content.FindRecipe(ProductionCatalog.RecipeOf(job)) is not {} recipe)return;
        job.RecipeSnapshot=recipe;job.RecipeRevision=RecipeRevision.Hash(recipe);
    }
    public ProcessingRecipe JobRecipe(DemoJob job)=>job.RecipeSnapshot??Content.Recipe(ProductionCatalog.RecipeOf(job));
    private static void RestoreJobRecipe(DemoJob job,ContentCatalog catalog)
    {
        if(!job.Production)
        {
            if(job.RecipeSnapshot is not null||job.RecipeRevision is not null)throw new InvalidDataException("非生产任务带有配方快照");
            return;
        }
        if(job.RecipeSnapshot is null||job.RecipeSnapshot.Id!=ProductionCatalog.RecipeOf(job)||job.RecipeRevision!=RecipeRevision.Hash(RecipeRevision.Validate(job.RecipeSnapshot,catalog)))
            throw new InvalidDataException("在制配方版本不一致");
        job.RecipeSnapshot=RecipeRevision.Validate(job.RecipeSnapshot,catalog);
        var r=job.RecipeSnapshot;
        if(job.Materials is null||job.Materials.Any(m=>m is null||m.Item is null||m.Count<=0||!r.Inputs.ContainsKey(m.Item))||r.Inputs.Any(p=>job.Materials.Where(m=>m.Item==p.Key).Sum(m=>(long)m.Count)!=p.Value))
            throw new InvalidDataException("在制配方材料不完整");
    }
}
