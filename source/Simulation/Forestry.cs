namespace NoDwarf.Simulation;
public sealed class TreeState
{
    public string Species {get;set;}="sapling";
    public Cell Cell {get;set;}
    public double Age {get;set;}=600;
    public double GrowthUpdatedAt {get;set;}
    public double NextGrowthAt {get;set;}
    public int MaximumHeight {get;set;}=10;
    public static int MatureHeight(string species)=>species switch {"acacia_sapling"=>6,"birch_sapling"=>8,"pine_sapling" or "jungle_sapling"=>14,_=>10};
    public int Height=>Math.Min(MaximumHeight,Age<CropCatalog.Get(Species).Seconds*.2?1:Age<CropCatalog.Get(Species).Seconds*.5?3:Age<CropCatalog.Get(Species).Seconds?6:MaximumHeight);
}
public sealed partial class DemoWorld
{
    public Dictionary<Cell,TreeState> Trees {get;}=new();
    private readonly ScheduledUpdates<Cell> _treeUpdates=new(CellUpdateOrder.Instance);
    public long TreeGrowthVisits {get;private set;}
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
        (!Farms.ContainsKey(c)||CropCatalog.IsTree(Farms[c].Crop)&&GrowingZoneAt(c)?.AllowSowing==true)&&!Stockpiles.Contains(c)&&!Plants.ContainsKey(c)&&BlockCatalog.IsSoil(BlockAt(c+new Cell(0,0,-1)))&&
        Enumerable.Range(0,14).All(y=>Inside(c+new Cell(0,0,y))&&!Occupied(c+new Cell(0,0,y)))&&
        !Jobs.Any(j=>j.Construction&&j.Cell.X==c.X&&j.Cell.Z==c.Z);
    public bool QueueTree(Cell c,bool chop,string species="sapling",bool announce=true)
    {
        if(!Inside(c)||!CropCatalog.IsTree(species))return false;
        if(chop)c=Surface(c.X,c.Z);
        if(Jobs.Any(j=>j.Forestry&&j.Cell==c)||!(chop?Trees.ContainsKey(c):Plantable(c)))
        {if(announce)Log(chop?"这里没有可砍伐的树。":"树苗需要露天土壤、上方空间且树干间距至少 4 米（含待种树苗）。");return false;}
        var job=new DemoJob{Id=++_nextJob,Cell=c,Chop=chop,PlantTree=!chop,TreeSpecies=species};
        if(!chop&&!Reserve(job,new Dictionary<string,int>{{CropSeed(species),1}})){Log("缺少实体树苗。");return false;}
        Jobs.Add(job);Revision++;if(announce)Log(chop?"已安排伐木。":"已安排种植树苗，生长时间取决于树种。");return true;
    }
    private bool ForestryReady(DemoJob j)=>j.Chop?Trees.ContainsKey(j.Cell):Plantable(j.Cell);
    private void CompleteForestry(DemoJob j)
    {
        if(j.Chop)
        {
            FellTree(j.Cell);
        }
        else{ConsumeMaterials(j);Trees[j.Cell]=new(){Cell=j.Cell,Age=0,Species=j.TreeSpecies,MaximumHeight=TreeState.MatureHeight(j.TreeSpecies)};ScheduleTreeGrowth(Trees[j.Cell],SimulationTime);Terrain.SetTree(j.Cell,true);}
        TreeRevision++;Revision++;
    }
    public bool FellTree(Cell root)
    {
        if(!Trees.Remove(root,out var tree))return false;
        _treeUpdates.Cancel(root);_blockDamage.Remove(root);Terrain.SetTree(root,false);
        if(TreeFalls.Count>=32)TreeFalls.Dequeue();TreeFalls.Enqueue(new(tree,Terrain.Seed));
        SpawnItem("wood",Math.Max(1,tree.Height*2),root);
        SpawnItem(CropSeed(tree.Species),tree.Age>=GrowTime(tree.Species)?2:1,root);
        SettleItems();TreeRevision++;Revision++;return true;
    }
    private static double NextTreeAge(TreeState tree)
    {
        double mature=GrowTime(tree.Species);
        return tree.Age<mature*.2?mature*.2:tree.Age<mature*.5?mature*.5:mature;
    }
    private void ScheduleTreeGrowth(TreeState tree,double now)
    {
        tree.GrowthUpdatedAt=now;
        tree.NextGrowthAt=now+Math.Max(.001,NextTreeAge(tree)-tree.Age);
        _treeUpdates.Schedule(tree.Cell,tree.NextGrowthAt);
    }
    public double TreeAge(TreeState tree)=>Math.Min(NextTreeAge(tree),tree.Age+Math.Max(0,SimulationTime-tree.GrowthUpdatedAt));
    private void UpdateTrees(double dt)
    {
        for(int budget=32;budget>0&&_treeUpdates.TryTake(SimulationTime,out var c);budget--)
        {
            if(!Trees.TryGetValue(c,out var tree))continue;
            TreeGrowthVisits++;
            int height=tree.Height;double previous=tree.Age,deadline=tree.NextGrowthAt;
            tree.Age=NextTreeAge(tree);
            bool blocked=false;
            for(int y=height;y<tree.Height;y++)
            {var cell=c+new Cell(0,0,y);if(Buildings.ContainsKey(cell)||Jobs.At(cell).Any(j=>j.Construction)){blocked=true;break;}}
            if(blocked)
            {
                tree.Age=previous;tree.GrowthUpdatedAt=SimulationTime;tree.NextGrowthAt=SimulationTime+1;
                _treeUpdates.Schedule(c,tree.NextGrowthAt);continue;
            }
            if(tree.Height!=height){Terrain.SetTree(c,true);TreeRevision++;Revision++;}
            tree.GrowthUpdatedAt=deadline;
            if(tree.Age<GrowTime(tree.Species))ScheduleTreeGrowth(tree,deadline);
        }
    }
}
