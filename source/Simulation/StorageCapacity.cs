namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    public const int CellStorageCapacity=100;
    public long StoredUnits(Cell cell)=>Items.Where(s=>s.Owner==""&&s.Cell==cell).Sum(s=>(long)s.Count*Content.Item(s.Item).StorageUnits);
    public long ReservedStorageUnits(Cell cell,DemoJob? except=null)=>Jobs.Where(j=>j!=except).Sum(j=>
        j.Haul&&j.Cell==cell&&!j.MaterialsPaused?j.Materials.Where(m=>m.Stage<2).Sum(m=>(long)m.Count*Content.Item(m.Item).StorageUnits):
        j.Production&&j.OutputCell==cell&&!j.MaterialsPaused?(long)JobRecipe(j).Count*Content.Item(JobRecipe(j).Output).StorageUnits:0);
    public int FreeStorageCount(Cell cell,string item,DemoJob? except=null)=>(int)Math.Max(0,(CellStorageCapacity-StoredUnits(cell)-ReservedStorageUnits(cell,except))/Content.Item(item).StorageUnits);
    private bool HaulSpaceAvailable(DemoJob job)=>SafetyWorkAllowed(job.Cell)&&Walkable(job.Cell)&&job.Materials.All(m=>StoragePriority(job.Cell,m.Item)>0)&&
        job.Materials.Where(m=>m.Stage<2).Sum(m=>(long)m.Count*Content.Item(m.Item).StorageUnits)<=CellStorageCapacity-StoredUnits(job.Cell)-ReservedStorageUnits(job.Cell,job);
    private IEnumerable<Cell> OutputPositions(DemoJob job)=>ActionPositions(job).Where(c=>c.Level==job.Cell.Level&&Walkable(c)&&SafetyWorkAllowed(c));
    private bool OutputSpaceAvailable(DemoJob job)=>job.OutputCell is Cell c&&OutputPositions(job).Contains(c)&&FreeStorageCount(c,JobRecipe(job).Output,job)>=JobRecipe(job).Count;
    private bool ReserveOutputSpace(DemoJob job)
    {
        if(!job.Production)return true;
        if(OutputSpaceAvailable(job))return true;
        job.OutputCell=null;var recipe=JobRecipe(job);
        foreach(var cell in OutputPositions(job))if(FreeStorageCount(cell,recipe.Output,job)>=recipe.Count){job.OutputCell=cell;return true;}
        return false;
    }
    public string StorageDescription(Cell c)
    {
        long used=StoredUnits(c),reserved=ReservedStorageUnits(c);
        return $"容量 {used}/{CellStorageCapacity} · 在途／产出预约 {reserved}"+(used>CellStorageCapacity?" · 现场物资超容，保留并等待清运":used+reserved>=CellStorageCapacity?" · 已满，停止派送":"");
    }
    private void AddStorageAlerts()
    {
        foreach(var zone in StorageZones.Where(z=>z.Cells.Count>0&&z.Cells.All(c=>StoredUnits(c)+ReservedStorageUnits(c)>=CellStorageCapacity)).Take(8))
            LifeAlerts.Add(new("storage:"+zone.Id,$"堆场 #{zone.Id} 已满，扩仓或清运后恢复派送",2,Location:zone.Cells.First()));
        foreach(var job in Jobs.Where(j=>j.Production&&!j.MaterialsPaused&&!OutputSpaceAvailable(j)).Take(8))
            LifeAlerts.Add(new("output:"+job.Id,"工坊等待输出空间：清运附近物资或恢复通路",2,Location:job.Cell));
    }
}
