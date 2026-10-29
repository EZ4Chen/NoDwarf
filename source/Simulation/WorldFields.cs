using System.Runtime.CompilerServices;
namespace NoDwarf.Simulation;

// Versioned, immutable atlas fields. A priority flood gives every land tile an
// acyclic outlet, including depressions; reverse traversal accumulates runoff.
internal sealed class WorldFields
{
    private static readonly ConditionalWeakTable<WorldGenerationSettings,WorldFields> Instances=new();
    private static readonly Dictionary<WorldGenerationSettings,WorldFields> Cache=new();
    private static readonly (int X,int Y)[] Directions={(1,0),(-1,0),(0,1),(0,-1)};
    public AtlasTile[,] Tiles {get;}
    public static WorldFields For(WorldGenerationSettings settings)=>Instances.GetValue(settings,s=>
    {
        var key=s with {SiteX=0,SiteY=0};
        lock(Cache)
        {
            if(Cache.TryGetValue(key,out var data))return data;
            data=new(key);if(Cache.Count>=4)Cache.Remove(Cache.Keys.First());Cache.Add(key,data);return data;
        }
    });
    private WorldFields(WorldGenerationSettings s)
    {
        s.Validate(false);
        int n=s.WorldSize,count=n*n;
        var heights=new double[count];var rain=new double[count];var drainage=new double[count];var land=new bool[count];
        double scale=12*Math.Pow(1.5,s.FeatureScale),rough=.13+(s.Ruggedness+2)*.075;
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            int i=y*n+x;
            heights[i]=s.Noise(x/scale+4,y/scale+11)*(1-rough)+s.Noise(x/(scale*.28)+21,y/(scale*.28)+7)*rough;
            rain[i]=Math.Clamp(s.Noise(x/11d+61,y/11d+43)+s.Moisture*.17,.03,.97);
            drainage[i]=Math.Clamp(s.Noise(x/8d+113,y/8d+157)+s.Drainage*.17,.03,.97);
        }
        // Quantiles control land coverage independently from altitude.
        double sea=heights.Order().ElementAt(count*(100-s.LandPercent)/100);
        for(int i=0;i<count;i++){land[i]=heights[i]>sea;heights[i]=land[i]?65+(heights[i]-sea)*65+s.Elevation*4:60;}
        for(int pass=0;pass<s.Erosion*2;pass++)
        {
            var previous=(double[])heights.Clone();
            for(int y=1;y<n-1;y++)for(int x=1;x<n-1;x++)
            {
                int i=y*n+x;if(!land[i])continue;
                double mean=(previous[i-1]+previous[i+1]+previous[i-n]+previous[i+n])*.25;
                heights[i]=previous[i]+(mean-previous[i])*.2*rain[i];
            }
        }
        var filled=(double[])heights.Clone();var receiver=Enumerable.Repeat(-1,count).ToArray();var visited=new bool[count];
        var order=new List<int>(count);var queue=new PriorityQueue<int,(double,int)>();
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            int i=y*n+x;if(land[i]&&x>0&&y>0&&x<n-1&&y<n-1)continue;
            visited[i]=true;queue.Enqueue(i,(filled[i],i));
        }
        while(queue.TryDequeue(out int i,out _))
        {
            order.Add(i);int x=i%n,y=i/n;
            foreach(var d in Directions)
            {
                int nx=x+d.X,ny=y+d.Y;if(nx<0||ny<0||nx>=n||ny>=n)continue;
                int j=ny*n+nx;if(visited[j])continue;
                visited[j]=true;receiver[j]=i;filled[j]=Math.Max(heights[j],filled[i]);queue.Enqueue(j,(filled[j],j));
            }
        }
        var flow=new double[count];
        for(int i=0;i<count;i++)flow[i]=land[i]?rain[i]*(.25+.75*drainage[i]):0;
        for(int p=order.Count-1;p>=0;p--){int i=order[p];if(receiver[i]>=0)flow[receiver[i]]+=flow[i];}
        Tiles=new AtlasTile[n,n];
        double threshold=s.Rivers==2?12:28;
        for(int y=0;y<n;y++)for(int x=0;x<n;x++)
        {
            int i=y*n+x;bool river=land[i]&&s.Rivers>0&&flow[i]>=threshold;
            bool lake=land[i]&&filled[i]-heights[i]>=1.2&&rain[i]>.35&&drainage[i]<.7;
            int height=Math.Clamp((int)Math.Round((lake||river?filled[i]:heights[i])-(river?s.Erosion*.6:0)),60,108);
            double wet=Math.Clamp(rain[i]*(1.25-drainage[i]*.5)+(lake?.15:0),.03,.97);
            double latitude=Math.Abs((y+.5)/n*2-1);
            double temperature=Math.Round(29-latitude*42+s.Temperature*7-(height-64)*.22+(s.Noise(x/12d+8,y/12d+99)-.5)*7,1);
            string region=height>=96?"alpine":temperature<-2?"tundra":temperature<6?"boreal":temperature>20&&wet<.22?"desert":wet<.3?"arid":temperature>22?(wet>.65?"rainforest":"savanna"):wet>.78?"wetland":wet<.48?"grassland":"temperate";
            int next=receiver[i];
            Tiles[x,y]=new(land[i],region,temperature,wet,height){Rainfall=rain[i],Drainage=drainage[i],Runoff=flow[i],Lake=lake,
                RiverWidth=river?Math.Clamp(3+(int)Math.Sqrt(flow[i]/threshold),4,10):0,DownstreamX=next<0?-1:next%n,DownstreamY=next<0?-1:next/n};
        }
    }
}
