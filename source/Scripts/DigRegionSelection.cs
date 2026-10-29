using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Button _cancelDigRegionAction=null!;
    private static bool InsideDigRegion(Cell c,DemoJob j)=>j.Dig&&!j.Demolish&&(j.DigRegionMin is Cell lo&&j.DigRegionMax is Cell hi
        ?c.X>=lo.X&&c.X<=hi.X&&c.Z>=lo.Z&&c.Z<=hi.Z&&c.Level>=lo.Level&&c.Level<=hi.Level
        :c.X==j.Cell.X&&c.Z==j.Cell.Z&&c.Level>=j.Cell.Level&&c.Level<j.Cell.Level+j.DigHeight);
    private DemoJob? DigRegionAt(Cell c)=>_world.Jobs.FirstOrDefault(j=>j.Dig&&!j.Demolish&&j.Cell==c)
        ??_world.Jobs.FirstOrDefault(j=>InsideDigRegion(c,j));
    private DemoJob? SelectedDigRegion=>_selectedWorker<0&&_selectedCell is Cell c?DigRegionAt(c):null;
    private DemoJob[] DigRegionJobs(DemoJob selected)=>selected.DigRegion==""?new[]{selected}:
        _world.Jobs.Where(j=>j.Dig&&!j.Demolish&&j.DigRegion==selected.DigRegion).ToArray();
    private void CancelSelectedDigRegion()
    {
        if(SelectedDigRegion is not {} selected)return;
        var jobs=DigRegionJobs(selected);
        foreach(var job in jobs)_world.CancelJob(job,false);
        _world.Log($"已取消这片开采的 {jobs.Length} 个剩余任务；已开采部分保留。");
        _selectedCell=null;_selectionPanel.Hide();SyncWorld();RefreshUi();
    }
    private void RefreshDigRegionSelection()
    {
        var selected=SelectedDigRegion;_cancelDigRegionAction.Visible=selected is not null;
        SyncDigRegionHighlight(selected);
        if(selected is null)return;
        var lo=selected.DigRegionMin??selected.Cell;
        var hi=selected.DigRegionMax??selected.Cell+new Cell(0,0,selected.DigHeight-1);
        _selection.Text=$"开采区域 · {hi.X-lo.X+1} × {hi.Z-lo.Z+1} × {hi.Level-lo.Level+1}\n高度 {lo.Level} 至 {hi.Level} m · 剩余 {DigRegionJobs(selected).Length} 个任务\n取消仅撤销剩余任务，已挖部分不会填回。";
        _demolishAction.Hide();_cancelConstructionAction.Hide();
    }
}
