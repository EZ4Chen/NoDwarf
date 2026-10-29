using Godot;
using NoDwarf.Services.Llm;
using NoDwarf.Simulation;

namespace NoDwarf;
public partial class Main
{
    private ResidentMindDirector? _residentMindDirector;
    private PanelContainer _mindPanel=null!;
    private Label _mindStatus=null!,_mindDetails=null!;
    private Button _mindToggle=null!,_mindRetry=null!;
    private SpinBox _mindInterval=null!;
    private void PumpResidentMind(bool advance)
    {
        if(!_world.ResidentMind.Enabled&&_residentMindDirector is null)return;
        _residentMindDirector??=new(LlmRuntime.Client);
        _residentMindDirector.Pump(_world,LlmRuntime.Configuration,advance);
    }
    private void BuildResidentMindInterface(Control root,Node menu)
    {
        TreeExiting+=()=>_residentMindDirector?.Dispose();
        _mindPanel=Panel(root,Vector2.Zero,new Vector2(450,520));BottomLeft(_mindPanel,570,450);_mindPanel.ZIndex=25;
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);_mindPanel.AddChild(column);
        var header=new HBoxContainer();column.AddChild(header);
        var title=Text("居民人格与决策",20,Ink);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;header.AddChild(title);
        UiAccess.Name(Button(header,"×",()=>_mindPanel.Hide()),"关闭居民人格面板");
        column.AddChild(Text("经历和好感已参与同等紧急程度下的照料选择。启用 AI 后会向已配置的服务发送居民背景、相关经历和当前选项，使用共享额度并可能计费。",13,Muted,true));
        _mindStatus=Text("",14,Accent,true);_mindStatus.AccessibilityLive=AccessibilityServer.AccessibilityLiveMode.Polite;column.AddChild(_mindStatus);
        var controls=new HBoxContainer();column.AddChild(controls);
        _mindToggle=Button(controls,"启用居民 AI",()=>
        {
            if(!_world.ResidentMind.Enabled&&LlmRuntime.Configuration.ApiKey==""&&!LlmRuntime.Configuration.Settings.Endpoint().IsLoopback)
            {ShowActionFeedback("请先在主菜单 → 设置 → AI 服务配置模型和密钥。",true);return;}
            _world.EnableResidentMind(!_world.ResidentMind.Enabled);PumpResidentMind(false);RefreshResidentMindInterface();
        });
        UiAccess.Name(_mindToggle,"启用或停止居民 AI","启用会发送居民摘要并可能计费。停止会取消未采纳的决定，已经开始的活动保留。");
        _mindRetry=Button(controls,"重新决策",()=>{_world.RetryResidentMind();PumpResidentMind(false);RefreshResidentMindInterface();});
        UiAccess.Name(_mindRetry,"重新请求居民决定","使用共享 AI 请求额度；不会重放已采纳的决定。");
        var interval=new HBoxContainer();column.AddChild(interval);interval.AddChild(Text("全体请求间隔（游戏小时）",13,Muted));
        _mindInterval=new SpinBox{MinValue=1,MaxValue=24,Step=1,Value=2,CustomMinimumSize=new(100,32)};interval.AddChild(_mindInterval);
        UiAccess.Name(_mindInterval,"居民 AI 全体请求间隔，游戏小时");
        _mindInterval.ValueChanged+=v=>{_world.ResidentMind.IntervalHours=(int)v;};
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};column.AddChild(scroll);
        _mindDetails=Text("",13,Ink,true);_mindDetails.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;scroll.AddChild(_mindDetails);
        _mindPanel.Hide();
        Button(menu,"居民人格与决策",()=>{_mindPanel.Show();_mindPanel.MoveToFront();_worldMenu.Hide();RefreshResidentMindInterface();});
    }
    private void RefreshResidentMindInterface()
    {
        if(_mindPanel?.Visible!=true)return;
        var mind=_world.ResidentMind;
        _mindInterval.SetValueNoSignal(mind.IntervalHours);
        _mindToggle.Text=mind.Enabled?"停止居民 AI":"启用居民 AI";
        _mindRetry.Disabled=!mind.Enabled||mind.Turn?.Status is "requesting" or "received";
        _mindStatus.Text=mind.Error!=""?mind.Error:!mind.Enabled?"本地人格规则运行中 · 居民 AI 未启用":mind.Turn?.Status switch
        {"requesting"=>"正在请求一名居民的决定", "received"=>"决定已收到，等待模拟推进", "waiting"=>"请求已停止，可重新决策", _=>"等待空闲居民与下一次决策"};
        _mindDetails.Text=string.Join("\n\n",_world.Workers.Take(12).Select(w=>$"{w.Name} · {w.Person.Personality().Preference}\n{w.Person.Personality().Voice}\n经历 {w.Person.Memories.Count} 条 · 归档 {w.Person.MemorySummaries.Count} 组"))+
            "\n\n最近决定（安排成功不代表已经完成）\n"+string.Join("\n\n",mind.History.TakeLast(5).Reverse().Select(h=>
            $"{_world.Workers.FirstOrDefault(w=>w.Id==h.Resident)?.Name??h.Resident} · {MindChoiceName(h.Choice)}\n{h.Reason}\n{h.Result}\n依据："+
            (h.Memories.Length==0?"当前需求与偏好":string.Join("；",h.Memories.Select(id=>_world.Workers.FirstOrDefault(w=>w.Id==h.Resident)?.Person.Memories.FirstOrDefault(m=>m.EventId==id)?.Text??"归档事件 "+id)))));
    }
    private string MindChoiceName(string choice)
    {
        var parts=choice.Split(':',2);
        string action=parts[0] switch{"feed"=>"送饭","treat"=>"护理","social"=>"交流","research"=>"研究","relax"=>"放松",_=>"继续原安排"};
        return parts.Length==2?action+" · "+(_world.Workers.FirstOrDefault(w=>w.Id==parts[1])?.Name??"原同伴"):action;
    }
}
