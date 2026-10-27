using System.Text.Json;

namespace NoDwarf.Simulation;

public sealed record LogRecord(DateTimeOffset Time,long Tick,string Level,string Category,string Message);
public sealed class SessionLog : IDisposable
{
    private readonly Queue<LogRecord> _records=new();
    private readonly object _gate=new();
    private StreamWriter? _writer;
    public string FilePath { get; }
    public string? WriteError { get; private set; }
    public long Version {get;private set;}
    public SessionLog(string folder)
    {
        FilePath=Path.Combine(folder,$"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.jsonl");
        try{Directory.CreateDirectory(folder);_writer=new StreamWriter(FilePath,false,System.Text.Encoding.UTF8){AutoFlush=true};}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){WriteError=e.Message;}
    }
    public void Write(long tick,string level,string category,string message)
    {
        lock(_gate)
        {
            var record=new LogRecord(DateTimeOffset.Now,tick,level,category,message);
            _records.Enqueue(record);while(_records.Count>1000)_records.Dequeue();Version++;
            try{_writer?.WriteLine(JsonSerializer.Serialize(record));}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){WriteError=e.Message;_writer?.Dispose();_writer=null;}
        }
    }
    public LogRecord[] Snapshot(){lock(_gate)return _records.ToArray();}
    public void Dispose(){lock(_gate){_writer?.Dispose();_writer=null;}}
}
