using Godot;
using NoDwarf.Services.Llm;
using NoDwarf.Simulation;

namespace NoDwarf;
public partial class Main
{
    private StorytellerDirector? _storyDirector;
    private PanelContainer? _storyPanel;
    private Label _storyValue = null!, _storyStatus = null!, _storyHistory = null!;
    private VBoxContainer _storyEvents = null!;
    private Button _storyToggle = null!, _storyRetry = null!;
    private string _storyQueueKey = "";
    private void PumpStoryteller(bool advance)
    {
        if (!_world.Story.Enabled && _storyDirector is null) return;
        _storyDirector ??= new(LlmRuntime.Client);
        _storyDirector.Pump(_world,LlmRuntime.Configuration,advance);
    }
    private void BuildStoryInterface(Control root, Node menu)
    {
        TreeExiting += () => _storyDirector?.Dispose();
        _storyPanel=Panel(root,Vector2.Zero,new Vector2(450,540));BottomLeft(_storyPanel,590,450);
        _storyPanel.ZIndex=25;
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);_storyPanel.AddChild(column);
        var heading=new HBoxContainer();column.AddChild(heading);
        var title=Text("叙事者与营地价值",20,Ink);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;heading.AddChild(title);
        UiAccess.Name(Button(heading,"×",()=>_storyPanel.Hide()),"关闭叙事者面板");
        _storyValue=Text("",14,Ink,true);column.AddChild(_storyValue);
        _storyStatus=Text("",14,Accent,true);_storyStatus.AccessibilityLive=AccessibilityServer.AccessibilityLiveMode.Polite;column.AddChild(_storyStatus);
        column.AddChild(Text("启用后会向已配置的 AI 服务发送营地摘要并计费。事件由模型安排；首日保护，之后可能出现袭击。",13,Muted,true));
        var controls=new HBoxContainer();column.AddChild(controls);
        _storyToggle=Button(controls,"启用叙事者",()=>
        {
            if(!_world.Story.Enabled&&LlmRuntime.Configuration.ApiKey==""&&!LlmRuntime.Configuration.Settings.Endpoint().IsLoopback)
            {ShowActionFeedback("请先在主菜单 → 设置中配置 AI 服务和密钥。",true);return;}
            _world.EnableStory(!_world.Story.Enabled);PumpStoryteller(false);RefreshStoryInterface();
        });
        UiAccess.Name(_storyToggle,"启用或停止 LLM 叙事者","停止会取消待发生事件；已发生的后果和持续天气保留。启用会发送摘要并使用请求额度。");
        _storyRetry=Button(controls,"重新决策",()=>{_world.RetryStory();RefreshStoryInterface();});
        UiAccess.Name(_storyRetry,"重新请求叙事决定","可能产生费用，已安排事件保留；不会重放已完成的工具调用。");
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};column.AddChild(scroll);
        var details=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};details.AddThemeConstantOverride("separation",8);scroll.AddChild(details);
        details.AddChild(Text("待发生事件（取消不会撤回已发生后果）",14,Muted,true));
        _storyEvents=new VBoxContainer();details.AddChild(_storyEvents);
        _storyHistory=Text("",13,Muted,true);details.AddChild(_storyHistory);
        _storyPanel.Hide();
        Button(menu,"叙事者与营地价值",()=>{_storyPanel.Show();_storyPanel.MoveToFront();_worldMenu.Hide();_storyQueueKey="";RefreshStoryInterface();});
    }
    private void RefreshStoryInterface()
    {
        if(_storyPanel?.Visible!=true)return;
        var s=_world.Story;var value=_world.Values();
        _storyValue.Text=$"总价值 {value.Total:0}\n物品 {value.Items:0} · 建筑 {value.Buildings:0} · 居民 {value.Residents:0}\n存活 {value.Living} · 可行动 {value.Capable} · 战力估计 {value.CombatPower:0}\n威胁预算 {_world.StoryThreatBudget(value):0} · 机会预算 {_world.StoryGiftBudget(value):0}";
        string state=s.Turn?.Status switch{"requesting"=>"正在请求 AI", "received"=>"JSON 已收到，等待模拟推进", "waiting"=>"等待重新决策", "ready"=>"准备继续 Agent 决策", _=>"等待下次决策"};
        _storyStatus.Text=!s.Enabled?"叙事者未启用":s.Error!=""?s.Error:state+(_clock.Paused?" · 游戏已暂停":"");
        _storyToggle.Text=s.Enabled?"停止并取消待发生事件":"启用叙事者";
        _storyRetry.Disabled=!s.Enabled||s.Turn?.Status is "requesting" or "received" or "ready";
        string key=string.Join('|',s.Pending.Select(e=>e.Id))+":"+_world.GetHashCode();
        if(key!=_storyQueueKey)
        {
            _storyQueueKey=key;Clear(_storyEvents);
            foreach(var e in s.Pending)
            {
                var row=new HBoxContainer();_storyEvents.AddChild(row);
                var label=Text($"{_world.StoryEventLabel(e)} · 第 {e.Due/GameCalendar.SecondsPerDay:0.00} 模拟日\n{e.Reason}",13,Ink,true);label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(label);
                UiAccess.Name(Button(row,"取消",()=>{_world.CancelStoryEvent(e.Id,"玩家取消了待发生事件。");RefreshStoryInterface();}),"取消待发生的"+_world.StoryEventLabel(e));
            }
            if(s.Pending.Count==0)_storyEvents.AddChild(Text("暂无事件",13,Muted));
        }
        string Outcome(string outcome)=>outcome switch {"executed"=>"已发生","cancelled"=>"已取消","completed"=>"已完成","observed"=>"已记录",_=>outcome};
        _storyHistory.Text="最近结果\n"+string.Join('\n',s.History.TakeLast(6).Reverse().Select(h=>$"{DemoWorld.StoryEventName(h.Kind)} · {Outcome(h.Outcome)}\n{h.Detail}"));
    }
}
