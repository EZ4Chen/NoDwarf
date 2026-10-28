using Godot;
using NoDwarf.Simulation;
using System.Text;
namespace NoDwarf;

// Lives across title/game transitions. Engine callbacks may run on worker threads:
// they only enqueue bounded text; disk writes and UI refresh happen on the main thread.
public partial class AppDiagnostics : Node
{
    public SessionLog Log {get;private set;}=null!;
    public long Tick;
    private readonly Queue<(string Level,string Category,string Text)> _pending=new();
    private readonly object _gate=new();
    private int _dropped;
    private EngineLogSink _sink=null!;
    private TextWriter _stdout=null!,_stderr=null!;
    public override void _EnterTree()
    {
        DiagnosticTrace.Initialize();
        Log=new SessionLog(ProjectSettings.GlobalizePath("user://logs"));
        _sink=new EngineLogSink{Receive=Enqueue};OS.AddLogger(_sink);
        _stdout=Console.Out;_stderr=Console.Error;
        Console.SetOut(new ConsoleLogWriter(_stdout,s=>Enqueue("INFO","C#",s)));
        Console.SetError(new ConsoleLogWriter(_stderr,s=>Enqueue("ERROR","C#",s)));
        AppDomain.CurrentDomain.UnhandledException+=Unhandled;
        TaskScheduler.UnobservedTaskException+=Unobserved;
    }
    internal void Enqueue(string level,string category,string text)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        lock(_gate)
        {
            if(_pending.Count>=512){_dropped++;return;}
            _pending.Enqueue((level,category,text.Length>8000?text[..8000]+"…":text.TrimEnd()));
        }
    }
    private void Unhandled(object sender,UnhandledExceptionEventArgs e)
        =>Log.Write(Tick,"ERROR","未处理异常",e.ExceptionObject.ToString()??"未知异常");
    private void Unobserved(object? sender,UnobservedTaskExceptionEventArgs e)
        =>Enqueue("ERROR","后台任务",e.Exception.ToString());
    public override void _Process(double delta)=>Drain();
    public void Drain()
    {
        lock(_gate)
        {
            for(int i=0;i<64&&_pending.TryDequeue(out var item);i++)Log.Write(Tick,item.Level,item.Category,item.Text);
            if(_dropped>0){Log.Write(Tick,"WARN","日志",$"日志过密，省略 {_dropped} 条；请检查重复报错。");_dropped=0;}
        }
    }
    public override void _ExitTree()
    {
        DiagnosticTrace.Save();
        OS.RemoveLogger(_sink);_sink.Receive=null;_sink.Dispose();
        Console.Out.Flush();Console.Error.Flush();Console.SetOut(_stdout);Console.SetError(_stderr);
        AppDomain.CurrentDomain.UnhandledException-=Unhandled;TaskScheduler.UnobservedTaskException-=Unobserved;
        while(_pending.Count>0)Drain();Log.Dispose();
    }
}

public partial class EngineLogSink : Logger
{
    public Action<string,string,string>? Receive;
    public override void _LogMessage(string message,bool error)=>Receive?.Invoke(error?"ERROR":"INFO","引擎",message);
    public override void _LogError(string function,string file,int line,string code,string rationale,bool editorNotify,int errorType,Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        =>Receive?.Invoke(errorType==(int)ErrorType.Warning?"WARN":"ERROR","引擎",
            $"{(string.IsNullOrEmpty(rationale)?code:rationale)}\n{file}:{line} {function}");
}

internal sealed class ConsoleLogWriter(TextWriter original,Action<string> receive) : TextWriter
{
    private readonly StringBuilder _line=new();
    private readonly object _gate=new();
    public override Encoding Encoding=>original.Encoding;
    public override void Write(char value)=>Write(value.ToString());
    public override void Write(string? value)
    {
        if(value is null)return;
        lock(_gate)
        {
            original.Write(value);
            foreach(char c in value)
            {
                if(c=='\n'){receive(_line.ToString());_line.Clear();}
                else if(c!='\r'&&_line.Length<8000)_line.Append(c);
            }
        }
    }
    public override void WriteLine(string? value)=>Write((value??"")+"\n");
    public override void Flush(){lock(_gate){if(_line.Length>0){receive(_line.ToString());_line.Clear();}original.Flush();}}
}
