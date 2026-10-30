namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    private readonly HashSet<Cell> _blockChanges=new();
    private readonly Dictionary<(int X,int Z),long> _farmColumnVersions=new();
    private int _blockBatchDepth;
    public long BlockUpdateFlushes {get;private set;}
    private long FarmGeometryVersion(Cell c)=>_farmColumnVersions.GetValueOrDefault((c.X,c.Z));
    // Geometry and navigation invalidate immediately, even inside a bulk edit.
    // Dependent systems receive the unique changed cells on commit or before a room query.
    private void GeometryChanged(Cell c)
    {
        Navigation.Invalidate(c);_blockDamage.Remove(c);_supportSafety.Clear();
        var key=(c.X,c.Z);_farmColumnVersions[key]=_farmColumnVersions.GetValueOrDefault(key)+1;
        _blockChanges.Add(c);if(_blockBatchDepth==0)FlushBlockChanges();
    }
    public IDisposable BatchBlockChanges(){_blockBatchDepth++;return new BlockBatch(this);}
    private sealed class BlockBatch(DemoWorld world):IDisposable
    {
        private bool _disposed;
        public void Dispose(){if(_disposed)return;_disposed=true;if(--world._blockBatchDepth==0)world.FlushBlockChanges();}
    }
    private void FlushBlockChanges()
    {
        if(_blockChanges.Count==0)return;
        BlockUpdateFlushes++;
        InvalidateRoomBatch(_blockChanges);
        foreach(var c in _blockChanges){FluidGeometryChanged(c);SupportChanged(c);}
        _blockChanges.Clear();
    }
}
