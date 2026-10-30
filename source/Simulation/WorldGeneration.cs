namespace NoDwarf.Simulation;

// A small, deterministic atlas. Only the selected tile becomes a simulated colony map.
public sealed record WorldGenerationSettings
{
    public const int CurrentRevision=2;
    [System.Text.Json.Serialization.JsonRequired] public int Revision {get;init;}=CurrentRevision;
    public int WorldSize {get;init;}=129;
    public int LandPercent {get;init;}=50;
    public int FeatureScale {get;init;}
    public int Ruggedness {get;init;}
    public int Drainage {get;init;}
    public int Erosion {get;init;}=1;
    public int Rivers {get;init;}=1;
    public int Minerals {get;init;}=1;
    public int SoilDepth {get;init;}=3;
    public int Aquifers {get;init;}=1;
    public int CavernLayers {get;init;}=1;
    public int CavernSize {get;init;}=1;
    [System.Text.Json.Serialization.JsonIgnore] public int Width=>WorldSize;
    [System.Text.Json.Serialization.JsonIgnore] public int Height=>WorldSize;
    public int Seed {get;init;}=74219;
    [System.Text.Json.Serialization.JsonRequired] public int EcologyVersion {get;init;}=TerrainMap.CurrentEcologyVersion;
    public int Temperature {get;init;}
    public int Moisture {get;init;}
    public int Elevation {get;init;}
    public int SiteX {get;init;}=36;
    public int SiteY {get;init;}=22;
    public void Validate(bool requireLand=true)
    {
        if(Revision!=CurrentRevision||WorldSize is not (65 or 129 or 257)||LandPercent is <25 or >75||
           FeatureScale is <-2 or >2||Ruggedness is <-2 or >2||Drainage is <-2 or >2||Erosion is <0 or >3||Rivers is <0 or >2||
           Minerals is <0 or >2||SoilDepth is <1 or >8||Aquifers is <0 or >2||CavernLayers is <0 or >3||CavernSize is <0 or >2||
           EcologyVersion!=TerrainMap.CurrentEcologyVersion||Temperature is <-2 or >2||Moisture is <-2 or >2||Elevation is <-2 or >2||SiteX<0||SiteX>=Width||SiteY<0||SiteY>=Height)
            throw new InvalidDataException("世界参数超出范围。");
        if(requireLand&&!Site.Land)throw new InvalidDataException("请选择陆地上的落脚点。");
    }
    public AtlasTile Site=>Tile(SiteX,SiteY);
    public int LocalSeed=>unchecked(Seed*397^SiteX*73856093^SiteY*19349663);
    public AtlasTile Tile(int x,int y)=>WorldFields.For(this).Tiles[x,y];
    public RegionProfile Profile()
    {
        Validate();var site=Site;var basis=RegionProfile.Get(site.Region);
        return basis with {Mean=site.Temperature,Humidity=site.Wetness,CampHeight=site.Height,TreeChance=basis.TreeChance==0?0:Math.Clamp(basis.TreeChance*(.25+site.Wetness*1.5),0,.4)};
    }
    internal double Noise(double x,double y)
    {
        int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);double tx=x-ix,ty=y-iy;tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);
        return (Hash(ix,iy)*(1-tx)+Hash(ix+1,iy)*tx)*(1-ty)+(Hash(ix,iy+1)*(1-tx)+Hash(ix+1,iy+1)*tx)*ty;
    }
    private double Hash(int x,int y)
    {unchecked{uint h=(uint)(x*374761393+y*668265263+Seed*1274126177);h=(h^(h>>13))*1274126177;return (h^(h>>16))/(double)uint.MaxValue;}}
    public WorldGenerationSettings WithRecommendedSite()
    {
        var options=from x in Enumerable.Range(0,Width) from y in Enumerable.Range(0,Height) let t=Tile(x,y) where t.Land
                    orderby Math.Abs(t.Temperature-15)+Math.Abs(t.Wetness-.55)*24+Math.Max(0,t.Height-82)*2 select (x,y);
        var best=options.First();return this with {SiteX=best.x,SiteY=best.y};
    }
}
public readonly record struct AtlasTile(bool Land,string Region,double Temperature,double Wetness,int Height)
{
    public double Rainfall {get;init;}
    public double Drainage {get;init;}
    public double Runoff {get;init;}
    public int DownstreamX {get;init;}=-1;
    public int DownstreamY {get;init;}=-1;
    public int RiverWidth {get;init;}
    public bool Lake {get;init;}
}
