using Godot;
using NoDwarf.Simulation;
using NoDwarf.Services.Llm;
namespace NoDwarf;

public partial class SettingsWindow:Window
{
    public readonly Dictionary<string,Control> Fields=new();
    public ILlmClient Client=LlmRuntime.Client;
    public DemoWorld? World;
    public Action? Applied;
    public Action? Closed;
    public string StatusText=>status.Text;
    public TabContainer Categories=>tabs;
    public bool IsDisplayPreview=>preview;
    private bool Alive=>GodotObject.IsInstanceValid(this)&&IsInsideTree();
    private Label status=null!;
    private TabContainer tabs=null!;
    private Button save=null!,test=null!,cancelTest=null!;
    private CancellationTokenSource? request;
    private Window root=null!;
    private ConfirmationDialog? confirmation;
    private double revertAt;
    private bool preview;
    public override void _Ready()
    {
        root=GetTree().Root;Title="设置";Size=new(850,620);MinSize=new(560,380);Transient=true;Exclusive=true;Borderless=true;
        CloseRequested+=Close;
        frame=new GameWindowFrame("设置",Close);AddChild(frame);frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);frame.EnableWindowDrag(this);
        tabs=new TabContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};frame.Body.AddChild(tabs);
        Build();status=frame.Status;Status(PreferencesRuntime.Issue);
        frame.AddAction("恢复本页默认",Defaults);
        var cancel=frame.AddAction("取消",Close);cancel.TooltipText="关闭并放弃尚未保存的修改";UiAccess.Name(cancel,"取消设置修改",cancel.TooltipText);
        save=frame.AddAction("保存并应用",Save,true);
    }
    private GameWindowFrame frame=null!;
    private void Status(string text)=>frame.SetStatus(text);
    private VBoxContainer Page(string name)
    {
        var scroll=GameUi.Scroll(tabs);scroll.Name=name;
        var box=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};box.AddThemeConstantOverride("separation",12);scroll.AddChild(box);return box;
    }
    private void Note(VBoxContainer page,string text)=>page.AddChild(new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart});
    private T Field<T>(VBoxContainer page,string key,string label,T control) where T:Control
    {
        Fields[key]=GameUi.Field(page,label,control);return control;
    }
    private void Toggle(VBoxContainer p,string key,string label,bool value)
    {Field(p,key,label,new CheckBox{ButtonPressed=value});}
    private void Number(VBoxContainer p,string key,string label,double value,double min,double max,double step=1)=>Field(p,key,label,new SpinBox{MinValue=min,MaxValue=max,Step=step,Value=value});
    private OptionButton Select(VBoxContainer p,string key,string label,int index,params string[] choices)
    {var b=new OptionButton();foreach(string c in choices)b.AddItem(c);b.Select(index);return Field(p,key,label,b);}
    private LineEdit Input(VBoxContainer p,string key,string label,string value,bool secret=false)=>Field(p,key,label,new LineEdit{Text=value,Secret=secret,MaxLength=secret?4096:512});
    private static Button AddButton(Node parent,string label,Action action)=>GameUi.Action(parent,label,action);
    private void Build()
    {
        var p=PreferencesRuntime.Current;var ai=LlmRuntime.Configuration;var d=p.Dialogue;
        var graphics=Page("画面");Toggle(graphics,"fullscreen","全屏显示",p.Fullscreen);Toggle(graphics,"vsync","垂直同步",p.Vsync);
        Number(graphics,"fps","帧率上限（0 为不限）",p.FrameLimit,0,240);Select(graphics,"msaa","3D 抗锯齿",p.Msaa,"关闭","2× MSAA","4× MSAA","8× MSAA");
        Toggle(graphics,"shadows","太阳阴影",p.Shadows);Number(graphics,"scale","界面缩放",p.UiScale,.8,1.3,.05);
        Note(graphics,"全屏与界面缩放更改后有 15 秒确认时间，未确认会恢复原设置。垂直同步可能优先限制帧率。");
        var game=Page("游戏");Number(game,"autosave","自动保存间隔（实际分钟，0 关闭）",p.AutosaveMinutes,0,60);
        Toggle(game,"focus","切换到其他应用时暂停",p.PauseOnFocusLoss);Toggle(game,"raid","袭击出现时暂停",p.PauseOnRaid);
        Toggle(game,"paused","新营地开始时暂停",p.StartPaused);Number(game,"speed","新营地初始速度",p.StartSpeed,1,3);
        Fields["autosave"].TooltipText="保存当前营地并保留上一份备份；暂停时不计时。";Fields["speed"].TooltipText="仅用于新营地，读档保留原速度。";
        var controls=Page("操作");Number(controls,"camera","键盘移动速度",p.CameraSpeed,.25,3,.05);Number(controls,"orbit","镜头旋转灵敏度",p.OrbitSpeed,.25,3,.05);
        Note(controls,"WASD / 方向键：移动镜头\n滚轮：缩放镜头\n空格：暂停 / 继续\nF5：保存营地　F9：读取存档\nEsc：关闭当前操作");
        var sound=Page("声音");Number(sound,"volume","主音量（0 为静音）",p.MasterVolume,0,100);Fields["volume"].TooltipText="调整游戏主音量。";
        var model=Page("AI 服务");var provider=Select(model,"provider","供应商",ai.Settings.Provider=="deepseek"?0:1,"DeepSeek","自定义兼容接口");
        Input(model,"url","接口地址",ai.Settings.BaseUrl);Input(model,"model","模型名称",ai.Settings.Model);Input(model,"key","API 密钥",ai.ApiKey,true);
        Toggle(model,"remember","在此电脑保存密钥（未加密）",ai.RememberKey);
        Number(model,"timeout","超时（秒）",ai.Settings.TimeoutSeconds,5,180);Number(model,"tokens","单次输出 Token 上限",ai.Settings.MaxOutputTokens,128,8192);Number(model,"limit","本次启动共享请求上限",ai.Settings.SessionRequestLimit,1,10000);
        Note(model,"居民决策、对话、记忆提炼和叙事者共用接口与额度。测试仅发送连接检查，可能计费；不发送营地数据。DeepSeek 关闭思考，兼容接口需支持 JSON 输出。");
        var actions=new HBoxContainer();model.AddChild(actions);test=AddButton(actions,"测试 JSON 调用",()=>_=TestAsync());cancelTest=AddButton(actions,"取消测试",()=>request?.Cancel());cancelTest.Disabled=true;
        provider.ItemSelected+=i=>{((LineEdit)Fields["key"]).Text="";((CheckBox)Fields["remember"]).ButtonPressed=false;((LineEdit)Fields["url"]).Text=i==0?"https://api.deepseek.com":"";((LineEdit)Fields["model"]).Text=i==0?"deepseek-flash":"";request?.Cancel();};
        var behavior=Page("AI 玩法");Toggle(behavior,"residentAI","启用居民自主决策",World?.ResidentMind.Enabled??p.ResidentAi);Toggle(behavior,"storyAI","启用营地叙事者",World?.Story.Enabled??p.Storyteller);
        Number(behavior,"residentInterval","居民全体决策间隔（游戏小时）",World?.ResidentMind.IntervalHours??p.ResidentIntervalHours,1,24);
        Note(behavior,World is null?"这两项作为新营地默认设置；读档保留存档中的开关。":"保存后应用于当前营地，并作为新营地默认设置。");
        Note(behavior,"启用后会发送居民或营地摘要并使用共享请求额度，可能计费。叙事者可以安排袭击；停止叙事者会取消待发生事件，已经发生的后果保留。");
        var memory=Page("对话与记忆");Toggle(memory,"dialogue","启用居民对话",d.Enabled);Toggle(memory,"summarize","启用旧对话 AI 提炼",d.Summarize);
        Note(memory,"启用对话会发送参与者背景、本人经历及相关对话，可能计费。每次交谈最多 4 次发言请求；旧对话提炼另用 1 次。关闭后停止新增请求，保留已有记忆。");
        Number(memory,"interval","全营地对话间隔（游戏小时）",d.IntervalHours,1,24);Number(memory,"short","短期记忆：保留原话轮数",d.ShortTermRounds,2,24);
        Number(memory,"long","长期记忆：旧对话保留轮数",d.LongTermRounds,8,200);Number(memory,"recall","每次检索上限（轮）",d.RecallRounds,1,12);
        Number(memory,"details","检索原话上限（轮）",d.DetailedRounds,0,12);Number(memory,"hours","回溯游戏小时（0 为不限）",d.RecallHours,0,2400);
        Number(memory,"summary","单条摘要字数上限",d.SummaryCharacters,80,400);
        Note(memory,"一轮指完整的双人交谈。优先检索共同经历，再补充本人的近期对话。关闭提炼时旧记忆保留原文并按短摘录检索；超出保留上限的最旧对话会被移除，降低上限也会生效。");
        foreach(var field in Fields.Values)
        {
            if(field is LineEdit input)input.TextChanged+=_=>request?.Cancel();
            if(field is SpinBox spin)spin.ValueChanged+=_=>request?.Cancel();
        }
    }
    private bool B(string k)=>((CheckBox)Fields[k]).ButtonPressed;
    private double N(string k)=>((SpinBox)Fields[k]).Value;
    private int I(string k)=>(int)N(k);
    private string S(string k)=>((LineEdit)Fields[k]).Text.Trim();
    public GamePreferences ReadPreferences()=>new(){Fullscreen=B("fullscreen"),Vsync=B("vsync"),FrameLimit=I("fps"),Msaa=((OptionButton)Fields["msaa"]).Selected,Shadows=B("shadows"),UiScale=N("scale"),MasterVolume=I("volume"),AutosaveMinutes=I("autosave"),PauseOnFocusLoss=B("focus"),PauseOnRaid=B("raid"),StartPaused=B("paused"),StartSpeed=I("speed"),CameraSpeed=N("camera"),OrbitSpeed=N("orbit"),ResidentAi=B("residentAI"),Storyteller=B("storyAI"),ResidentIntervalHours=I("residentInterval"),Dialogue=new(){Enabled=B("dialogue"),Summarize=B("summarize"),IntervalHours=I("interval"),ShortTermRounds=I("short"),LongTermRounds=I("long"),RecallRounds=I("recall"),DetailedRounds=I("details"),RecallHours=I("hours"),SummaryCharacters=I("summary")}};
    public LlmConfiguration ReadConfiguration()=>new(){ApiKey=S("key"),RememberKey=B("remember"),Settings=new(){Provider=((OptionButton)Fields["provider"]).Selected==0?"deepseek":"compatible",BaseUrl=S("url"),Model=S("model"),TimeoutSeconds=I("timeout"),MaxOutputTokens=I("tokens"),SessionRequestLimit=I("limit")}};
    private void Defaults()
    {
        // Reset only the selected page, leaving credentials and other drafts intact.
        var defaults=new GamePreferences();var d=defaults.Dialogue;
        void Set(string k,object v){if(Fields[k] is CheckBox b)b.ButtonPressed=(bool)v;else if(Fields[k] is SpinBox n)n.Value=Convert.ToDouble(v);else if(Fields[k] is OptionButton o)o.Select((int)v);}
        switch(tabs.CurrentTab)
        {
            case 0:Set("fullscreen",false);Set("vsync",true);Set("fps",60);Set("msaa",0);Set("shadows",true);Set("scale",1d);break;
            case 1:Set("autosave",5);Set("focus",true);Set("raid",true);Set("paused",true);Set("speed",1);break;
            case 2:Set("camera",1d);Set("orbit",1d);break;
            case 3:Set("volume",100);break;
            case 4:Set("timeout",45);Set("tokens",1024);Set("limit",100);break;
            case 5:Set("residentAI",false);Set("storyAI",false);Set("residentInterval",2);break;
            case 6:Set("dialogue",false);Set("summarize",true);Set("interval",d.IntervalHours);Set("short",d.ShortTermRounds);Set("long",d.LongTermRounds);Set("recall",d.RecallRounds);Set("details",d.DetailedRounds);Set("hours",d.RecallHours);Set("summary",d.SummaryCharacters);break;
        }
        Status("本页已恢复默认，保存后生效。AI 地址、模型和密钥保留。");
    }
    public void Save()
    {
        if(request is not null||preview)return;
        try
        {
            var p=ReadPreferences();var c=ReadConfiguration();p.Validate();c.Validate();
            if(p.Fullscreen!=PreferencesRuntime.Current.Fullscreen||p.UiScale!=PreferencesRuntime.Current.UiScale)
            {
                preview=true;PreferencesRuntime.Apply(root,p);PopupCenteredClamped(new(850,620),.92f);revertAt=Time.GetTicksMsec()/1000d+15;
                confirmation=new ConfirmationDialog{Title="保留显示设置？",DialogText="请确认界面清晰可用；15 秒后自动恢复。",OkButtonText="保留并保存",CancelButtonText="恢复原设置"};AddChild(confirmation);
                confirmation.Confirmed+=()=>{preview=false;confirmation.QueueFree();confirmation=null;Commit(p,c);};confirmation.Canceled+=Revert;confirmation.PopupCentered(new(440,150));
            }
            else Commit(p,c);
        }
        catch(Exception e) when(e is LlmException or IOException or InvalidDataException or UnauthorizedAccessException){Status(e.Message);}
    }
    private void Commit(GamePreferences p,LlmConfiguration config)
    {
        try{PreferencesRuntime.Save(p,config);PreferencesRuntime.Apply(root);if(World is not null){if(World.ResidentMind.Enabled!=p.ResidentAi)World.EnableResidentMind(p.ResidentAi);if(World.Story.Enabled!=p.Storyteller)World.EnableStory(p.Storyteller);World.ResidentMind.IntervalHours=p.ResidentIntervalHours;}Applied?.Invoke();Status("设置已保存并应用。");}
        catch(Exception e) when(e is LlmException or IOException or InvalidDataException or UnauthorizedAccessException){PreferencesRuntime.Apply(root);Status("保存失败："+e.Message);}
    }
    private void Revert(){preview=false;PreferencesRuntime.Apply(root);confirmation?.QueueFree();confirmation=null;Status("显示设置已恢复，未保存。");}
    public override void _Process(double delta){if(preview&&Time.GetTicksMsec()/1000d>=revertAt)Revert();}
    public async Task TestAsync()
    {
        if(request is not null)return;
        using var token=new CancellationTokenSource();
        try
        {
            var config=ReadConfiguration();config.Validate();if(config.ApiKey==""&&!config.Settings.Endpoint().IsLoopback)throw new LlmException("请先填写 API 密钥。");
            request=token;test.Disabled=save.Disabled=true;cancelTest.Disabled=false;Status("正在测试……");
            var reply=await LlmConnectionProbe.RunAsync(Client,config,token.Token);
            if(Alive)Status($"测试通过 · {reply.Milliseconds} ms · 共享额度已用 {LlmRuntime.Client.Requests} 次；配置尚需保存。");
        }
        catch(OperationCanceledException){if(Alive)Status("测试已取消，已发出的请求仍可能计费。");}
        catch(LlmException e){if(Alive)Status(e.Message);}
        finally{request=null;if(Alive){test.Disabled=save.Disabled=false;cancelTest.Disabled=true;}}
    }
    public override void _Input(InputEvent input)
    {if(input is InputEventKey{Pressed:true,Echo:false,Keycode:Key.Escape}&&!preview){SetInputAsHandled();Close();}}
    public void Close(){request?.Cancel();if(preview)Revert();Hide();Closed?.Invoke();QueueFree();}
    public override void _ExitTree(){request?.Cancel();if(preview)PreferencesRuntime.Apply(root);}
}
