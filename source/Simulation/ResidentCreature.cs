using System.Text.Json.Serialization;
namespace NoDwarf.Simulation;

public sealed partial class ResidentProfile
{
    public string SpeciesId {get;set;}="base:dwarf";
    private CreatureCatalog? _speciesCatalog;
    [JsonIgnore] public CreatureSpeciesDefinition Species => !string.IsNullOrWhiteSpace(SpeciesId)&&(_speciesCatalog??CreatureCatalog.Default).Species.TryGetValue(SpeciesId,out var definition)&&definition.Controller=="resident"
        ? definition : throw new InvalidDataException("缺少居民物种："+SpeciesId);
    [JsonIgnore] public CreatureBodyDefinition Body=>(_speciesCatalog??CreatureCatalog.Default).Bodies[Species.Body];
    public bool Can(string capability)=>Species.Can(capability);
    public void BindSpeciesCatalog(CreatureCatalog catalog)
    {
        if(string.IsNullOrWhiteSpace(SpeciesId)||!catalog.Species.TryGetValue(SpeciesId,out var definition)||definition.Controller!="resident")throw new InvalidDataException("物种不支持居民控制器："+SpeciesId);
        _speciesCatalog=catalog;
    }
}

// Handles include their owner to avoid collisions between existing string resident IDs and wildlife IDs.
public readonly record struct CreatureHandle(string Owner,string Id);
public sealed record UnifiedCreatureObservation(CreatureHandle Handle,string Species,string Name,string Role,
    Cell Cell,bool Dead,IReadOnlyList<string> Capabilities,IReadOnlyDictionary<string,double> PartIntegrity);
public sealed partial class DemoWorld
{
    public IEnumerable<CreatureHandle> AllCreatureHandles()
    {
        foreach(var actor in Workers.Concat(Enemies))yield return new("resident",actor.Id);
        if(_creatures is not null)foreach(var id in _creatures.Ids())yield return new("wildlife",id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    public UnifiedCreatureObservation ObserveCreature(CreatureHandle handle)
    {
        if(handle.Owner=="resident")
        {
            var actor=Workers.Concat(Enemies).FirstOrDefault(a=>a.Id==handle.Id)??throw new ArgumentException("未知生物");
            var p=actor.Person;
            return new(handle,p.SpeciesId,actor.Name,Workers.Contains(actor)?"colonist":"hostile",actor.Tile,p.Dead,p.Species.Capabilities,
                new System.Collections.ObjectModel.ReadOnlyDictionary<string,double>(p.Body.Parts.ToDictionary(part=>part.Id,part=>p.Integrity(part.Id))));
        }
        if(handle.Owner!="wildlife"||!long.TryParse(handle.Id,out var id))throw new ArgumentException("未知生物句柄");
        var animal=Creatures.Observe(id,SimulationTime);var definition=Creatures.Catalog.Species[animal.Species];
        var body=Creatures.Catalog.Bodies[definition.Body];
        return new(handle,animal.Species,animal.Name,"wild",animal.Cell,animal.Dead,definition.Capabilities,
            new System.Collections.ObjectModel.ReadOnlyDictionary<string,double>(body.Parts.ToDictionary(p=>p.Id,p=>animal.Health[p.Id]/p.Health)));
    }
    // Injury kinds and treatment stay in the established resident health pipeline.
    public ResidentResult InjureCreature(CreatureHandle handle,string part,string kind,double amount)
    {
        if(handle.Owner=="resident")
        {
            var actor=Workers.Concat(Enemies).FirstOrDefault(a=>a.Id==handle.Id);
            if(actor is null)return new(false,"未知生物");
            var result=ApplyInjury(actor,part,kind,amount);if(result.Ok)Revision++;return result;
        }
        if(handle.Owner!="wildlife"||!long.TryParse(handle.Id,out var id))return new(false,"未知生物句柄");
        if(kind!="cut")return new(false,"野生生物尚未接入分型伤势");
        return new(DamageCreature(id,part,amount),"已处理生物伤害");
    }
}
