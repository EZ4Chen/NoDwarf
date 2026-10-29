using Godot;
using NoDwarf.Services.Llm;

namespace NoDwarf;

public partial class Startup
{
    private LineEdit _llmUrl = null!, _llmModel = null!, _llmKey = null!;
    private OptionButton _llmProvider = null!;
    private SpinBox _llmTimeout = null!, _llmTokens = null!, _llmLimit = null!;
    private CheckBox _llmRemember = null!;
    private Label _llmStatus = null!;
    private CancellationTokenSource? _llmTest;
    private ILlmClient _llmClient = LlmRuntime.Client;
    private string _llmLoadIssue = "";
    private int _llmPageVersion;
    private string LlmSettingsPath => ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--llm-settings-test")
        ? "res://.local/llm-settings-tests/settings.json" : "user://settings/llm-v1.json");

    private void InitializeLlm()
    {
        if (LlmRuntime.Initialized) return;
        try { LlmRuntime.Configure(LlmSettingsStore.Load(LlmSettingsPath)); }
        catch (LlmException e) { _llmLoadIssue = e.Message; }
    }
    private void CancelLlmTest()
    {
        _llmPageVersion++;
        _llmTest?.Cancel(); // The request owner disposes it after its continuation completes.
        _llmTest = null;
    }
    public override void _ExitTree() => CancelLlmTest();

    private void ShowLlmSettings()
    {
        ClearPage("设置 / AI 服务");
        _content.AddChild(Label("AI 服务",30,Ink));
        var panel = Card(_content);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        panel.AddChild(scroll);
        var form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        form.AddThemeConstantOverride("separation",10); scroll.AddChild(form);
        var config = LlmRuntime.Configuration;
        var settings = config.Settings;
        HBoxContainer Row(string title)
        {
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation",16); form.AddChild(row);
            var label = Label(title,16,Ink); label.CustomMinimumSize = new(170,36); row.AddChild(label); return row;
        }
        LineEdit Input(string title,string value,bool secret=false)
        {
            var input = new LineEdit { Text = value, Secret = secret, MaxLength = secret ? 4096 : 512,
                CustomMinimumSize = new(260,36), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            Row(title).AddChild(input); UiAccess.Name(input,title); return input;
        }
        SpinBox Number(string title,int value,int min,int max)
        {
            var input = new SpinBox { MinValue=min,MaxValue=max,Step=1,Value=value,CustomMinimumSize=new(180,36) };
            Row(title).AddChild(input);UiAccess.Name(input,title);return input;
        }
        _llmProvider = new OptionButton { CustomMinimumSize=new(260,36),SizeFlagsHorizontal=SizeFlags.ExpandFill };
        _llmProvider.AddItem("DeepSeek"); _llmProvider.AddItem("自定义 Chat Completions 兼容接口");
        _llmProvider.Select(settings.Provider == "deepseek" ? 0 : 1);Row("供应商").AddChild(_llmProvider);UiAccess.Name(_llmProvider,"LLM 供应商");
        _llmUrl=Input("接口地址",settings.BaseUrl); _llmUrl.PlaceholderText="https://服务地址/v1";
        _llmModel=Input("模型名称",settings.Model);_llmModel.MaxLength=128;
        _llmKey=Input("API 密钥",config.ApiKey,true);_llmKey.PlaceholderText="本机免密服务可留空";
        _llmRemember=new CheckBox { Text="在此电脑保存密钥（未加密）",ButtonPressed=config.RememberKey };
        form.AddChild(_llmRemember);UiAccess.Name(_llmRemember,"在此电脑保存密钥（未加密）","不勾选时密钥只在本次启动有效；取消勾选并保存会移除已保存密钥。");
        _llmTimeout=Number("超时（秒）",settings.TimeoutSeconds,5,180);
        _llmTokens=Number("单次输出 Token 上限",settings.MaxOutputTokens,128,8192);
        _llmLimit=Number("本次启动请求上限",settings.SessionRequestLimit,1,10000);
        form.AddChild(Label("最多同时请求 2 次。测试会向当前地址发送一次小型请求，可能产生费用；不发送营地数据。",14,Muted,true));
        form.AddChild(Label("DeepSeek 显式关闭思考；兼容接口使用供应商默认模式，需支持 JSON 输出。",14,Muted,true));
        _llmStatus=Label(_llmLoadIssue!=""?_llmLoadIssue:$"尚未测试 · 本次启动已使用 {LlmRuntime.Client.Requests} 次请求额度",14,Gold,true);
        _llmStatus.AccessibilityLive=AccessibilityServer.AccessibilityLiveMode.Polite;_content.AddChild(_llmStatus);
        var footer=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.End};_content.AddChild(footer);
        Action(footer,"llm-back","返回（不保存）",ShowHome);
        var cancel=Action(footer,"llm-cancel","取消测试",()=>{_llmTest?.Cancel();});cancel.Disabled=true;
        Action(footer,"llm-test","测试 JSON 调用",()=>_ = TestLlmAsync());
        Action(footer,"llm-save","保存设置",SaveLlmSettings,true);
        _llmProvider.ItemSelected+=index=>
        {
            _llmKey.Text="";_llmRemember.ButtonPressed=false;
            if(index==0){_llmUrl.Text="https://api.deepseek.com";_llmModel.Text="deepseek-flash";}
            else {_llmUrl.Text="";_llmModel.Text="";}
        };
        void Changed()
        {
            if(_llmTest is not null) _llmTest.Cancel();
            _llmStatus.Text="配置已修改，保存后生效；测试使用当前填写的配置。";
        }
        foreach(var input in new[]{_llmUrl,_llmModel,_llmKey})input.TextChanged+=_=>Changed();
        foreach(var input in new[]{_llmTimeout,_llmTokens,_llmLimit})input.ValueChanged+=_=>Changed();
        _llmRemember.Toggled+=_=>Changed();
    }
    private LlmConfiguration ReadLlmForm()
    {
        var config=new LlmConfiguration
        {
            Settings=new() { Provider=_llmProvider.Selected==0?"deepseek":"compatible", BaseUrl=_llmUrl.Text.Trim(),Model=_llmModel.Text.Trim(),
                TimeoutSeconds=(int)_llmTimeout.Value,MaxOutputTokens=(int)_llmTokens.Value,SessionRequestLimit=(int)_llmLimit.Value },
            ApiKey=_llmKey.Text.Trim(),RememberKey=_llmRemember.ButtonPressed
        };
        config.Validate();return config;
    }
    private void SaveLlmSettings()
    {
        try
        {
            var config=ReadLlmForm();LlmSettingsStore.Save(LlmSettingsPath,config);LlmRuntime.Configure(config);_llmLoadIssue="";
            _llmStatus.Text=config.RememberKey?"设置已保存，密钥保存在此电脑。":"设置已保存，密钥仅在本次启动有效。";
        }
        catch(LlmException e){_llmStatus.Text=e.Message;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){_llmStatus.Text="无法保存设置，请检查用户目录权限。";}
    }
    private async Task TestLlmAsync()
    {
        if(_llmTest is not null)return;
        LlmConfiguration config;
        try { config=ReadLlmForm(); }
        catch(LlmException e){_llmStatus.Text=e.Message;return;}
        if(config.ApiKey==""&&!config.Settings.Endpoint().IsLoopback){_llmStatus.Text="请先填写 API 密钥。";return;}
        using var cancel=new CancellationTokenSource();_llmTest=cancel;
        int page=_llmPageVersion;_actions["llm-test"].Disabled=true;_actions["llm-cancel"].Disabled=false;_actions["llm-save"].Disabled=true;
        _llmStatus.Text="正在测试连接与工具 JSON…";
        try
        {
            var reply=await LlmConnectionProbe.RunAsync(_llmClient,config,cancel.Token);
            if(page==_llmPageVersion&&!cancel.IsCancellationRequested)
                _llmStatus.Text=$"测试通过 · {reply.Milliseconds} ms · 输入 {reply.InputTokens?.ToString()??"未报告"} / 输出 {reply.OutputTokens?.ToString()??"未报告"} Token · 配置尚需保存";
        }
        catch(OperationCanceledException){if(page==_llmPageVersion)_llmStatus.Text="测试已取消；已发出的请求仍可能计费。";}
        catch(LlmException e){if(page==_llmPageVersion)_llmStatus.Text=e.Message;}
        finally
        {
            if(page==_llmPageVersion){_llmTest=null;_actions["llm-test"].Disabled=false;_actions["llm-cancel"].Disabled=true;_actions["llm-save"].Disabled=false;}
        }
    }
}
