namespace NoDwarf.Simulation;

public enum BlockShape { Empty, Solid, Ramp, Stairs, Fixture, Vegetation }
public readonly record struct BuildCost(int Stone = 0, int Wood = 0, int Iron = 0);
public sealed record BlockDefinition(string Id, string Name, BlockShape Shape = BlockShape.Solid,
    string Color = "929084", BuildCost Cost = default)
{
    public double Durability => Id switch {
        "nodwarf:air"=>0,"nodwarf:bedrock"=>double.PositiveInfinity,
        "nodwarf:sand" or "nodwarf:sand_ramp" or "nodwarf:gravel" or "nodwarf:gravel_ramp"=>40,
        "nodwarf:humus" or "nodwarf:humus_ramp" or "nodwarf:dry_grass" or "nodwarf:dry_grass_ramp"=>60,
        "nodwarf:granite" or "nodwarf:granite_ramp"=>140,
        "nodwarf:sandstone" or "nodwarf:sandstone_ramp"=>80,
        "nodwarf:grass" or "nodwarf:soil" or "nodwarf:soil_ramp"=>60,
        "nodwarf:ladder" or "nodwarf:planks" or "nodwarf:plank_stairs" or "nodwarf:wood_door"=>60,
        "nodwarf:log" or "nodwarf:log_stairs" or "nodwarf:tree"=>80,
        "nodwarf:deepslate" or "nodwarf:deepslate_coal_ore"=>160,
        "nodwarf:deepslate_iron_ore"=>180,"nodwarf:iron_ore"=>120,
        "nodwarf:stone_wall" or "nodwarf:stone_bricks" or "nodwarf:stone_brick_stairs"=>150,
        "nodwarf:anvil"=>200,_=>100};
}

