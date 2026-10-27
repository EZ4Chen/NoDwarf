namespace NoDwarf.Simulation;
public readonly record struct TreeVoxel(Cell Offset,bool Leaf);
public static class TreeShape
{
    // Stable integer mixing: independent of runtime string hashes and iteration order.
    public static uint Seed(int worldSeed,Cell c)=>unchecked((uint)worldSeed*747796405u^(uint)c.X*2891336453u^(uint)c.Z*277803737u^(uint)c.Level*1597334677u);
    public static IReadOnlyList<TreeVoxel> Generate(int seed,TreeState tree)
    {
        uint state=Seed(seed,tree.Cell);int Next(int count){state=unchecked(state*1664525u+1013904223u);return (int)((state>>8)%(uint)count);}
        int h=tree.Height;
        if(tree.Species=="acacia_sapling"&&h>=5)return Acacia(tree,Next(4),Next(2));
        var blocks=new Dictionary<Cell,bool>();
        for(int y=0;y<Math.Max(1,h-1);y++)blocks[new(0,0,y)]=false;
        int lower=Math.Max(1,h-(tree.Species=="pine_sapling"?Math.Max(3,h/2):3));
        for(int y=lower;y<h;y++)
        {
            int radius=h<4?1:tree.Species=="pine_sapling"?Math.Min(2,1+(h-1-y)/2):tree.Species=="jungle_sapling"?3:2;
            for(int x=-radius;x<=radius;x++)for(int z=-radius;z<=radius;z++)
            {
                if(Math.Abs(x)+Math.Abs(z)>radius+1||Math.Abs(x)+Math.Abs(z)==radius+1&&Next(3)==0)continue;
                var c=new Cell(x,z,y);var absolute=tree.Cell+c;
                if(TerrainMap.InBounds(absolute))blocks.TryAdd(c,true);
            }
        }
        if(h>=6)for(int i=0;i<2;i++)
        {int dir=Next(4),y=h-3+Next(2);var d=new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)}[dir];var c=new Cell(d.X,d.Z,y);if(TerrainMap.InBounds(tree.Cell+c))blocks[c]=false;}
        return blocks.Select(p=>new TreeVoxel(p.Key,p.Value)).OrderBy(v=>v.Offset.Level).ThenBy(v=>v.Offset.X).ThenBy(v=>v.Offset.Z).ToArray();
    }
    private static IReadOnlyList<TreeVoxel> Acacia(TreeState tree,int direction,int variant)
    {
        var d=new[]{new Cell(1,0),new Cell(0,1),new Cell(-1,0),new Cell(0,-1)}[direction];
        var cells=new Dictionary<Cell,bool>();int h=tree.Height;
        void Add(Cell c,bool leaf){if(TerrainMap.InBounds(tree.Cell+c)&&(!cells.ContainsKey(c)||!leaf))cells[c]=leaf;}
        for(int y=0;y<h-1;y++)Add(new Cell(0,0,y),false);
        for(int i=1;i<=2;i++)Add(new Cell(d.X*i,d.Z*i,h-3+i/2),false);
        Add(new Cell(-d.X,-d.Z,h-3),false);
        foreach(var center in new[]{new Cell(d.X*2,d.Z*2,h-2),new Cell(-d.X,-d.Z,h-2)})
            for(int y=0;y<2;y++)for(int x=-2;x<=2;x++)for(int z=-2;z<=2;z++)
                if(Math.Abs(x)+Math.Abs(z)<=(y==0?3:2+variant))Add(center+new Cell(x,z,y),true);
        return cells.Select(p=>new TreeVoxel(p.Key,p.Value)).OrderBy(v=>v.Offset.Level).ThenBy(v=>v.Offset.X).ThenBy(v=>v.Offset.Z).ToArray();
    }
}
public sealed record TreeFall(TreeState Tree,int Seed);
