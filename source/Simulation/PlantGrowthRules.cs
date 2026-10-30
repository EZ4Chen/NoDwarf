using System.Globalization;
using System.Xml;
using System.Xml.Linq;
namespace NoDwarf.Simulation;
public sealed record PlantGrowthRule(double MinTemperature,double IdealLow,double IdealHigh,double MaxTemperature,
    double MinLight,double OptimalLight,double FertilitySensitivity,double MinFertility,int RestStart,int RestEnd,bool Dark,double WildChance)
{
    public double TemperatureFactor(double t)=>t<=MinTemperature||t>=MaxTemperature?0:t<IdealLow?(t-MinTemperature)/(IdealLow-MinTemperature):t>IdealHigh?(MaxTemperature-t)/(MaxTemperature-IdealHigh):1;
    public double FertilityFactor(double fertility)=>fertility<MinFertility?0:Math.Max(0,1+(fertility-1)*FertilitySensitivity);
    public double LightFactor(double light)=>Dark?Math.Clamp(1-light/OptimalLight,0,1):light<MinLight?0:Math.Clamp(light/OptimalLight,0,1);
    public bool Resting(int hour)=>RestStart<0?false:RestStart<RestEnd?hour>=RestStart&&hour<RestEnd:hour>=RestStart||hour<RestEnd;
}
public sealed class PlantGrowthRules
{
    private readonly Dictionary<string,PlantGrowthRule> _crops=new();
    private readonly Dictionary<string,double> _soil=new();
    public PlantGrowthRule Crop(string id)=>_crops.TryGetValue(id,out var rule)?rule:throw new InvalidDataException("缺少作物生长配置："+id);
    public double Fertility(string block)=>_soil.GetValueOrDefault(block,BlockCatalog.IsSoil(block)?1:0);
    public static PlantGrowthRules Load(Stream stream)
    {
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=100_000});
        var root=XDocument.Load(reader).Root;if(root?.Name!="PlantGrowth")throw new InvalidDataException("无效作物生长配置");
        var rules=new PlantGrowthRules();
        double Number(XElement e,string name,double min,double max)
        {if(!double.TryParse((string?)e.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n)||n<min||n>max)throw new InvalidDataException("作物生长参数无效："+name);return n;}
        foreach(var e in root.Elements("Crop"))
        {
            string id=(string?)e.Attribute("id")??"";
            double start=Number(e,"restStart",-1,23),end=Number(e,"restEnd",-1,23);
            if(start!=Math.Truncate(start)||end!=Math.Truncate(end)||(start<0)!=(end<0)||start>=0&&start==end)throw new InvalidDataException("作物休眠时段无效");
            if(!bool.TryParse((string?)e.Attribute("dark"),out var dark))throw new InvalidDataException("作物暗生参数无效");
            var rule=new PlantGrowthRule(Number(e,"minTemperature",-100,100),Number(e,"idealLow",-100,100),Number(e,"idealHigh",-100,100),Number(e,"maxTemperature",-100,100),Number(e,"minLight",0,1),Number(e,"optimalLight",.001,1),Number(e,"fertilitySensitivity",0,2),Number(e,"minFertility",0,3),(int)start,(int)end,dark,Number(e,"wildChance",0,1));
            if(id.Length is <1 or >100||rule.MinTemperature>=rule.IdealLow||rule.IdealLow>rule.IdealHigh||rule.IdealHigh>=rule.MaxTemperature||rule.MinLight>=rule.OptimalLight||!rules._crops.TryAdd(id,rule))throw new InvalidDataException("作物生长范围或 ID 无效");
        }
        foreach(var e in root.Elements("Soil"))if(!rules._soil.TryAdd((string?)e.Attribute("block")??throw new InvalidDataException("缺少土壤 ID"),Number(e,"fertility",0,3)))throw new InvalidDataException("重复土壤肥力定义");
        foreach(var crop in CropCatalog.All.Where(c=>!c.Tree))rules.Crop(crop.Id);
        return rules;
    }
    private static readonly Lazy<PlantGrowthRules> Rules=new(()=>
    {
        string? path=new[]{"Assets/Definitions/plant-growth.xml","source/Assets/Definitions/plant-growth.xml",Path.Combine(AppContext.BaseDirectory,"Assets/Definitions/plant-growth.xml")}.FirstOrDefault(File.Exists);
        using var stream=path is null?typeof(PlantGrowthRules).Assembly.GetManifestResourceStream("NoDwarf.plant-growth.xml")??throw new InvalidDataException("缺少 plant-growth.xml"):File.OpenRead(path);
        return Load(stream);
    });
    public static PlantGrowthRules Default=>Rules.Value;
}
