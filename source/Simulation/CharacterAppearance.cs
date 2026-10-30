using System.Text.Json;

namespace NoDwarf.Simulation;

// Stable string IDs are stored instead of enum ordinals or renderer resources.
public sealed record CharacterAppearance
{
    public string BodySex { get; init; } = "male";
    public string Skin { get; init; } = "warm";
    public string Hair { get; init; } = "crop";
    public string HairColor { get; init; } = "chestnut";
    public string BeardColor { get; init; } = "silver";
    public string Face { get; init; } = "round";
    public string Outfit { get; init; } = "apron";
    public string Cloth { get; init; } = "sage";
    public string Body { get; init; } = "male";
    public string Beard { get; init; } = "none";

    public static readonly string[] Skins = ["porcelain", "warm", "tan", "brown", "deep"];
    public static readonly string[] Hairs = ["crop", "sweep", "bob", "braid", "curls", "ponytail", "topknot", "bald"];
    public static readonly string[] HairNames = ["短寸", "侧分短发", "蓬松长发", "双辫", "短卷发", "马尾", "发髻", "光头"];
    public static readonly string[] HairColors = ["chestnut", "black", "gold", "auburn", "silver", "white"];
    public static readonly string[] Faces = ["round", "oval", "square"];
    public static readonly string[] Outfits = ["apron", "tunic", "vest", "coat"];
    public static readonly string[] Cloths = ["sage", "ochre", "blue", "plum", "rust", "cream"];
    public static readonly string[] Bodies = ["male", "female", "thin", "hulk", "fat"];
    public static readonly string[] Beards = ["none", "stubble", "full"];
    public CharacterAppearance Normalize() => this with
    {
        BodySex = BodySex == "female" ? "female" : "male",
        Skin = ValidColor(Skin, Skins, "warm"), Hair = Valid(Hair, Hairs, "crop"),
        HairColor = ValidColor(HairColor, HairColors, "chestnut"), BeardColor = ValidColor(BeardColor, HairColors, "silver"), Face = Valid(Face, Faces, "round"),
        Outfit = Valid(Outfit, Outfits, "apron"), Cloth = Valid(Cloth, Cloths, "sage"),
        Body = Valid(Body, Bodies, BodySex == "female" ? "female" : "male"), Beard = Valid(Beard, Beards, "none")
    };
    private static string Valid(string value, string[] values, string fallback) => values.Contains(value) ? value : fallback;
    private static string ValidColor(string value, string[] presets, string fallback) =>
        value is { Length:7 } && value[0]=='#' && value.AsSpan(1).ToString().All(Uri.IsHexDigit)
            ? value.ToLowerInvariant() : Valid(value,presets,fallback);
    public static CharacterAppearance Randomize(Random random) => new()
    {
        BodySex = random.Next(2) == 0 ? "male" : "female",
        Skin = Skins[random.Next(Skins.Length)], Hair = Hairs[random.Next(Hairs.Length)],
        HairColor = HairColors[random.Next(HairColors.Length)], BeardColor = HairColors[random.Next(HairColors.Length)], Face = Faces[random.Next(Faces.Length)],
        Outfit = Outfits[random.Next(Outfits.Length)], Cloth = Cloths[random.Next(Cloths.Length)],
        Body = Bodies[random.Next(Bodies.Length)], Beard = Beards[random.Next(Beards.Length)]
    };
}

public sealed class AppearanceArchive
{
    public const int CurrentVersion=2;
    [System.Text.Json.Serialization.JsonRequired] public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, CharacterAppearance> Residents { get; set; } = new();
}

public static class AppearanceStorage
{
    public static string Serialize(IEnumerable<DemoWorker> workers) => JsonSerializer.Serialize(new AppearanceArchive
    { Residents = workers.ToDictionary(w => w.Id, w => w.Appearance.Normalize()) }, new JsonSerializerOptions { WriteIndented = true });

    public static Dictionary<string, CharacterAppearance> Deserialize(string json)
    {
        var archive = JsonSerializer.Deserialize<AppearanceArchive>(json) ?? throw new InvalidDataException("角色档案为空");
        if (archive.Version != AppearanceArchive.CurrentVersion || archive.Residents is null) throw new InvalidDataException("角色档案版本不受支持");
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
