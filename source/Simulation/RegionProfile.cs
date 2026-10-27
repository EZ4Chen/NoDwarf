namespace NoDwarf.Simulation;

// IDs and generation version are saved. Legacy maps retain their original terrain exactly.
public sealed record RegionProfile(string Id,string Name,double Mean,double Seasonal,double Daily,
    int CampHeight,double TreeChance,string Rock)
{
    public static readonly RegionProfile[] Legacy={
        new("temperate","温带森林平原",12,18,4,68,.18,"limestone"),
        new("boreal","针叶林丘陵",3,22,5,78,.22,"granite"),
        new("savanna","热带草原",25,5,6,70,.006,"sandstone"),
        new("alpine","高寒山地",-3,16,5,98,.035,"granite")};
    public bool Modern {get;init;}
    public double CoverDensity {get;init;}=.22;
    public double Humidity {get;init;}=.6;
    public int ShortTrees {get;init;}
    public string Cover {get;init;}="grass";
    public static readonly RegionProfile[] All={
        Legacy[0] with {Modern=true,CoverDensity=.28,Humidity=.7},
        Legacy[1] with {Modern=true,Cover="fern",CoverDensity=.24,Humidity=.65},
        Legacy[2] with {Modern=true,TreeChance=.003,ShortTrees=6,CoverDensity=.55,Humidity=.25},
        Legacy[3] with {Modern=true,CoverDensity=.08,Humidity=.35},
        new("grassland","温带草原",12,18,5,68,.0015,"limestone"){Modern=true,ShortTrees=6,CoverDensity=.58,Humidity=.5},
        new("desert","热带沙漠",31,9,11,70,0,"sandstone"){Modern=true,Cover="dead_bush",CoverDensity=.018,Humidity=.02},
        new("arid","干旱灌木地",23,10,8,71,.002,"sandstone"){Modern=true,ShortTrees=5,CoverDensity=.16,Humidity=.15},
        new("rainforest","热带雨林",26,3,3,72,.22,"granite"){Modern=true,Cover="fern",CoverDensity=.42,Humidity=.95},
        new("wetland","温带湿润林地",14,13,3,66,.12,"limestone"){Modern=true,Cover="fern",CoverDensity=.45,Humidity=.9},
        new("tundra","苔原",-5,20,6,72,.0007,"granite"){Modern=true,ShortTrees=5,CoverDensity=.12,Humidity=.3}};
    public static RegionProfile Get(string id,int ecology=2)=>(ecology==1?Legacy:All).FirstOrDefault(p=>p.Id==id)
        ??throw new ArgumentException("未知地区类型："+id);
    public string TreeSpecies(double roll)=>Id switch {
        "boreal"=>roll<.88?"pine_sapling":"birch_sapling",
        "alpine" or "tundra"=>"pine_sapling", "savanna" or "arid"=>Modern?"acacia_sapling":"sapling",
        "rainforest"=>"jungle_sapling",
        _=>roll<.65?"sapling":"birch_sapling"};
}
