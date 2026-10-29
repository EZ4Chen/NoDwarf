namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    private List<DemoWorker>? _removedResidents;
    public IEnumerable<Cell> StairObstructions(Cell c)=>Enumerable.Range(0,3).Select(h=>c+new Cell(0,0,h)).Where(Occupied);
    public bool CanDeveloperPlace(Cell c,BuildKind kind,int facing=0) =>
        (kind!=BuildKind.Ladder||Solid(c+StairDirection(facing)))&&Inside(c)&&c.Level>TerrainMap.MinLevel&&c.Level<=TerrainMap.MaxLevel&&!Occupied(c)&&Fluids.Amount(c)==0&&
        !(TallFixture(kind)&&Occupied(c+new Cell(0,0,1)))&&
        !(BlockCatalog.IsStairs(kind)&&(Occupied(c+new Cell(0,0,1))||Occupied(c+new Cell(0,0,2))))&&!Jobs.Any(j=>j.Construction&&j.Cell==c)&&!Actors.Any(w=>IntersectsWorker(c,kind,w));
    public bool DeveloperPlace(Cell c, BuildKind kind, int facing=0)
    {
        if(!CanDeveloperPlace(c,kind,facing))
        {Log("立即建造失败：位置被占用、超出边界或与居民重叠。");return false;}
        var nearby=Directions.Select(d=>c+d).FirstOrDefault(Walkable);
        if(Inside(nearby)&&Walkable(nearby))DisplaceItems(c,nearby,TallFixture(kind)?2:1);
        Buildings[c]=new Building(c,kind,facing);Revision++;ValidateWorkshop();
        Log($"[开发] 放置 {BlockCatalog.Id(kind)} @ {c}，未消耗库存。");return true;
    }
    public bool DeveloperDig(Cell c,bool stairs=false,int facing=0,int height=1)
    {
        _supportSafety.Clear();
        if(Trees.ContainsKey(c))return DamageBlock(c,double.MaxValue);
        if(!CanDig(c,stairs,height)) {Log("立即挖掘失败：没有可挖岩土或与现有物件重叠。");return false;}
        var job=new DemoJob{Cell=c,Dig=true,DigHeight=stairs?3:height,Facing=facing};
        job.AcceptedWaterRisk=DigWaterRisk(job); // Explicit developer terrain editing bypasses gameplay confirmation.
        if(!DigSafe(job)){Log("立即挖掘失败：居民或建筑正在使用这处支撑。");return false;}
        for(int i=0;i<job.DigHeight;i++) MineWithDrop(c+new Cell(0,0,i));
        SettleItems();Revision++;ValidateWorkshop();Log($"[开发] 立即开凿 @ {c}，已生成对应掉落。");return true;
    }
    public bool DeveloperErase(Cell c)
    {
        if(Trees.ContainsKey(c))return DamageBlock(c,double.MaxValue);
        if(Jobs.Any(j=>j.Construction&&j.Cell==c)){Erase(c);return true;}
        if(!Buildings.TryGetValue(c,out var b))return false;
        if(BlockCatalog.IsWall(b.Kind)&&Workers.Any(w=>(w.Tile==c+new Cell(0,0,1)||(w.Path.Count>0&&w.Path.Peek()==c+new Cell(0,0,1)))))
        {Log("先移开楼板上的居民再拆除。");return false;}
        _supportSafety.Clear();if(RemovalEndangersResident(c)){Log("先移开受支撑建筑上的居民再拆除。");return false;}
        _blockDamage.Remove(c);Buildings.Remove(c);SettleItems();Revision++;ValidateWorkshop();Log($"[开发] 移除 {b.BlockId} @ {c}，未增加库存。");return true;
    }
    public bool DeveloperResidents(bool present)
    {
        if(!present)
        {
            if(Workers.Count==0)return true;
            foreach(var j in Jobs.ToArray())Release(j);
            foreach(var w in Workers)StopMovement(w);
            _removedResidents=Workers.ToList();Workers.Clear();Revision++;Log("[开发] 已移除居民；可继续直接编辑世界。");return true;
        }
        if(_removedResidents is null)return true;
        var positions=new List<Cell>();
        foreach(var w in _removedResidents)
        {
            Cell? spawn=Walkable(w.Tile)?w.Tile:null;
            for(int radius=0;radius<16&&spawn is null;radius++)for(int x=Camp.X-radius;x<=Camp.X+radius&&spawn is null;x++)for(int z=Camp.Z-radius;z<=Camp.Z+radius;z++)
            {if(!Inside(new Cell(x,z)))continue;var c=Surface(x,z);if(Walkable(c)){spawn=c;break;}}
            if(spawn is null){Log("恢复居民失败：营地附近没有可站立位置。");return false;}
            positions.Add(spawn.Value);
        }
        for(int i=0;i<_removedResidents.Count;i++)
        {var w=_removedResidents[i];w.X=positions[i].X;w.Z=positions[i].Z;w.Level=positions[i].Level;Workers.Add(w);}
        _removedResidents=null;Revision++;Log("[开发] 居民已恢复。");return true;
    }
    public int DeveloperFinishPlans()
    {
        int completed=0;
        while(completed<512)
        {
            Demo?.Advance(this);Underground?.Advance(this);
            var job=Jobs.FirstOrDefault(j=>j.Construction && (j.Build!=BuildKind.Ladder||Solid(j.Cell+StairDirection(j.Facing))) && (j.Dig?DigSafe(j):Fluids.Amount(j.Cell)==0&&Supported(j.Cell)&&!Actors.Any(w=>IntersectsWorker(j.Cell,j.Build!.Value,w))));
            if(job is null)break;
            Complete(job,new DemoWorker{Name="开发者"});completed++;
        }
        Log($"[开发] 完成 {completed} 个规划；剩余 {Jobs.Count} 个任务。");return completed;
    }
}
