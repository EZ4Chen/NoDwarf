using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Label _actionFeedback=null!;
    private ulong _feedbackUntil;
    private readonly HashSet<string> _feedbackEvents=new();
    private void BuildActionFeedback(Control root)
    {
        _actionFeedback=Text("",14,Ink,true);
        _actionFeedback.Name="ActionFeedback";_actionFeedback.ZIndex=30;
        _actionFeedback.MouseFilter=Control.MouseFilterEnum.Ignore;
        root.AddChild(_actionFeedback);
        _actionFeedback.AnchorLeft=_actionFeedback.AnchorRight=.5f;
        _actionFeedback.OffsetLeft=-290;_actionFeedback.OffsetRight=290;
        _actionFeedback.OffsetTop=86;_actionFeedback.OffsetBottom=132;
        _actionFeedback.AddThemeStyleboxOverride("normal",RStyle("181e26","817457",10,8));
        _actionFeedback.Hide();
    }
    private void ShowActionFeedback(string message,bool failed=false)
    {
        if(_feedbackEvents.Count>128)_feedbackEvents.Clear();
        _feedbackEvents.Add(message);
        _sessionLog.Write(_clock.Tick,failed?"WARN":"INFO","操作",message);
        _actionFeedback.Text=message;
        _actionFeedback.AddThemeColorOverride("font_color",failed?new Color("e6a38c"):Ink);
        UiAccess.Name(_actionFeedback,(failed?"未完成：":"操作结果：")+message);
        _feedbackUntil=Time.GetTicksMsec()+6000;_actionFeedback.Show();
    }
    private void RefreshActionFeedback()
    {
        if(_actionFeedback is null)return;
        if(Time.GetTicksMsec()>=_feedbackUntil)_actionFeedback.Hide();
        _hint.Visible=_tool!="select"&&!DossierOpen&&!_characterEditor.Visible;
        // The architect's existing description area is the contextual help surface.
        if(_buildMenu.Visible)
        {_architectHint.Text=_tool=="select"?"":_hint.Text;_hint.Hide();}
    }
    private void QueueProduction(Func<bool> queue)
    {
        string? before=_world.Events.FirstOrDefault();bool ok=queue();string? after=_world.Events.FirstOrDefault();
        if(after is not null&&after!=before)_feedbackEvents.Add(after);
        ShowActionFeedback(ok?"已下单一批；在订单页查看进度。":after!=before?after!:"无法下单，请检查设施、材料和排队数量。",!ok);RefreshUi();
    }
}
