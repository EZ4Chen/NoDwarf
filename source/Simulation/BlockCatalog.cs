namespace NoDwarf.Simulation;

public enum BlockShape { Empty, Solid, Ramp, Stairs, Fixture, Vegetation }
public readonly record struct BuildCost(int Stone = 0, int Wood = 0, int Iron = 0);
public sealed record BlockDefinition(string Id, string Name, BlockShape Shape = BlockShape.Solid,
    string Color = "929084", BuildCost Cost = default):IContentDefinition
{
    public double Durability {get;init;}=100;
    public DefinitionInfo Info {get;init;}=DefinitionInfo.Empty;
    public IReadOnlyDictionary<string,int> Drops {get;init;}=new System.Collections.ObjectModel.ReadOnlyDictionary<string,int>(new Dictionary<string,int>());
    public OreGeneration? Generation {get;init;}

}

// Persistent names, independent of enum order. Direction belongs to placed block state.
public static class BlockCatalog
{
    // Enum callers describe built-in handlers; world content uses world.Content.Block/Building.
    private static readonly Lazy<ContentCatalog> Core=new(()=>ContentCatalog.Load(Array.Empty<ContentPack>()));
    public static IReadOnlyCollection<BlockDefinition> All => Core.Value.Blocks.Values.ToArray();
    public static BlockDefinition Get(string id) => Core.Value.Block(id);
    public static BlockDefinition Get(BuildKind kind) => Get(Id(kind));
    public static bool IsStairs(BuildKind kind) => kind is BuildKind.LogStairs or BuildKind.PlankStairs or BuildKind.CobbleStairs or BuildKind.BrickStairs;
    public static bool IsWall(BuildKind kind) => kind is BuildKind.Wall or BuildKind.Log or BuildKind.Planks or BuildKind.Cobblestone or BuildKind.StoneBricks;
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
