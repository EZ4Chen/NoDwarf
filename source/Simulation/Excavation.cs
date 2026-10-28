namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    public UndergroundDemo? Underground { get; private set; }
    public bool CanDig(Cell c,bool stairs=false,int height=1)
    {
        height=stairs?3:height;
        if(Trees.ContainsKey(c))return height==1;
        if(height is <1 or >3)return false;
        if(!Inside(c)||c.Level<=TerrainMap.MinLevel||c.Level>TerrainMap.MaxLevel) return false;
        var cells=Enumerable.Range(0,height).Select(y=>c+new Cell(0,0,y)).ToArray();
        if(cells.Any(p=>!Inside(p)))return false;
        if(!cells.Any(p=>NaturalSolid(p)||NaturalRamp(p))) return false;
        if(cells.Any(p=>Buildings.ContainsKey(p)||Jobs.Any(j=>j.Construction&&j.Cell.X==p.X&&j.Cell.Z==p.Z&&p.Level>=j.Cell.Level&&p.Level<j.Cell.Level+(j.Dig?j.DigHeight: TallFixture(j.Build!.Value)?2:1)))) return false;
        if(Terrain[c.X,c.Z].Tree && cells.Any(p=>p.Level>=Terrain[c.X,c.Z].Level-1))return false;
        return true;
    }
    public bool QueueDig(Cell c,bool stairs=false,int facing=0,bool announce=true,int height=1)
    {
        if(Trees.ContainsKey(c))return QueueTree(c,true);
        if(!CanDig(c,stairs,height)) { if(announce)Log("此处不能挖掘：需要天然岩土，且不能覆盖建筑、其他挖掘任务或树根。");return false; }
        Jobs.Add(new DemoJob {Id=++_nextJob,Cell=c,Dig=true,DigHeight=stairs?3:height,Facing=facing});Revision++;
        if(announce)Log(stairs?"已规划楼梯坑：只挖出三米净空，完成后请另选材质安装楼梯。":"已规划挖掘一个方块（1 米），其他高度保持不变。");return true;
    }
    private static bool PlannedHazard(DemoJob j,Cell c) => j.Construction && (j.Dig
        ? c.X==j.Cell.X&&c.Z==j.Cell.Z&&c.Level-1>=j.Cell.Level&&c.Level-1<j.Cell.Level+j.DigHeight
        : j.Cell==c);
    private bool DigSafe(DemoJob job, HashSet<Cell>? residentFeet = null)
    {
        if(job.Demolish&&RemovalEndangersResident(job.Cell,residentFeet))return false;
        if(job.Demolish)return residentFeet is not null ? !residentFeet.Contains(job.Cell+new Cell(0,0,1)) : !Workers.Any(w=>w.Tile==job.Cell+new Cell(0,0,1)||(w.Path.TryPeek(out var next)&&next==job.Cell+new Cell(0,0,1)));
        // Protect residents; removing a building foundation now triggers gravity.
        for(int y=0;y<job.DigHeight;y++)
        {
            var c=job.Cell+new Cell(0,0,y);
            if(!NaturalSolid(c)&&!NaturalRamp(c))continue;
            if(Buildings.ContainsKey(c)||RemovalEndangersResident(c,residentFeet))return false;
            if(residentFeet is not null ? residentFeet.Contains(c+new Cell(0,0,1)) : Workers.Any(w=>w.Tile==c+new Cell(0,0,1)||(w.Path.TryPeek(out var next)&&next==c+new Cell(0,0,1))))return false;
        }
        return job.DigHeight==1 || Solid(job.Cell+new Cell(0,0,-1));
    }
    public bool StartUndergroundDemo()
    {
        if(Underground is not null) {Log("地下室示例已经开始。");return false;}
        for(int x=0;x<6;x++)for(int z=0;z<8;z++)
        {
            var c=Camp+new Cell(x,z);
            if(!Terrain.Contains(c)||NaturalRamp(c)||Terrain[c.X,c.Z].Tree||Buildings.Keys.Any(b=>b.X==c.X&&b.Z==c.Z)||Jobs.Any(j=>j.Cell.X==c.X&&j.Cell.Z==c.Z)||Terrain.Excavated.Any(b=>b.X==c.X&&b.Z==c.Z))
            {Log("地下室示例需要营地 6×8 范围保持原貌；请换一张空白地图。");return false;}
        }
        Underground=new UndergroundDemo(Camp);Underground.Advance(this);return true;
    }
}

public sealed class UndergroundDemo
{
    private readonly Cell _origin;
    public int Stage {get;private set;}
    internal void RestoreStage(int stage)=>Stage=stage;
    public bool Finished=>Stage==8;
    public UndergroundDemo(Cell origin)=>_origin=origin;
    public void Advance(DemoWorld world)
    {
        if(Finished||world.Jobs.Any(j=>j.Construction&&j.Cell.X>=_origin.X&&j.Cell.X<_origin.X+6&&j.Cell.Z>=_origin.Z&&j.Cell.Z<_origin.Z+8))return;
        if(Stage<6)
        {
            int segment=Stage/2;
            var c=_origin+new Cell(2,7-segment,-1-segment);
            if(Stage%2==0)
            {
                if(!world.QueueDig(c,true,1,false))return;
                world.Log($"地下室：挖出第 {segment+1}/3 段楼梯坑。");
            }
            else
            {
                if(!world.QueueBuild(c,BuildKind.CobbleStairs,false,1))return;
                world.Log($"地下室：安装第 {segment+1}/3 段圆石楼梯。");
            }
            Stage++;return;
        }
        if(Stage==6)
        {
            world.QueueDig(_origin+new Cell(2,4,-3),announce:false,height:2);
            for(int x=1;x<5;x++)for(int z=1;z<4;z++) world.QueueDig(_origin+new Cell(x,z,-3),announce:false,height:2);
            Stage++;world.Log("地下室：居民沿楼梯下去，逐格挖出两米净空的房间。");return;
        }
        world.QueueBuild(_origin+new Cell(2,4,-3),BuildKind.Door,false);
        world.QueueBuild(_origin+new Cell(1,1,-3),BuildKind.Furnace,false);
        world.QueueBuild(_origin+new Cell(4,1,-3),BuildKind.Anvil,false);
        Stage++;world.Log("地下空间已挖通，开始安装门、熔炉与铁砧。− 切到 F−1 查看。");
    }
}