// Persistent names, independent of enum order. Direction belongs to placed block state.
public static class BlockCatalog
{
    private static readonly Dictionary<string, BlockDefinition> Definitions = new[]
    {
        new BlockDefinition("nodwarf:air", "空气", BlockShape.Empty),
        new("nodwarf:grass", "草地", Color: "6f7c50"), new("nodwarf:soil", "土壤", Color: "8a7356"),
        new("nodwarf:sand", "沙子", Color: "c2ae7d"), new("nodwarf:stone", "岩石", Color: "85877b"),
        new("nodwarf:soil_ramp", "土壤斜坡", BlockShape.Ramp, "8a7356"),
        new("nodwarf:sand_ramp", "沙子斜坡", BlockShape.Ramp, "c2ae7d"),
        new("nodwarf:stone_ramp", "岩石斜坡", BlockShape.Ramp, "85877b"),
        new("nodwarf:iron_ore", "铁矿", Color: "a08064"), new("nodwarf:coal_ore", "煤矿", Color: "555d56"),
        new("nodwarf:bedrock", "基岩", Color: "404047"),
        new("nodwarf:deepslate", "深板岩", Color: "4e505a"),
        new("nodwarf:deepslate_iron_ore", "深层铁矿", Color: "756961"),
        new("nodwarf:deepslate_coal_ore", "深层煤矿", Color: "35373d"),
        new("nodwarf:tree_oak_log", "橡树树干", BlockShape.Vegetation, "987b54"),
        new("nodwarf:tree_oak_leaves", "橡树树叶", BlockShape.Vegetation, "789659"),
        new("nodwarf:tree_birch_log", "桦树树干", BlockShape.Vegetation, "987b54"),
        new("nodwarf:tree_birch_leaves", "桦树树叶", BlockShape.Vegetation, "789659"),
        new("nodwarf:tree_spruce_log", "松树树干", BlockShape.Vegetation, "987b54"),
        new("nodwarf:tree_spruce_leaves", "松树树叶", BlockShape.Vegetation, "789659"),
        new("nodwarf:humus", "腐殖土", Color:"65523c"),
        new("nodwarf:dry_grass", "干草地", Color:"b4a363"),
        new("nodwarf:gravel", "砾石", Color:"96938d"),
        new("nodwarf:granite", "花岗岩", Color:"ab8a7d"),
        new("nodwarf:limestone", "石灰岩", Color:"c5bca5"),
        new("nodwarf:sandstone", "砂岩", Color:"c3ad7c"),
        new("nodwarf:humus_ramp", "腐殖土斜坡", BlockShape.Ramp,"65523c"),
        new("nodwarf:dry_grass_ramp", "干草地斜坡", BlockShape.Ramp,"b4a363"),
        new("nodwarf:gravel_ramp", "砾石斜坡", BlockShape.Ramp,"96938d"),
        new("nodwarf:granite_ramp", "花岗岩斜坡", BlockShape.Ramp,"ab8a7d"),
        new("nodwarf:limestone_ramp", "石灰岩斜坡", BlockShape.Ramp,"c5bca5"),
        new("nodwarf:sandstone_ramp", "砂岩斜坡", BlockShape.Ramp,"c3ad7c"),
        new("nodwarf:tree_acacia_log", "树干", BlockShape.Vegetation,"789659"),
        new("nodwarf:tree_acacia_leaves", "树叶", BlockShape.Vegetation,"789659"),
        new("nodwarf:tree_jungle_log", "树干", BlockShape.Vegetation,"789659"),
        new("nodwarf:tree_jungle_leaves", "树叶", BlockShape.Vegetation,"789659"),
        new("nodwarf:cover_grass", "草丛", BlockShape.Vegetation,"8c9d55"),
        new("nodwarf:cover_fern", "蕨类", BlockShape.Vegetation,"8c9d55"),
        new("nodwarf:cover_dead_bush", "枯灌木", BlockShape.Vegetation,"8c9d55"),
        new("nodwarf:tree", "树木", BlockShape.Vegetation, "42563a"),
        new("nodwarf:stone_wall", "石墙", Color: "c4bda5", Cost: new(Stone: 2)),
        new("nodwarf:log", "原木", Color: "795337", Cost: new(Wood: 2)),
        new("nodwarf:planks", "木板", Color: "b68b55", Cost: new(Wood: 2)),
        new("nodwarf:cobblestone", "圆石", Color: "7b807c", Cost: new(Stone: 2)),
        new("nodwarf:stone_bricks", "石砖", Color: "aba99d", Cost: new(Stone: 2)),
        new("nodwarf:ladder", "木梯", BlockShape.Fixture, "b68b55", new(Wood: 1)),
        new("nodwarf:log_stairs", "原木楼梯", BlockShape.Stairs, "795337", new(Wood: 3)),
        new("nodwarf:plank_stairs", "木板楼梯", BlockShape.Stairs, "b68b55", new(Wood: 3)),
        new("nodwarf:cobblestone_stairs", "圆石楼梯", BlockShape.Stairs, "7b807c", new(Stone: 3)),
        new("nodwarf:stone_brick_stairs", "石砖楼梯", BlockShape.Stairs, "aba99d", new(Stone: 3)),
        new("nodwarf:floodgate", "闸门", BlockShape.Fixture, "637f87", new(Stone: 3)),
        new("nodwarf:water", "水", BlockShape.Empty, "3d92bb"),
        new("nodwarf:wood_door", "木门", BlockShape.Fixture, "a37a50", new(Wood: 2)),
        new("nodwarf:table","餐桌",BlockShape.Fixture,"aa8b62",new(Wood:4)),
        new("nodwarf:chair","木椅",BlockShape.Fixture,"aa8b62",new(Wood:2)),
        new("nodwarf:game_table","棋桌",BlockShape.Fixture,"8b7955",new(Wood:4,Stone:1)),
        new("nodwarf:workbench","加工台",BlockShape.Fixture,"aa8b62",new(Wood:6)),
        new("nodwarf:mill","石磨",BlockShape.Fixture,"898b80",new(Stone:6,Wood:2)),
        new("nodwarf:loom","织机",BlockShape.Fixture,"b18e60",new(Wood:6)),
        new("nodwarf:stove", "炊炉", BlockShape.Fixture, "888278", new(Stone: 4)),
        new("nodwarf:bed", "床", BlockShape.Fixture, "ae6e64", new(Wood: 4)),
        new("nodwarf:campfire", "篝火", BlockShape.Fixture, "b27542", new(Stone: 2,Wood: 2)),
        new("nodwarf:furnace", "高炉", BlockShape.Fixture, "7c7465", new(Stone: 5)),
        new("nodwarf:anvil", "铁砧", BlockShape.Fixture, "54646a", new(Iron: 1))
    }.ToDictionary(d => d.Id, StringComparer.Ordinal);
    public static IReadOnlyCollection<BlockDefinition> All => Definitions.Values;
    public static BlockDefinition Get(string id) => Definitions.TryGetValue(id, out var definition)
        ? definition : throw new ArgumentException($"Unknown block ID: {id}", nameof(id));
    public static BlockDefinition Get(BuildKind kind) => Get(Id(kind));
    public static bool IsStairs(BuildKind kind) => Get(kind).Shape == BlockShape.Stairs;
    public static bool IsWall(BuildKind kind) => Get(kind).Shape == BlockShape.Solid;
    public static string Id(GroundKind kind) => kind switch
    {
        GroundKind.Grass => "nodwarf:grass", GroundKind.Soil => "nodwarf:soil", GroundKind.Sand => "nodwarf:sand",
        GroundKind.Rock => "nodwarf:stone", GroundKind.Iron => "nodwarf:iron_ore",
        GroundKind.Coal => "nodwarf:coal_ore",
        GroundKind.Humus=>"nodwarf:humus",GroundKind.DryGrass=>"nodwarf:dry_grass",
        GroundKind.Gravel=>"nodwarf:gravel",GroundKind.Granite=>"nodwarf:granite",
        GroundKind.Limestone=>"nodwarf:limestone",GroundKind.Sandstone=>"nodwarf:sandstone", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static string RampId(GroundKind kind) => kind switch
    { GroundKind.Humus or GroundKind.DryGrass or GroundKind.Gravel or GroundKind.Granite or GroundKind.Limestone or GroundKind.Sandstone=>Id(kind)+"_ramp", GroundKind.Sand => "nodwarf:sand_ramp", GroundKind.Rock or GroundKind.Iron or GroundKind.Coal => "nodwarf:stone_ramp", _ => "nodwarf:soil_ramp" };
    public static bool IsSoil(string id)=>id is "nodwarf:soil" or "nodwarf:grass" or "nodwarf:humus" or "nodwarf:dry_grass";
    public static bool IsRegionalRock(string id)=>id is "nodwarf:granite" or "nodwarf:limestone" or "nodwarf:sandstone";
    public static string Id(BuildKind kind) => kind switch
    {
        BuildKind.Ladder=>"nodwarf:ladder", BuildKind.Floodgate=>"nodwarf:floodgate", BuildKind.Wall => "nodwarf:stone_wall", BuildKind.Door => "nodwarf:wood_door",
        BuildKind.Table=>"nodwarf:table",BuildKind.Chair=>"nodwarf:chair",BuildKind.GameTable=>"nodwarf:game_table",BuildKind.Workbench=>"nodwarf:workbench",BuildKind.Mill=>"nodwarf:mill",BuildKind.Loom=>"nodwarf:loom",BuildKind.Stove=>"nodwarf:stove",BuildKind.Bed=>"nodwarf:bed",BuildKind.Campfire=>"nodwarf:campfire",
        BuildKind.Furnace => "nodwarf:furnace", BuildKind.Anvil => "nodwarf:anvil",
        BuildKind.Log => "nodwarf:log", BuildKind.Planks => "nodwarf:planks",
        BuildKind.Cobblestone => "nodwarf:cobblestone", BuildKind.StoneBricks => "nodwarf:stone_bricks",
        BuildKind.LogStairs => "nodwarf:log_stairs", BuildKind.PlankStairs => "nodwarf:plank_stairs",
        BuildKind.CobbleStairs => "nodwarf:cobblestone_stairs", BuildKind.BrickStairs => "nodwarf:stone_brick_stairs",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
