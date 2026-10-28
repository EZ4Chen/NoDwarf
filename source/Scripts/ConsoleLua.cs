using MoonSharp.Interpreter;
using NoDwarf.Simulation;
using Godot;
namespace NoDwarf;

// Created only on first use, never ticked. No CLR objects, file/OS loaders or
// callback-based standard libraries are exposed to console scripts.
internal sealed class ConsoleLua
{
    private readonly MoonSharp.Interpreter.Script _script=new(CoreModules.Basic|CoreModules.GlobalConsts|CoreModules.TableIterators|CoreModules.Math);
    private readonly Action<string> _output;
    private int _calls,_printed;
    public ConsoleLua(Action<string> command,Action<string> output,Func<Cell?> selected,Func<(long Tick,int Residents,int Jobs)> status)
    {
        _output=output;
        _script.Options.DebugPrint=Print;
        var game=new Table(_script);
        game["command"]=DynValue.NewCallback((_,args)=>
        {
            if(++_calls>16)throw new ScriptRuntimeException("每次 Lua 最多调用 16 条游戏命令。");
            if(args.Count!=1||args[0].Type!=DataType.String||args[0].String.Length>512)
                throw new ScriptRuntimeException("game.command 需要一条不超过 512 字符的命令。");
            try{command(args[0].String);}catch(Exception e){throw new ScriptRuntimeException(e.Message);}
            return DynValue.True;
        });
        game["selected"]=DynValue.NewCallback((_,args)=>
        {
            if(selected() is not Cell cell)return DynValue.Nil;
            var value=new Table(_script);value["x"]=cell.X;value["z"]=cell.Z;value["y"]=cell.Level;
            return DynValue.NewTable(value);
        });
        game["status"]=DynValue.NewCallback((_,args)=>
        {
            var state=status();var value=new Table(_script);
            value["tick"]=(double)state.Tick;value["residents"]=state.Residents;value["jobs"]=state.Jobs;
            return DynValue.NewTable(value);
        });
        _script.Globals["game"]=game;
    }
    private void Print(string text)
    {
        int remaining=4000-_printed;if(remaining<=0)return;
        string clipped=text.Length>remaining?text[..remaining]+"…":text;_printed+=clipped.Length;_output(clipped);
    }
    public void Execute(string code)
    {
        if(code.Length>16384)throw new ArgumentException("Lua 代码最多 16 KB。");
        _calls=0;_printed=0;
        if(code.StartsWith('='))code="return "+code[1..];
        var coroutine=_script.CreateCoroutine(_script.LoadString(code));
        coroutine.Coroutine.AutoYieldCounter=10000;
        var result=coroutine.Coroutine.Resume();
        if(coroutine.Coroutine.State!=CoroutineState.Dead)
            throw new InvalidOperationException("Lua 超过 10000 条执行指令，已中止并清空 Lua 变量；此前已执行的游戏命令不会回滚。");
        if(result.Type is not (DataType.Void or DataType.Nil))Print(result.ToPrintString());
    }
}

public partial class Main
{
    private void RunLua(string code)
    {
        if(code=="help")
        {
            RecordWorldEvent("Lua：lua print(1+2) | lua = 2+3 | lua game.command(\"status\")\n"+
                "lua local c=game.selected(); if c then print(c.x,c.z,c.y) end\n"+
                "lua for i=1,3 do game.command(\"give wood 1\") end（需 dev on）\n"+
                "支持变量、循环、函数、表、pairs/ipairs、数学；不开放系统、文件和 CLR。\n"+
                "每次限 10000 指令、16 条游戏命令；lua reset 清空变量；换地图/读档也会清空。");return;
        }
        if(code=="reset"){_lua=null;RecordWorldEvent("Lua 变量已清空。");return;}
        _lua??=new ConsoleLua(command=>
        {
            _sessionLog.Write(_clock.Tick,"INFO","Lua命令",command);
            ExecuteCommand(command.Split(' ',StringSplitOptions.RemoveEmptyEntries));
        },output=>_sessionLog.Write(_clock.Tick,"INFO","Lua",output),()=>_selectedCell,
            ()=>(_clock.Tick,_world.Workers.Count,_world.Jobs.Count));
        try{_lua.Execute(code);}
        catch(InterpreterException e){_lua=null;throw new ArgumentException("Lua："+e.DecoratedMessage+"（变量已清空；已执行的游戏命令不会回滚）");}
        catch{_lua=null;throw;}
    }
}
