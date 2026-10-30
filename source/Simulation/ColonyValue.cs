namespace NoDwarf.Simulation;

// Versioned balance values, not trade quotes. Every current item has an explicit base value.
public static class ValueCatalog
{
    public const int Version = 1;
    public static double Base(string id,ContentCatalog? content=null)=>(content??ContentRuntime.Current).Item(id).BaseValue;
    public static double Item(string id, ItemProperties p, double time,ContentCatalog? content=null)
    {
        if (p.ExpiresAt is double end && end <= time) return 0;
        double material = p.MaterialId switch { "steel" => 1.6, "silver" => 2, "gold" => 4, _ => 1 };
        double quality = p.QualityId switch { "poor" => .6, "fine" or "good" => 1.3, "excellent" => 1.7, "masterwork" => 2.5, _ => 1 };
        double condition = p.MaxDurability is > 0 ? Math.Clamp(p.Durability!.Value / p.MaxDurability.Value, 0, 1) : 1;
        return Base(id,content) * material * quality * condition;
    }
    public static double Building(BuildKind kind,ContentCatalog? content=null)
    {
        var catalog=content??ContentRuntime.Current;
        return catalog.Building(BlockCatalog.Id(kind)).Cost.Sum(p=>p.Value*Base(p.Key,catalog))+5;
    }
    public static double Resident(ResidentProfile p)
    {
        if (p.Dead) return 0;
        double skills = 0; foreach (var s in ResidentCatalog.Skills) skills += p.Level(s.Id) * 30;
        return (500 + skills) * Math.Clamp(p.Consciousness, .2, 1);
    }
}

public sealed record ColonyValue(double Items, double Buildings, double Residents, int Living, int Capable,
    double CombatPower, int Meals, double At)
{
    public double Total => Items + Buildings + Residents;
}

public sealed partial class DemoWorld
{
    private ColonyValue? _valueCache;
    private long _valueCacheStamp;
    // Low-frequency aggregate; never scans terrain voxels or performs pathfinding.
    // Force a fresh total at the mutation boundary, because public inventory collections also support mods/tests.
    public ColonyValue Values(bool fresh = false)
    {
        if (!fresh && _valueCache is {} cached && SimulationTime >= cached.At && SimulationTime - cached.At < 2 && Environment.TickCount64 - _valueCacheStamp < 2000) return cached;
        using var timing = Probe?.Measure("story.value") ?? default;
        double items = 0, buildings = 0, residents = 0, combat = 0; int living = 0, capable = 0, meals = 0;
        var owners = new HashSet<string>(StringComparer.Ordinal);
        var fighters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in Workers)
        {
            owners.Add(w.Id); residents += ValueCatalog.Resident(w.Person);
            if (w.Person.Dead) continue;
            living++;
            if (!w.Person.Incapacitated) { fighters.Add(w.Id); capable++; combat += 50 * w.Person.Consciousness * w.Person.Manipulation + w.Person.Level("combat") * 5; }
        }
        foreach (var s in Items)
        {
            if (s.Owner != "" && !owners.Contains(s.Owner)) continue;
            double value = ValueCatalog.Item(s.Item, s.Properties, SimulationTime,Content);
            items += value * s.Count;
            if (CanEat(s) && value > 0) meals += s.Count;
            if (s.Slot == "main" && s.Item is "sword" or "battleaxe" && value > 0 && fighters.Contains(s.Owner)) combat += 25;
        }
        // Stage 0/2 reservations refer to real stacks already counted above. Stage 1 exists only in cargo.
        foreach (var j in Jobs) foreach (var r in j.Materials)
            if (r.Stage == 1) { double v = ValueCatalog.Item(r.Item, r.Properties, SimulationTime,Content); items += v * r.Count; if (Content.Item(r.Item).Edible is not null && v > 0) meals += r.Count; }
        foreach (var b in Buildings.Values)
        {
            double max = Content.Block(b.BlockId).Durability;
            double damage = _blockDamage.TryGetValue(b.Cell, out var d) && d.Block == b.BlockId ? d.Damage : 0;
            buildings += (BuildingDefinition(b).Cost.Sum(p=>p.Value*ValueCatalog.Base(p.Key,Content))+5) * Math.Clamp(1 - damage / max, 0, 1);
        }
        _valueCacheStamp = Environment.TickCount64;
        return _valueCache = new(items, buildings, residents, living, capable, combat, meals, SimulationTime);
    }
}
