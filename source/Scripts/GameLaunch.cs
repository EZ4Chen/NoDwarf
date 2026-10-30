using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public sealed record GameLaunch(DemoWorld World,Main.PreparedSession? Session=null,string? SavePath=null)
{
    public static GameLaunch? Pending {get;set;}
    public static GameLaunch? Take(){var value=Pending;Pending=null;return value;}
}
public partial class Main
{
    private ConfirmationDialog? _titleDialog;
    private void RequestTitleScreen()=>RequestLeaveColony(false);
    private void RequestWindowQuit()=>RequestLeaveColony(true);
    private void RequestLeaveColony(bool quit)
    {
        // Repeated close requests must not replace the original pause state.
        if(_sessionLeaving)return;
        if(_titleDialog is not null&&IsInstanceValid(_titleDialog)&&_titleDialog.Visible)return;
        bool wasPaused=_clock.Paused;_clock.Paused=true;
        if(_titleDialog is not null)_titleDialog.QueueFree();
        _titleDialog=new ConfirmationDialog{Title=quit?"退出游戏":"返回开始菜单",DialogText="离开前是否保存？不保存将丢失上次存档后的进度。",OkButtonText=quit?"保存并退出":"保存并返回",CancelButtonText="继续游戏"};
        _titleDialog.AddButton(quit?"不保存退出":"不保存返回",false,"discard");AddChild(_titleDialog);
        void Leave(){if(quit)GetTree().Quit();else GetTree().ChangeSceneToFile("res://Scenes/Startup.tscn");}
        _titleDialog.Confirmed+=async()=>
        {
            _sessionLeaving=true;
            ShowActionFeedback(quit?"正在保存，完成后退出……":"正在保存，完成后返回菜单……");
            if(await SaveSessionAsync())Leave();
            else{_sessionLeaving=false;_clock.Paused=wasPaused;}
        };
        _titleDialog.CustomAction+=async action=>
        {
            if(action!="discard")return;
            _sessionLeaving=true;
            // An already accepted write must finish before its scene/process disappears.
            if(_saveTask is {} saving)await saving;
            Leave();
        };
        _titleDialog.Canceled+=()=>_clock.Paused=wasPaused;
        _titleDialog.PopupCentered(new Vector2I(480,160));
    }
}
