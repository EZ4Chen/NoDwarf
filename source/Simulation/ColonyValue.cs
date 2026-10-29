namespace NoDwarf.Simulation;

// Versioned balance values, not trade quotes. Every current item has an explicit base value.
public static class ValueCatalog
{
    public const int Version = 1;
    public static double Base(string id) => id switch
    {
        "rot" => 0, "soil" or "sand" => .2, "cobblestone" or "deep_rubble" => 1,
        "wood" => 2, "raw_iron" => 5, "coal" => 3, "iron" => 12,
        "sword" => 65, "battleaxe" => 85, "shield" => 30,
        "pickaxe" or "axe" => 45, "hammer" => 35, "shovel" or "hoe" => 30,
        "shirt" or "trousers" => 18, "boots" => 25, "gloves" or "cap" => 12,
        "sapling" or "acacia_sapling" or "jungle_sapling" or "birch_sapling" or "pine_sapling" => 3,
        "grain_seed" or "potato_seed" or "cotton_seed" or "mushroom_spawn" => 1,
        "wheat" or "potato" or "mushroom" or "raw_food" => 2, "flour" => 3,
        "cotton" => 2, "cloth" => 7, "meal" => 6, "bandage" => 9,
        "bucket" => 8, "water_bucket" => 9, "splint" => 12, "prosthesis" => 40,
        _ => throw new ArgumentException("物品缺少估值定义：" + id)
    };
    public static double Item(string id, ItemProperties p, double time)
    {
        if (p.ExpiresAt is double end && end <= time) return 0;
        double material = p.MaterialId switch { "steel" => 1.6, "silver" => 2, "gold" => 4, _ => 1 };
        double quality = p.QualityId switch { "poor" => .6, "fine" or "good" => 1.3, "excellent" => 1.7, "masterwork" => 2.5, _ => 1 };
        double condition = p.MaxDurability is > 0 ? Math.Clamp(p.Durability!.Value / p.MaxDurability.Value, 0, 1) : 1;
        return Base(id) * material * quality * condition;
    }
    public static double Building(BuildKind kind)
    {
        var cost = BlockCatalog.Get(kind).Cost;
        return cost.Stone * Base("cobblestone") + cost.Wood * Base("wood") + cost.Iron * Base("iron") + 5;
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
            double value = ValueCatalog.Item(s.Item, s.Properties, SimulationTime);
            items += value * s.Count;
            if (s.Item == "meal" && value > 0) meals += s.Count;
            if (s.Slot == "main" && s.Item is "sword" or "battleaxe" && value > 0 && fighters.Contains(s.Owner)) combat += 25;
        }
        // Stage 0/2 reservations refer to real stacks already counted above. Stage 1 exists only in cargo.
        foreach (var j in Jobs) foreach (var r in j.Materials)
            if (r.Stage == 1) { double v = ValueCatalog.Item(r.Item, r.Properties, SimulationTime); items += v * r.Count; if (r.Item == "meal" && v > 0) meals += r.Count; }
        foreach (var b in Buildings.Values)
        {
            double max = BlockCatalog.Get(b.Kind).Durability;
            double damage = _blockDamage.TryGetValue(b.Cell, out var d) && d.Block == BlockCatalog.Id(b.Kind) ? d.Damage : 0;
            buildings += ValueCatalog.Building(b.Kind) * Math.Clamp(1 - damage / max, 0, 1);
        }
        _valueCacheStamp = Environment.TickCount64;
        return _valueCache = new(items, buildings, residents, living, capable, combat, meals, SimulationTime);
    }
}
