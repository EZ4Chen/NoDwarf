namespace NoDwarf.Simulation;

// A small, deterministic atlas. Only the selected tile becomes a simulated colony map.
public sealed record WorldGenerationSettings
{
    public const int AtlasWidth=72,AtlasHeight=44;
    public int Seed {get;init;}=74219;
    public int EcologyVersion {get;init;}=2;
    public int Temperature {get;init;}
    public int Moisture {get;init;}
    public int Elevation {get;init;}
    public int SiteX {get;init;}=36;
    public int SiteY {get;init;}=22;
    public void Validate(bool requireLand=true)
    {
        if(EcologyVersion is not (1 or 2)||Temperature is <-2 or >2||Moisture is <-2 or >2||Elevation is <-2 or >2||SiteX is <0 or >=AtlasWidth||SiteY is <0 or >=AtlasHeight)
            throw new InvalidDataException("世界参数超出范围。");
        if(requireLand&&!Site.Land)throw new InvalidDataException("请选择陆地上的落脚点。");
    }
    public AtlasTile Site=>Tile(SiteX,SiteY);
    public int LocalSeed=>unchecked(Seed*397^SiteX*73856093^SiteY*19349663);
    public AtlasTile Tile(int x,int y)
    {
        double continental=Noise(x/15d+4,y/15d+11)*.72+Noise(x/6d+21,y/6d+7)*.28;
        bool land=continental+Elevation*.045>.39;
        int height=Math.Clamp(64+(int)Math.Round((continental-.39)*80)+Elevation*5,60,108);
        double wet=Math.Clamp(Noise(x/9d+61,y/9d+43)+Moisture*.17,.03,.97);
        double latitude=Math.Abs((y+.5)/AtlasHeight*2-1);
        double temperature=Math.Round(29-latitude*42+Temperature*7-(height-64)*.22+(Noise(x/12d+8,y/12d+99)-.5)*7,1);
        string region=height>=96?"alpine":temperature<6?"boreal":wet<.32||temperature>22?"savanna":"temperate";
        if(EcologyVersion>=2)region=height>=96?"alpine":temperature<-2?"tundra":temperature<6?"boreal":
            temperature>20&&wet<.22?"desert":wet<.3?"arid":temperature>22?(wet>.65?"rainforest":"savanna"):
            wet>.78?"wetland":wet<.48?"grassland":"temperate";
        return new(land,region,temperature,wet,height);
    }
    public RegionProfile Profile()
    {
        Validate();var site=Site;var basis=RegionProfile.Get(site.Region,EcologyVersion);
        return basis with {Mean=site.Temperature,Humidity=site.Wetness,CampHeight=site.Height,TreeChance=basis.TreeChance==0?0:Math.Clamp(basis.TreeChance*(.25+site.Wetness*1.5),EcologyVersion==1?.001:0,.4)};
    }
    private double Noise(double x,double y)
    {
        int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);double tx=x-ix,ty=y-iy;tx=tx*tx*(3-2*tx);ty=ty*ty*(3-2*ty);
        return (Hash(ix,iy)*(1-tx)+Hash(ix+1,iy)*tx)*(1-ty)+(Hash(ix,iy+1)*(1-tx)+Hash(ix+1,iy+1)*tx)*ty;
    }
    private double Hash(int x,int y)
    {unchecked{uint h=(uint)(x*374761393+y*668265263+Seed*1274126177);h=(h^(h>>13))*1274126177;return (h^(h>>16))/(double)uint.MaxValue;}}
    public WorldGenerationSettings WithRecommendedSite()
    {
        var options=from x in Enumerable.Range(0,AtlasWidth) from y in Enumerable.Range(0,AtlasHeight) let t=Tile(x,y) where t.Land
                    orderby Math.Abs(t.Temperature-15)+Math.Abs(t.Wetness-.55)*24+Math.Max(0,t.Height-82)*2 select (x,y);
        var best=options.First();return this with {SiteX=best.x,SiteY=best.y};
    }
}
public readonly record struct AtlasTile(bool Land,string Region,double Temperature,double Wetness,int Height);
