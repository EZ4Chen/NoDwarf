namespace NoDwarf.Simulation;

// Current biome definitions shared by atlas and local terrain generation.
public sealed record RegionProfile(string Id,string Name,double Mean,double Seasonal,double Daily,
    int CampHeight,double TreeChance,string Rock)
{
    public double CoverDensity {get;init;}=.22;
    public double Humidity {get;init;}=.6;
    public int ShortTrees {get;init;}
    public string Cover {get;init;}="grass";
    public static readonly RegionProfile[] All={
        new("temperate","温带森林平原",12,18,4,68,.18,"limestone"){CoverDensity=.28,Humidity=.7},
        new("boreal","针叶林丘陵",3,22,5,78,.22,"granite"){Cover="fern",CoverDensity=.24,Humidity=.65},
        new("savanna","热带草原",25,5,6,70,.003,"sandstone"){ShortTrees=6,CoverDensity=.55,Humidity=.25},
        new("alpine","高寒山地",-3,16,5,98,.035,"granite"){CoverDensity=.08,Humidity=.35},
        new("grassland","温带草原",12,18,5,68,.0015,"limestone"){ShortTrees=6,CoverDensity=.58,Humidity=.5},
        new("desert","热带沙漠",31,9,11,70,0,"sandstone"){Cover="dead_bush",CoverDensity=.018,Humidity=.02},
        new("arid","干旱灌木地",23,10,8,71,.002,"sandstone"){ShortTrees=5,CoverDensity=.16,Humidity=.15},
        new("rainforest","热带雨林",26,3,3,72,.22,"granite"){Cover="fern",CoverDensity=.42,Humidity=.95},
        new("wetland","温带湿润林地",14,13,3,66,.12,"limestone"){Cover="fern",CoverDensity=.45,Humidity=.9},
        new("tundra","苔原",-5,20,6,72,.0007,"granite"){ShortTrees=5,CoverDensity=.12,Humidity=.3}};
    public static RegionProfile Get(string id)=>All.FirstOrDefault(p=>p.Id==id)
        ??throw new ArgumentException("未知地区类型："+id);
    public string TreeSpecies(double roll)=>Id switch {
        "boreal"=>roll<.88?"pine_sapling":"birch_sapling",
        "alpine" or "tundra"=>"pine_sapling", "savanna" or "arid"=>"acacia_sapling",
        "rainforest"=>"jungle_sapling",
        _=>roll<.65?"sapling":"birch_sapling"};
}
