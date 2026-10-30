using System.Text.Json;
namespace NoDwarf.Simulation;

// One service owns a session's writes. A failed write does not poison the queue.
// Callers enqueue detached snapshots only, and await the returned commit receipt.
public sealed class SaveService
{
    public static readonly Task Ready=Task.Run(SnapshotCopy.Prepare<WorldSnapshot>);
    private readonly object gate=new();
    private Task tail=Task.CompletedTask;
    public Task Write<T>(string path,T snapshot)
    {
        lock(gate)
        {
            var preceding=tail;
            return tail=Task.Run(()=>CommitAfter(preceding,path,snapshot));
        }
    }
    private static async Task CommitAfter<T>(Task? preceding,string path,T snapshot)
    {
        try
        {
            try{await preceding!.ConfigureAwait(false);}catch{ /* Each caller observes its own failure. */ }
            preceding=null; // Do not retain the chain of previous snapshots through receipts.
            SaveFiles.Write(path,stream=>JsonSerializer.Serialize(stream,snapshot,DemoWorld.SaveOptions));
        }
        finally{snapshot=default!;preceding=null;}
    }
    public Task Drain(){lock(gate)return tail;}
}
