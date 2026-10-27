namespace NoDwarf.Simulation;

// Low vegetation is deterministic decoration, separate from solid ground and pathfinding.
public static class GroundCover
{
    public static string? At(DemoWorld world,Cell c)
    {
        var t=world.Terrain;var profile=t.Region;
        if(profile is null||!profile.Modern||!t.Contains(c)||t.Ramp(c)||t[c.X,c.Z].Tree||
            !t.Solid(c+new Cell(0,0,-1))||world.Buildings.ContainsKey(c)||world.Farms.ContainsKey(c)||world.Stockpiles.Contains(c))return null;
        if(t.Hash(c.X+1703,c.Z+971)>=profile.CoverDensity)return null;
        var ground=t[c.X,c.Z].Kind;
        if(profile.Cover=="dead_bush")return ground is GroundKind.Sand or GroundKind.Sandstone?"dead_bush":null;
        if(!BlockCatalog.IsSoil(BlockCatalog.Id(ground)))return null;
        if(profile.Id=="arid"&&t.Hash(c.X+73,c.Z+711)<.4)return "dead_bush";
        return profile.Cover;
    }
}
