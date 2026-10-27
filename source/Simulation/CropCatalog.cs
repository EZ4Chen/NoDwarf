namespace NoDwarf.Simulation;
public sealed record CropDefinition(string Id,string Name,string Seed,string Yield,int Count,double Seconds,string Description,bool Tree=false);
public static class CropCatalog
{
    public static readonly CropDefinition[] All={
        new("grain","小麦","grain_seed","wheat",3,90,"露天土壤；小麦 → 石磨磨粉 → 炊炉烘焙。"),
        new("potato","土豆","potato_seed","potato",4,110,"露天土壤；耐凉，收获土豆和留种块茎，可烹饪。"),
        new("mushroom","洞穴蘑菇","mushroom_spawn","mushroom",3,120,"有顶板的地下天然地面；首次铺菌床另需土壤，可烹饪。"),
        new("cotton","棉花","cotton_seed","cotton",4,160,"露天土壤；喜暖，收获棉絮 → 织机织布 → 绷带。"),
        new("sapling","橡树","sapling","wood",20,600,"露天土壤；预留 14 米高度、4 米树间距，伐木获取木料。",true),
        new("birch_sapling","桦树","birch_sapling","wood",16,480,"生长较快，成树 8 米；预留 14 米高度、4 米树间距。",true),
        new("acacia_sapling","金合欢树","acacia_sapling","wood",12,560,"成树 6 米，弯曲分枝与扁平树冠；砍伐返还树苗。",true),
        new("jungle_sapling","丛林树","jungle_sapling","wood",28,840,"成树 14 米，宽阔树冠；用于热带雨林。",true),
        new("pine_sapling","松树","pine_sapling","wood",28,780,"生长较慢，成树 14 米；预留 14 米高度、4 米树间距。",true)};
    public static CropDefinition Get(string id)=>All.First(c=>c.Id==id);
    public static bool Valid(string id)=>All.Any(c=>c.Id==id);
    public static bool IsTree(string id)=>All.Any(c=>c.Id==id&&c.Tree);
}
