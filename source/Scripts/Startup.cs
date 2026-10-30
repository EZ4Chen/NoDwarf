using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Startup : Control
{
    private WorldGenerationSettings _settings=new(){Revision=2};
    private WorldGenerationSettings? _atlasIdentity;
    private VBoxContainer _content=null!;
    private Label _step=null!,_message=null!,_siteInfo=null!;
    private Button _next=null!,_enter=null!;
    private LineEdit _seedInput=null!;
    private WorldAtlas _atlas=null!;
    private bool _busy;
    private readonly List<string> _savePaths=new();
    private ItemList? _saveList;
    private readonly Dictionary<string,Button> _actions=new();
    private readonly Dictionary<string,OptionButton> _options=new();
    private static readonly Color Ink=new("e3e6de"),Muted=new("9baaa7"),Gold=new("d5bb83");
    public override void _Ready()
    {
        GetTree().AutoAcceptQuit=true;
        var args=OS.GetCmdlineUserArgs();
        if(args.Any(a=>a.EndsWith("-test")||a.EndsWith("-review"))&&!args.Contains("--startup-preview-test")&&!args.Contains("--llm-settings-test"))
        {Callable.From(()=>GetTree().ChangeSceneToFile("res://Scenes/Main.tscn")).CallDeferred();return;}
        GetWindow().Title="NoDwarf · 开始";
        Theme=new Theme{DefaultFont=GameFont.Regular,DefaultFontSize=16};
        var frame=new MarginContainer();AddChild(frame);frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach(var side in new[]{"left","right"})frame.AddThemeConstantOverride("margin_"+side,64);
        foreach(var side in new[]{"top","bottom"})frame.AddThemeConstantOverride("margin_"+side,40);
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",20);frame.AddChild(column);
        var header=new HBoxContainer();column.AddChild(header);header.AddChild(Label("N O D W A R F",24,Gold));
        _step=Label("",14,Muted);_step.SizeFlagsHorizontal=SizeFlags.ExpandFill;_step.HorizontalAlignment=HorizontalAlignment.Right;header.AddChild(_step);
        _content=new VBoxContainer{SizeFlagsVertical=SizeFlags.ExpandFill};_content.AddThemeConstantOverride("separation",16);column.AddChild(_content);
        column.AddChild(Label("一片土地，几位居民。故事从这里开始。",13,Muted));
        PreferencesRuntime.Initialize();PreferencesRuntime.Apply(GetTree().Root);
        Resized+=QueueRedraw;ShowHome();
        if(args.Contains("--startup-preview-test"))RunStartupPreview();
        if(args.Contains("--llm-settings-test"))RunLlmSettingsReview();
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("111c23"));
        DrawCircle(new Vector2(Size.X*.84f,Size.Y*.17f),110,new Color("26373b"));
        for(int layer=0;layer<3;layer++)
        {
            float baseline=Size.Y*(.48f+layer*.12f);var points=new List<Vector2>{new(0,Size.Y)};
            for(int i=0;i<=16;i++)points.Add(new(Size.X*i/16,baseline+(float)Math.Sin(i*1.8+layer)*52+(i%2)*25));
            points.Add(new(Size.X,Size.Y));DrawColoredPolygon(points.ToArray(),new Color(new[]{"1a2c32","203239","263b3d"}[layer]));
        }
    }
    private static Label Label(string text,int size,Color color,bool wrap=false)
    {
        var label=new Label{Text=text,AutowrapMode=wrap?TextServer.AutowrapMode.WordSmart:TextServer.AutowrapMode.Off};
        label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color);return label;
    }
    private static StyleBoxFlat Style(string color,int pad=14)=>new(){BgColor=new Color(color),BorderColor=new Color("596a6d"),BorderWidthBottom=1,BorderWidthTop=1,BorderWidthLeft=1,BorderWidthRight=1,ContentMarginLeft=pad,ContentMarginRight=pad,ContentMarginTop=pad,ContentMarginBottom=pad};
    private Button Action(Node parent,string id,string text,Action action,bool primary=false)
    {
        var button=new Button{Text=text,CustomMinimumSize=new(180,46),MouseDefaultCursorShape=CursorShape.PointingHand};
        button.AddThemeStyleboxOverride("normal",Style(primary?"625a42":"27383f",10));button.AddThemeStyleboxOverride("hover",Style(primary?"7b7051":"3a5058",10));
        button.AddThemeStyleboxOverride("focus",UiAccess.FocusRing());button.AddThemeStyleboxOverride("disabled",Style("1d282e",10));
        button.AddThemeColorOverride("font_color",Ink);button.Pressed+=()=>{if(!_busy)action();};parent.AddChild(button);_actions[id]=button;return button;
    }
    private void ClearPage(string step)
    {
        foreach(var node in _content.GetChildren()){_content.RemoveChild(node);node.QueueFree();}
        _actions.Clear();_options.Clear();_step.Text=step;
    }
    private VBoxContainer Card(Node parent,float width=0)
    {
        var panel=new PanelContainer{CustomMinimumSize=new(width,0),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill};panel.AddThemeStyleboxOverride("panel",Style("17262eed",24));parent.AddChild(panel);
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",14);panel.AddChild(column);return column;
    }
    private void ShowHome()
    {
        ClearPage("营地模拟 · 开始");var row=new HBoxContainer{SizeFlagsVertical=SizeFlags.ExpandFill};row.AddThemeConstantOverride("separation",56);_content.AddChild(row);
        var intro=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill,Alignment=BoxContainer.AlignmentMode.Center};intro.AddThemeConstantOverride("separation",24);row.AddChild(intro);
        intro.AddChild(Label("在旷野中\n建立你的营地",48,Ink));intro.AddChild(Label("选择世界的气候与地形，\n寻找落脚之地，安顿第一批居民。",20,Muted));
        var choices=Card(row,340);choices.Alignment=BoxContainer.AlignmentMode.Center;
        var menuPanel=choices.GetParent<Control>();menuPanel.SizeFlagsHorizontal=SizeFlags.ShrinkEnd;menuPanel.SizeFlagsVertical=SizeFlags.ShrinkCenter;menuPanel.CustomMinimumSize=new(340,380);
        choices.AddChild(Label("新的故事",22,Gold));Action(choices,"new","新游戏",ShowSettings,true);
        Action(choices,"load","读取存档",()=>ShowSaves());Action(choices,"settings","设置",ShowGameSettings);Action(choices,"quit","退出游戏",()=>GetTree().Quit());
        choices.AddChild(Label("新游戏不会覆盖已有存档。",13,Muted));
    }
    private void ShowSettings()
    {
        ClearPage("01  世界参数   /   02  选择落脚点");_content.AddChild(Label("创建一个世界",32,Ink));
        var row=new HBoxContainer{SizeFlagsVertical=SizeFlags.ExpandFill};row.AddThemeConstantOverride("separation",24);_content.AddChild(row);
        var formCard=Card(row,520);formCard.AddChild(Label("世界参数",20,Gold));
        var scroll=new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true};formCard.AddChild(scroll);
        var form=new VBoxContainer{SizeFlagsHorizontal=SizeFlags.ExpandFill};form.AddThemeConstantOverride("separation",12);scroll.AddChild(form);
        var seedRow=new HBoxContainer();form.AddChild(seedRow);seedRow.AddChild(Label("世界种子",16,Ink));
        _seedInput=new LineEdit{Text=_settings.Seed.ToString(),CustomMinimumSize=new(180,40),SizeFlagsHorizontal=SizeFlags.ExpandFill,PlaceholderText="输入整数种子"};seedRow.AddChild(_seedInput);UiAccess.Name(_seedInput,"世界种子","整数；相同种子与参数生成相同世界。");
        Action(seedRow,"random","随机种子",()=>{_seedInput.Text=unchecked((int)GD.Randi()).ToString();_seedInput.EmitSignal(LineEdit.SignalName.TextChanged,_seedInput.Text);}).CustomMinimumSize=new(100,40);
        AddChoice(form,"size","世界规模",new[]{"小 · 65 × 65","中 · 129 × 129","大 · 257 × 257"},Array.IndexOf(new[]{65,129,257},_settings.WorldSize),v=>_settings=_settings with {WorldSize=new[]{65,129,257}[v],SiteX=0,SiteY=0},"改变世界地区格数；实际营地仍为 255 × 255 格。");
        AddChoice(form,"land","陆地比例",new[]{"25% · 海洋为主","50% · 海陆均衡","75% · 陆地为主"},(_settings.LandPercent-25)/25,v=>_settings=_settings with {LandPercent=25+v*25},"控制陆地面积，不改变可玩高度。");
        AddSetting(form,"scale","地形尺度",new[]{"细碎","较小","适中","较大","广阔"},_settings.FeatureScale,v=>_settings=_settings with {FeatureScale=v},"控制大陆、岛屿与局部丘陵的宽度。");
        AddSetting(form,"ruggedness","地形起伏",new[]{"平缓","较平","适中","起伏","崎岖"},_settings.Ruggedness,v=>_settings=_settings with {Ruggedness=v},"控制世界与营地的高低起伏，出生平台保持平整。");
        form.AddChild(Label("气候与水系",18,Gold));
        AddSetting(form,"temperature","总体温度",new[]{"严寒","偏冷","适中","偏热","炎热"},_settings.Temperature,v=>_settings=_settings with {Temperature=v},"影响各地区气温和作物的生长速度。");
        AddSetting(form,"moisture","总体降水",new[]{"干旱","偏干","适中","湿润","潮湿"},_settings.Moisture,v=>_settings=_settings with {Moisture=v},"影响降水场、河流汇水与植被；湿润度另受排水影响。");
        AddSetting(form,"elevation","总体海拔",new[]{"低地","偏低","适中","偏高","高地"},_settings.Elevation,v=>_settings=_settings with {Elevation=v},"改变陆地海拔与山地分布；海陆比例独立设置。");
        AddSetting(form,"drainage","排水能力",new[]{"积水","较弱","适中","较强","畅通"},_settings.Drainage,v=>_settings=_settings with {Drainage=v},"影响土壤湿润度、地表汇流与湖盆蓄水。");
        AddChoice(form,"erosion","侵蚀程度",new[]{"无","轻微","中等","明显"},_settings.Erosion,v=>_settings=_settings with {Erosion=v},"平缓高低地并加深河谷。");
        AddChoice(form,"rivers","河流丰富度",new[]{"无河流","适中","丰富"},_settings.Rivers,v=>_settings=_settings with {Rivers=v},"按实际降水汇流生成河网；无河流时仍可能有天然湖泊。");
        form.AddChild(Label("地质与地下",18,Gold));
        AddChoice(form,"minerals","矿物丰度",new[]{"稀少","适中","丰富"},_settings.Minerals,v=>_settings=_settings with {Minerals=v},"改变铁、煤矿脉的数量。");
        AddChoice(form,"soil","土层厚度",new[]{"薄 · 1 格","中 · 3 格","厚 · 6 格"},Array.IndexOf(new[]{1,3,6},_settings.SoilDepth),v=>_settings=_settings with {SoilDepth=new[]{1,3,6}[v]},"土层下依次为地区岩石、石灰岩、花岗岩和深板岩。");
        AddChoice(form,"aquifers","含水层",new[]{"无","适中","丰富"},_settings.Aquifers,v=>_settings=_settings with {Aquifers=v},"生成有限储量的地下水库，较高降水允许缓慢补给。");
        AddChoice(form,"caverns","洞穴层数",new[]{"无","1 层","2 层","3 层"},_settings.CavernLayers,v=>_settings=_settings with {CavernLayers=v},"地下天然洞室与通道，挖通后发现；不改变地图总高度。");
        AddChoice(form,"cavernSize","洞穴开阔度",new[]{"狭窄","适中","开阔"},_settings.CavernSize,v=>_settings=_settings with {CavernSize=v},"改变洞室宽度与高度，同层由通道连接。");
        var preview=Card(row,400);preview.AddChild(Label("世界预览",20,Gold));
        _atlas=new WorldAtlas{Interactive=false,CustomMinimumSize=new(380,250),SizeFlagsVertical=SizeFlags.ExpandFill};preview.AddChild(_atlas);_atlas.SetWorld(_settings);AddAtlasLayers(preview);
        _message=Label("营地：255 × 255 格",14,Muted);_content.AddChild(_message);
        var footer=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.End};_content.AddChild(footer);Action(footer,"back","返回",ShowHome);
        _next=Action(footer,"next","下一步：选择落脚点",()=>{var identity=_settings with {SiteX=0,SiteY=0};
            if(_atlasIdentity!=identity){_settings=_settings.WithRecommendedSite();_atlasIdentity=identity;}
            ShowAtlas();},true);
        _seedInput.TextChanged+=text=>{bool valid=int.TryParse(text,out int seed);_next.Disabled=!valid;_message.Text=valid?"营地：255 × 255 格":"种子需要是 -2147483648 至 2147483647 之间的整数。";if(valid){_settings=_settings with {Seed=seed};_atlas.SetWorld(_settings);}};
    }
    private void AddSetting(Node parent,string id,string title,string[] choices,int value,Action<int> change,string help)
        =>AddChoice(parent,id,title,choices,value+2,index=>change(index-2),help);
    private void AddChoice(Node parent,string id,string title,string[] choices,int value,Action<int> change,string help)
    {
        var row=new HBoxContainer();parent.AddChild(row);var label=Label(title,16,Ink);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(label);
        var option=new OptionButton{CustomMinimumSize=new(220,38)};foreach(var choice in choices)option.AddItem(choice);option.Select(Math.Max(0,value));row.AddChild(option);_options[id]=option;
        option.ItemSelected+=index=>{change((int)index);_atlas.SetWorld(_settings);};
        option.TooltipText=help;UiAccess.Name(option,title,help);
    }
    private void ShowAtlas()
    {
        ClearPage("01  世界参数   /   02  选择落脚点");_content.AddChild(Label("选择落脚之地",32,Ink));
        var row=new HBoxContainer{SizeFlagsVertical=SizeFlags.ExpandFill};row.AddThemeConstantOverride("separation",24);_content.AddChild(row);
        var map=Card(row,720);map.AddChild(Label("点击陆地或用方向键选择地区",17,Gold));
        _atlas=new WorldAtlas{CustomMinimumSize=new(640,370),SizeFlagsVertical=SizeFlags.ExpandFill};map.AddChild(_atlas);_atlas.SetWorld(_settings);AddAtlasLayers(map);
        var details=Card(row,340);details.AddChild(Label("落脚点信息",20,Gold));_siteInfo=Label("",17,Ink,true);_siteInfo.AccessibilityLive=AccessibilityServer.AccessibilityLiveMode.Polite;_siteInfo.SizeFlagsVertical=SizeFlags.ExpandFill;details.AddChild(_siteInfo);
        Action(details,"recommend","推荐落脚点",()=>{_settings=_settings.WithRecommendedSite();UpdateSite();});
        details.AddChild(Label("开局 3 位居民，携带基础物资。",14,Muted,true));
        var footer=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.End};_content.AddChild(footer);Action(footer,"back","返回参数",ShowSettings);
        _enter=Action(footer,"enter","建立营地 →",()=>_ = EnterWorld(),true);
        _atlas.Selected=(x,y)=>{_settings=_settings with {SiteX=x,SiteY=y};UpdateSite();};UpdateSite();
    }
    private void AddAtlasLayers(Node parent)
    {
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",12);parent.AddChild(row);
        row.AddChild(Label("地图图层",15,Muted));
        var picker=new OptionButton{CustomMinimumSize=new(140,32)};row.AddChild(picker);UiAccess.Name(picker,"世界地图图层","只改变地图显示，不改变生成参数或落脚点");
        foreach(var name in new[]{"生态","海拔","湿润度","温度","降水","排水","水系"})picker.AddItem(name);
        var legend=Label(_atlas.Legend,13,Muted);legend.AutowrapMode=TextServer.AutowrapMode.WordSmart;parent.AddChild(legend);
        var atlas=_atlas;picker.ItemSelected+=id=>{atlas.Layer=(AtlasLayer)id;legend.Text=atlas.Legend;};
    }
    private void UpdateSite()
    {
        var site=_settings.Site;_enter.Disabled=!site.Land;_atlas.SetWorld(_settings);
        _siteInfo.Text=site.Land?$"{RegionProfile.Get(site.Region).Name}\n\n地区 {_settings.SiteX+1}, {_settings.SiteY+1}\n年均温度 {site.Temperature:F1}°C\n湿润度 {site.Wetness:P0}\n营地高度 {site.Height} m\n{(site.RiverWidth>0?$"河流约 {site.RiverWidth} 格宽 · 过河需通路":"无地表河流")}{(site.Lake?" · 湖泊":"")}\n含水层 {new[]{"无","适中","丰富"}[_settings.Aquifers]} · 洞穴 {_settings.CavernLayers} 层\n\n{(site.Temperature<4?"寒冷：露天种植窗口较短。":site.Temperature>26?"炎热：注意作物耐温范围。":site.Wetness<.3?"干燥：树木较少，注意木材供应。":"气候较温和，适合初次定居。")}":"海洋\n\n这里无法建立营地。\n请选择陆地上的地区。";
    }
    private string SaveDirectory=>ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--startup-preview-test")?"res://.local/startup-tests":"user://saves");
    private static string SaveLabel(string path)=>path.EndsWith(".json.bak",StringComparison.OrdinalIgnoreCase)
        ?Path.GetFileNameWithoutExtension(path[..^4])+" · 上一次备份":Path.GetFileNameWithoutExtension(path);
    private void ShowSaves(string error="")
    {
        ClearPage("读取营地");_content.AddChild(Label("继续之前的故事",32,Ink));_savePaths.Clear();
        if(Directory.Exists(SaveDirectory))_savePaths.AddRange(Directory.EnumerateFiles(SaveDirectory).Where(p=>p.EndsWith(".json")||p.EndsWith(".json.bak")).OrderByDescending(File.GetLastWriteTimeUtc).Take(100));
        var card=Card(_content);_saveList=new ItemList{CustomMinimumSize=new(700,300),SizeFlagsVertical=SizeFlags.ExpandFill};card.AddChild(_saveList);
        foreach(string path in _savePaths)
        {var info=new FileInfo(path);_saveList.AddItem($"{SaveLabel(path)}    ·    {info.LastWriteTime:yyyy-MM-dd HH:mm}    ·    {info.Length/1024d:N0} KB");}
        _message=Label(error!=""?error:_savePaths.Count==0?"还没有营地存档。先开始新游戏，进入后按 F5 保存。":"选择一个存档读取。若最新存档损坏，可以尝试上一次备份。",15,error!=""?new Color("e3a18d"):Muted,true);card.AddChild(_message);
        var footer=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.End};_content.AddChild(footer);Action(footer,"back","返回",ShowHome);
        var load=Action(footer,"read","读取营地 →",()=>{var selected=_saveList.GetSelectedItems();if(selected.Length>0)_=ReadWorld(_savePaths[selected[0]]);},true);load.Disabled=_savePaths.Count==0;
        if(_savePaths.Count>0)_saveList.Select(0);
    }
    private void ShowBusy(string message)
    {
        _busy=true;ClearPage("准备营地");var card=Card(_content);card.Alignment=BoxContainer.AlignmentMode.Center;
        card.AddChild(Label(message,30,Gold));card.AddChild(Label("正在准备地形、居民与物资，请稍候……",17,Muted));
    }
    private async Task EnterWorld()
    {
        if(_busy)return;var settings=_settings;settings.Validate();ShowBusy("正在建立营地");
        try
        {
            var world=await Task.Run(()=>new DemoWorld(settings.LocalSeed,region:settings.Site.Region,generation:settings));
            string savePath=Path.Combine(SaveDirectory,$"colony-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.json");
            world.EnableResidentMind(PreferencesRuntime.Current.ResidentAi);world.ResidentMind.IntervalHours=PreferencesRuntime.Current.ResidentIntervalHours;world.EnableStory(PreferencesRuntime.Current.Storyteller);
            GameLaunch.Pending=new(world,SavePath:savePath);GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
        }
        catch(Exception e){_busy=false;ShowSettings();_message.Text="生成失败："+e.Message;}
    }
    private async Task ReadWorld(string path)
    {
        if(_busy)return;ShowBusy("正在读取营地");
        try
        {
            var prepared=await Task.Run(()=>Main.PrepareSession(path));
            string target=path.EndsWith(".bak")?path[..^4]:path;
            if(!Path.GetFullPath(target).StartsWith(Path.GetFullPath(SaveDirectory)+Path.DirectorySeparatorChar,StringComparison.Ordinal))target=Path.Combine(SaveDirectory,"colony-save.json");
            GameLaunch.Pending=new(prepared.World,prepared,target);
            GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
        }
        catch(Exception e){_busy=false;ShowSaves("无法读取此存档："+e.Message);}
    }
}
