namespace NoDwarf.Simulation;
public sealed class TreeState
{
    public string Species {get;set;}="sapling";
    public Cell Cell {get;set;}
    public double Age {get;set;}=600;
    public int MaximumHeight {get;set;}=10;
    public static int MatureHeight(string species)=>species switch {"acacia_sapling"=>6,"birch_sapling"=>8,"pine_sapling" or "jungle_sapling"=>14,_=>10};
    public int Height=>Math.Min(MaximumHeight,Age<CropCatalog.Get(Species).Seconds*.2?1:Age<CropCatalog.Get(Species).Seconds*.5?3:Age<CropCatalog.Get(Species).Seconds?6:MaximumHeight);
}
public sealed partial class DemoWorld
{
    public Dictionary<Cell,TreeState> Trees {get;}=new();
    private readonly HashSet<Cell> _youngTrees=new();
    public Queue<TreeFall> TreeFalls {get;}=new();
    public int TreeRevision {get;private set;}
    private void SeedTrees()
    {
        for(int x=0;x<Width;x++)for(int z=0;z<Depth;z++)if(Terrain[x,z].Tree)
        {var c=Surface(x,z);string species=new[]{"sapling","birch_sapling","pine_sapling"}[(int)(Terrain.Hash(x+31,z+72)*3)%3];species=Terrain.Region?.TreeSpecies(Terrain.Hash(x+31,z+72))??species;Trees[c]=new(){Cell=c,Species=species,Age=GrowTime(species),MaximumHeight=Terrain.Region?.ShortTrees is >0?Terrain.Region.ShortTrees:TreeState.MatureHeight(species)};}
    }
    public int TreeHeight(Cell c)=>Trees.TryGetValue(c,out var tree)?tree.Height:0;
    public const int TreeSpacing=4;
    private bool TreeSpacingAvailable(Cell c)
    {
        for(int x=Math.Max(0,c.X-3);x<=Math.Min(Width-1,c.X+3);x++)for(int z=Math.Max(0,c.Z-3);z<=Math.Min(Depth-1,c.Z+3);z++)
            if(x!=c.X||z!=c.Z)if(Terrain[x,z].Tree)return false;
        return !Jobs.Any(j=>j.PlantTree&&j.Cell!=c&&Math.Abs(j.Cell.X-c.X)<TreeSpacing&&Math.Abs(j.Cell.Z-c.Z)<TreeSpacing);
    }
    private bool Plantable(Cell c)=>TreeSpacingAvailable(c)&&Inside(c)&&Terrain.Contains(c)&&!NaturalRamp(c)&&!Terrain[c.X,c.Z].Tree&&
        (!Farms.ContainsKey(c)||CropCatalog.IsTree(Farms[c].Crop)&&GrowingZoneAt(c)?.AllowSowing==true)&&!Stockpiles.Contains(c)&&BlockCatalog.IsSoil(BlockAt(c+new Cell(0,0,-1)))&&
        Enumerable.Range(0,14).All(y=>Inside(c+new Cell(0,0,y))&&!Occupied(c+new Cell(0,0,y)))&&
        !Jobs.Any(j=>j.Construction&&j.Cell.X==c.X&&j.Cell.Z==c.Z);
    public bool QueueTree(Cell c,bool chop,string species="sapling",bool announce=true)
    {
        if(!Inside(c)||!CropCatalog.IsTree(species))return false;
        if(chop)c=Surface(c.X,c.Z);
        if(Jobs.Any(j=>j.Forestry&&j.Cell==c)||!(chop?Trees.ContainsKey(c):Plantable(c)))
        {if(announce)Log(chop?"这里没有可砍伐的树。":"树苗需要露天土壤、上方空间且树干间距至少 4 米（含待种树苗）。");return false;}
        var job=new DemoJob{Id=++_nextJob,Cell=c,Chop=chop,PlantTree=!chop,TreeSpecies=species};
        if(!chop&&!Reserve(job,new(){{CropSeed(species),1}})){Log("缺少实体树苗。");return false;}
        Jobs.Add(job);Revision++;if(announce)Log(chop?"已安排伐木。":"已安排种植树苗，生长时间取决于树种。");return true;
    }
    private bool ForestryReady(DemoJob j)=>j.Chop?Trees.ContainsKey(j.Cell):Plantable(j.Cell);
    private void CompleteForestry(DemoJob j)
    {
        if(j.Chop)
        {
            FellTree(j.Cell);
        }
        else{ConsumeMaterials(j);Trees[j.Cell]=new(){Cell=j.Cell,Age=0,Species=j.TreeSpecies,MaximumHeight=TreeState.MatureHeight(j.TreeSpecies)};_youngTrees.Add(j.Cell);Terrain.SetTree(j.Cell,true);}
        TreeRevision++;Revision++;
    }
    public bool FellTree(Cell root)
    {
        if(!Trees.Remove(root,out var tree))return false;
        _youngTrees.Remove(root);_blockDamage.Remove(root);Terrain.SetTree(root,false);
        if(TreeFalls.Count>=32)TreeFalls.Dequeue();TreeFalls.Enqueue(new(tree,Terrain.Seed));
        SpawnItem("wood",Math.Max(1,tree.Height*2),root);
        SpawnItem(CropSeed(tree.Species),tree.Age>=GrowTime(tree.Species)?2:1,root);
        SettleItems();TreeRevision++;Revision++;return true;
    }
    private void UpdateTrees(double dt)
    {
        foreach(var c in _youngTrees.ToArray())
        {
            var tree=Trees[c];int h=tree.Height;double age=tree.Age;tree.Age=Math.Min(GrowTime(tree.Species),tree.Age+dt);
            if(tree.Height>h&&Enumerable.Range(h,tree.Height-h).Any(y=>Buildings.ContainsKey(c+new Cell(0,0,y))||Jobs.Any(j=>j.Construction&&j.Cell==c+new Cell(0,0,y)))){tree.Age=age;continue;}
            if(tree.Height!=h){Terrain.SetTree(c,true);TreeRevision++;Revision++;}
            if(tree.Age>=GrowTime(tree.Species))_youngTrees.Remove(c);
        }
    }
}
