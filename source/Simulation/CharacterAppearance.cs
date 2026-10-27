using System.Text.Json;

namespace NoDwarf.Simulation;

// Stable string IDs are stored instead of enum ordinals or renderer resources.
public sealed record CharacterAppearance
{
    public string Skin { get; init; } = "warm";
    public string Hair { get; init; } = "crop";
    public string HairColor { get; init; } = "chestnut";
    public string Face { get; init; } = "round";
    public string Outfit { get; init; } = "apron";
    public string Cloth { get; init; } = "sage";
    public string Body { get; init; } = "regular";
    public string Beard { get; init; } = "none";

    public static readonly string[] Skins = ["porcelain", "warm", "tan", "brown", "deep"];
    public static readonly string[] Hairs = ["crop", "sweep", "bob", "braid", "bald"];
    public static readonly string[] HairColors = ["chestnut", "black", "gold", "auburn", "silver", "white"];
    public static readonly string[] Faces = ["round", "oval", "square"];
    public static readonly string[] Outfits = ["apron", "tunic", "vest", "coat"];
    public static readonly string[] Cloths = ["sage", "ochre", "blue", "plum", "rust", "cream"];
    public static readonly string[] Bodies = ["slim", "regular", "broad"];
    public static readonly string[] Beards = ["none", "stubble", "full"];
    public CharacterAppearance Normalize() => this with
    {
        Skin = Valid(Skin, Skins, "warm"), Hair = Valid(Hair, Hairs, "crop"),
        HairColor = Valid(HairColor, HairColors, "chestnut"), Face = Valid(Face, Faces, "round"),
        Outfit = Valid(Outfit, Outfits, "apron"), Cloth = Valid(Cloth, Cloths, "sage"),
        Body = Valid(Body, Bodies, "regular"), Beard = Valid(Beard, Beards, "none")
    };
    private static string Valid(string value, string[] values, string fallback) => values.Contains(value) ? value : fallback;
    public static CharacterAppearance Randomize(Random random) => new()
    {
        Skin = Skins[random.Next(Skins.Length)], Hair = Hairs[random.Next(Hairs.Length)],
        HairColor = HairColors[random.Next(HairColors.Length)], Face = Faces[random.Next(Faces.Length)],
        Outfit = Outfits[random.Next(Outfits.Length)], Cloth = Cloths[random.Next(Cloths.Length)],
        Body = Bodies[random.Next(Bodies.Length)], Beard = Beards[random.Next(Beards.Length)]
    };
}

public sealed class AppearanceArchive
{
    public int Version { get; set; } = 1;
    public Dictionary<string, CharacterAppearance> Residents { get; set; } = new();
}

public static class AppearanceStorage
{
    public static string Serialize(IEnumerable<DemoWorker> workers) => JsonSerializer.Serialize(new AppearanceArchive
    { Residents = workers.ToDictionary(w => w.Id, w => w.Appearance.Normalize()) }, new JsonSerializerOptions { WriteIndented = true });

    public static Dictionary<string, CharacterAppearance> Deserialize(string json)
    {
        var archive = JsonSerializer.Deserialize<AppearanceArchive>(json) ?? throw new InvalidDataException("角色档案为空");
        if (archive.Version != 1 || archive.Residents is null) throw new InvalidDataException("角色档案版本不受支持");
        return archive.Residents.ToDictionary(p => p.Key, p => (p.Value ?? new CharacterAppearance()).Normalize());
    }

    public static void Save(string path, IEnumerable<DemoWorker> workers)
    {
        string json = Serialize(workers);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, true);
    }
    public static void Load(string path, IEnumerable<DemoWorker> workers)
    {
        if (!File.Exists(path)) return;
        var records = Deserialize(File.ReadAllText(path));
        foreach (var worker in workers)
            if (records.TryGetValue(worker.Id, out var appearance)) worker.Appearance = appearance;
    }
}
