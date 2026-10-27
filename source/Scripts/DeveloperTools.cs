using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private bool _developerMode, _creative=true, _revealUnknown;
    private PanelContainer _developerPanel=null!, _consolePanel=null!;
    private RichTextLabel _consoleOutput=null!;
    private LineEdit _commandInput=null!;
    private Label _developerStatus=null!;
    private SessionLog _sessionLog=null!;
    private DemoWorld? _loggedWorld;
    private long _shownLog=-1;
    private readonly List<string> _commandHistory=new();
    private int _historyIndex;
    private long _logStart;
    private void StartLogging()
    {
        _sessionLog=new SessionLog(ProjectSettings.GlobalizePath("user://logs"));
        BindWorldLog();
        AppDomain.CurrentDomain.UnhandledException+=LogUnhandled;
        _sessionLog.Write(0,"INFO","应用","启动开发调试版");
    }
    private void BindWorldLog()
    {
        if(_loggedWorld==_world)return;
        if(_loggedWorld is not null)_loggedWorld.Logged-=RecordWorldEvent;
        _loggedWorld=_world;_world.Logged+=RecordWorldEvent;
        foreach(var e in _world.Events.AsEnumerable().Reverse())RecordWorldEvent(e);
    }
    private void LogUnhandled(object sender,UnhandledExceptionEventArgs e)=>_sessionLog.Write(_clock.Tick,"ERROR","应用",e.ExceptionObject.ToString()??"未知异常");
    private void RecordWorldEvent(string message)=>_sessionLog.Write(_clock.Tick,"INFO","世界",message);
    public override void _ExitTree()
    {
        GetWindow().CloseRequested-=RequestWindowQuit;
        if(_loggedWorld is not null)_loggedWorld.Logged-=RecordWorldEvent;
        AppDomain.CurrentDomain.UnhandledException-=LogUnhandled;
        _sessionLog?.Dispose();
    }
    private void BuildDeveloperUi()
    {
        var layer=new CanvasLayer{Layer=20};AddChild(layer);
        var root=new Control{MouseFilter=Control.MouseFilterEnum.Ignore};layer.AddChild(root);root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _developerPanel=Panel(root,new Vector2(300,150),new Vector2(500,0));_developerPanel.Hide();
        var box=new VBoxContainer();_developerPanel.AddChild(box);
        _developerStatus=new Label();box.AddChild(_developerStatus);
        Button(box,"开启 / 关闭开发者模式",()=>RunCommand(_developerMode?"dev off":"dev on"));
        Button(box,"直接编辑 / 普通规划（开发模式）",()=>RunCommand(_creative?"creative off":"creative on"));
        Button(box,"移除居民",()=>RunCommand("residents off"));
        Button(box,"添加 / 恢复居民",()=>RunCommand("residents on"));
        Button(box,"立即完成现有施工规划",()=>RunCommand("finish"));
        var waterTools=new HBoxContainer();box.AddChild(waterTools);
        Button(waterTools,"选中格注水 1000 L",()=>EditSelectedWater(false));Button(waterTools,"选中格排水",()=>EditSelectedWater(true));
        Button(box,"查看寻路统计",()=>{RunCommand("navstats");ToggleConsole();});
        Button(box,"显示 / 隐藏未知岩层（开发模式）",()=>RunCommand(_revealUnknown?"fog on":"fog off"));
        var time=new HBoxContainer();box.AddChild(time);
        Button(time,"暂停",()=>RunCommand("pause"));Button(time,"继续",()=>RunCommand("resume"));Button(time,"推进 1 tick",()=>RunCommand("step"));
        Button(box,"打开日志目录",()=>OS.ShellOpen(ProjectSettings.GlobalizePath("user://logs")));
        Button(box,"关闭 [F1]",()=>ToggleDeveloperPanel());
        _consolePanel=Panel(root,Vector2.Zero,Vector2.Zero);_consolePanel.Hide();
        _consolePanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _consolePanel.OffsetLeft=12;_consolePanel.OffsetRight=-12;_consolePanel.OffsetTop=-350;_consolePanel.OffsetBottom=-48;
        var console=new VBoxContainer();_consolePanel.AddChild(console);
        console.AddChild(new Label{Text="调试控制台 · help 查看命令 · ↑↓ 历史 · F2 / Esc 关闭"});
        _consoleOutput=new RichTextLabel{BbcodeEnabled=false,ScrollFollowing=true,SizeFlagsVertical=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(0,210)};
        console.AddChild(_consoleOutput);
        _commandInput=new LineEdit{PlaceholderText="例如：dev on / residents off / level -2"};console.AddChild(_commandInput);
        _commandInput.TextSubmitted+=text=>{RunCommand(text);_commandInput.Clear();};
        RefreshDeveloperUi();
    }
    private void ToggleDeveloperPanel(){_developerPanel.Visible=!_developerPanel.Visible;_consolePanel.Hide();_commandInput.ReleaseFocus();_dragging=false;Clear(_hover);RefreshDeveloperUi();}
    private void ToggleConsole(){_consolePanel.Visible=!_consolePanel.Visible;_developerPanel.Hide();_dragging=false;Clear(_hover);if(_consolePanel.Visible)_commandInput.GrabFocus();else _commandInput.ReleaseFocus();}
    private bool DeveloperInput(InputEvent input)
    {
        if(input is InputEventKey{Pressed:true,Echo:false} key)
        {
            if(key.Keycode==Key.F1){ToggleDeveloperPanel();GetViewport().SetInputAsHandled();return true;}
            if(key.Keycode==Key.F2){ToggleConsole();GetViewport().SetInputAsHandled();return true;}
            if(_consolePanel.Visible && key.Keycode==Key.Escape){ToggleConsole();GetViewport().SetInputAsHandled();return true;}
            if(_consolePanel.Visible && key.Keycode is Key.Up or Key.Down)
            {
                _historyIndex=Math.Clamp(_historyIndex+(key.Keycode==Key.Up?-1:1),0,_commandHistory.Count);
                _commandInput.Text=_historyIndex==_commandHistory.Count?"":_commandHistory[_historyIndex];
                _commandInput.CaretColumn=_commandInput.Text.Length;GetViewport().SetInputAsHandled();return true;
            }
        }
        return _consolePanel.Visible || _developerPanel.Visible;
    }
    private bool CreativeAct(Cell c)
    {
        if(_tool is "dig" or "digstairs")_world.DeveloperDig(c,_tool=="digstairs",_facing);
        else if(_tool=="erase"){if(!_world.DeveloperErase(c))_world.Log("没有可移除的建筑或规划。");}
        else if(Enum.TryParse<BuildKind>(_tool,out var kind))_world.DeveloperPlace(c,kind,_facing);
        else if(_tool=="blueprint")_world.Log("蓝图仍按正常施工规划；直接编辑请使用单块工具。");
        else return false;
        return true;
    }
    private void RefreshDeveloperUi()
    {
        if(_developerStatus is null)return;
        _developerStatus.Text=$"开发者模式：{(_developerMode?"开":"关")} · {(_creative?"立即编辑，不耗料":"普通规划")}\n未知岩层：{(_revealUnknown?"显示":"隐藏")} · 居民 {_world.Workers.Count}\n{(_sessionLog.WriteError is null?"日志已写入磁盘":"日志写入失败："+_sessionLog.WriteError)}";
        if(_shownLog==_sessionLog.Version)return;
        _shownLog=_sessionLog.Version;
        _consoleOutput.Text=string.Join("\n",_sessionLog.Snapshot().TakeLast((int)Math.Min(400,_sessionLog.Version-_logStart)).Select(r=>$"{r.Time:HH:mm:ss} [{r.Level}/{r.Category}] T{r.Tick} {r.Message}"));
    }
    private void RunCommand(string text)
    {
        text=text.Trim();if(text.Length==0)return;
        _commandHistory.Add(text);if(_commandHistory.Count>100)_commandHistory.RemoveAt(0);_historyIndex=_commandHistory.Count;
        _sessionLog.Write(_clock.Tick,"INFO","命令",text);
        try {ExecuteCommand(text.Split(' ',StringSplitOptions.RemoveEmptyEntries));}
        catch(Exception e){_sessionLog.Write(_clock.Tick,e is ArgumentException?"WARN":"ERROR","控制台",e.Message);}
        SyncWorld();RefreshUi();
    }
    private void ExecuteCommand(string[] a)
    {
        void Count(int n){if(a.Length!=n)throw new ArgumentException("参数数量不正确；输入 help 查看格式。");}
        void Dev(){if(!_developerMode)throw new ArgumentException("请先执行 dev on。");}
        bool Switch(){Count(2);return a[1] switch{"on"=>true,"off"=>false,_=>throw new ArgumentException("请使用 on 或 off。")};}
        int Number(int i,int min=-100,int max=100){if(i>=a.Length||!int.TryParse(a[i],out int n)||n<min||n>max)throw new ArgumentException("坐标或数字无效。");return n;}
        Cell Position(int i)=>new(Number(i,0,TerrainMap.Size-1),Number(i+1,0,TerrainMap.Size-1),Number(i+2,TerrainMap.MinLevel+1,TerrainMap.MaxLevel));
        switch(a[0].ToLowerInvariant())
        {
            case "help":
                Count(1);RecordWorldEvent("人物调试（开发模式）：personxp 居民ID 技能ID 经验 | injure 居民ID 部位ID cut/fracture/burn/missing 严重度 | give 物品ID 数量\n" + "region [类型] [种子]（region 查看全部类型） | weather [auto/clear/cloudy/rain/fog] | water X Z Y 升 | drain X Z Y | aquifer X Z Y 储量 水头 流量 补给 温度 | gate X Z Y | fluidstats | help | status | navstats | navmode exact/hierarchy | dev on/off | creative on/off | fog on/off\npause | resume | step [1..300] | level 高度 | residents on/off | finish\nplace 方块ID X Z 高度 [朝向0..3] | dig X Z 高度 [stairs 朝向0..3]\nerase X Z 高度 | clear | logs\n直接放置不耗料；直接挖掘天然方块会掉落物品；finish 完成已有规划，沿用其材料结算。");break;
            case "personxp":
                Dev();Count(4);
                var learner=_world.Workers.FirstOrDefault(w=>w.Id==a[1])??throw new ArgumentException("未知居民 ID");
                if(!ResidentCatalog.Skills.Any(s=>s.Id==a[2]))throw new ArgumentException("未知技能 ID");
                learner.Person.Earn(a[2],Number(3,1,100000));RecordWorldEvent("[开发] 已增加工作经验；技能点按累计经验结算。");break;
            case "injure":
                Dev();Count(5);var injury=_world.InjureResident(a[1],a[2],a[3],Number(4,1,100));
                if(!injury.Ok)throw new ArgumentException(injury.Message);RecordWorldEvent("[开发] "+injury.Message);break;
            case "give":
                Dev();Count(3);_world.SpawnItem(a[1],Number(2,1,1000),_selectedWorker>=0&&_selectedWorker<_world.Workers.Count?_world.Workers[_selectedWorker].Tile:_world.Camp+new Cell(4,8));RecordWorldEvent("[开发] 已生成地面物品，需由居民拾取。");break;
            case "navstats":
                Count(1);var nav=_world.Navigation;
                RecordWorldEvent($"寻路：{(nav.UseHierarchy?"分层（实验）":"精确 A*（默认）")} · 道路版本 {nav.Version} · 缓存节点 {nav.CachedNodes}\n累计请求 {nav.Searches} · 展开节点 {nav.ExpandedNodes} · 重建分块 {nav.RebuiltChunks} · 连通性拒绝 {nav.RejectedUnreachable}\n自动找工作使用共享图 BFS；每 tick 最多 {_world.NavigationRequestsPerTick} 次自动寻路请求。");break;
            case "navmode":
                Dev();Count(2);
                _world.Navigation.UseHierarchy=a[1] switch {"exact"=>false,"hierarchy"=>true,_=>throw new ArgumentException("navmode exact/hierarchy")};
                RecordWorldEvent(_world.Navigation.UseHierarchy?"后续点对点寻路使用实验分层模式，可能绕路。":"后续点对点寻路使用精确 A*。");break;
            case "water":
                Dev();Count(5);RecordWorldEvent($"已加入 {_world.Fluids.Add(Position(1),Number(4,1,1000))} L 水（不能加入固体或关闭的闸门）。");break;
            case "drain":
                Dev();Count(4);RecordWorldEvent($"移除 {_world.Fluids.Drain(Position(1))} L 水，已计入外部排水。");break;
            case "aquifer":
                Dev();Count(9);var sourceCell=Position(1);int stored=Number(4,1,10000000);
                _world.Fluids.AddSource(new Aquifer{Cell=sourceCell,Capacity=stored,Stored=stored,Head=Number(5,sourceCell.Level+1,TerrainMap.MaxLevel+1),Rate=Number(6,5,5000),Recharge=Number(7,0,5000),Temperature=Number(8,0,100)});RecordWorldEvent("已设置有限含水层。挖通相邻格会出水。");break;
            case "gate":
                Count(4);if(!_world.ToggleFloodgate(Position(1)))throw new ArgumentException("此处没有闸门。");break;
            case "fluidstats":
                Count(1);RecordWorldEvent($"水 {_world.Fluids.Cells.Count} 格 / 待更新 {_world.Fluids.Pending} / 质量 {_world.Fluids.Mass} L / 守恒 {_world.Fluids.Conserved} / 上限 {_world.Fluids.AtLimit}");break;
            case "weather":
                if(a.Length==1){RecordWorldEvent($"当前天气：{_world.Weather.Name} · 纬度 {_world.Latitude:F1}°\nweather auto / clear / cloudy / rain / fog");break;}
                Dev();Count(2);_world.SetWeather(a[1]);break;
            case "region":
                if(a.Length==1){RecordWorldEvent("当前地区："+(_world.Terrain.Region?.Name??"旧版地形")+"\n"+string.Join(" | ",RegionProfile.All.Select(p=>p.Id+"："+p.Name))+"\nregion 类型 [种子]：确认后重新生成，当前营地会清空。");break;}
                Dev();if(a.Length is not (2 or 3))throw new ArgumentException("region 类型 [种子]");
                _nextRegion=RegionProfile.Get(a[1]).Id;if(a.Length==3){_seed.Value=Number(2,0,999999);_seed.GetLineEdit().Text=((int)_seed.Value).ToString();}
                Reset();break;
            case "status":Count(1);RecordWorldEvent($"种子 {_world.Terrain.Seed} · 居民 {_world.Workers.Count} · 待办 {_world.Jobs.Count} · tick {_clock.Tick} · 高度 {ActiveLevel}");break;
            case "dev":
                bool enabled=Switch();
                _developerMode=enabled;if(enabled)_clock.Paused=true;else _revealUnknown=false;
                _selectedWorker=-1;CreatePeople();RecordWorldEvent($"开发模式 {(enabled?"开启；已暂停":"关闭")}");break;
            case "creative":Dev();_creative=Switch();break;
            case "fog":Dev();_revealUnknown=!Switch();break;
            case "residents":
                Dev();bool present=Switch();if(_characterEditor.Visible)CloseCharacterEditor();
                _world.DeveloperResidents(present);_selectedWorker=-1;_selectionPanel.Hide();CreatePeople();break;
            case "finish":Dev();Count(1);_world.DeveloperFinishPlans();break;
            case "pause":Count(1);_clock.Paused=true;break;
            case "resume":Count(1);_clock.Paused=false;break;
            case "step":
                Dev();if(a.Length>2)throw new ArgumentException("step [1..300]");
                int ticks=a.Length==2?Number(1,1,300):1;_clock.Paused=true;
                for(int i=0;i<ticks;i++){_clock.Step();_world.Update(1.0/SimulationClock.BaseTicksPerSecond);}break;
            case "level":
                Count(2);int height=Number(1,TerrainMap.MinLevel+1,TerrainMap.MaxLevel);
                _foundation??=new FoundationPlan(_world.Camp,_world.Camp+new Cell(5,7));
                _layerOffset=height-_foundation.WorldLevel;_placement=0;_cutaway=true;_focus.Y=height;PositionCamera();DrawFoundation();break;
            case "place":
                Dev();if(a.Length is not (5 or 6))throw new ArgumentException("place 方块ID X Z 高度 [朝向]");
                var kinds=Enum.GetValues<BuildKind>().Where(k=>BlockCatalog.Id(k)==a[1]||k.ToString().Equals(a[1],StringComparison.OrdinalIgnoreCase)).ToArray();
                if(kinds.Length!=1)throw new ArgumentException("未知建筑方块 ID。");
                if(!_world.DeveloperPlace(Position(2),kinds[0],a.Length==6?Number(5,0,3):0))throw new ArgumentException("放置失败，见世界日志。");break;
            case "dig":
                Dev();if(a.Length!=4 && !(a.Length==6&&a[4]=="stairs"))throw new ArgumentException("dig X Z 高度 [stairs 朝向]");
                if(!_world.DeveloperDig(Position(1),a.Length==6,a.Length==6?Number(5,0,3):0))throw new ArgumentException("挖掘失败，见世界日志。");break;
            case "erase":Dev();Count(4);if(!_world.DeveloperErase(Position(1)))throw new ArgumentException("没有可移除的建筑或规划。");break;
            case "logs":Count(1);RecordWorldEvent(_sessionLog.FilePath);break;
            case "clear":Count(1);_logStart=_sessionLog.Version;_shownLog=-1;break;
            default:throw new ArgumentException("未知命令；输入 help 查看命令。");
        }
    }
    private void CheckDeveloperConsole()
    {
        _world=new DemoWorld(flat:true);_overview.World=_world;BindWorldLog();CreatePeople();
        RunCommand("residents off");
        if(_world.Workers.Count!=3)throw new InvalidOperationException("Developer guard failed");
        RunCommand("dev on");RunCommand("residents off");
        int stock=_world.Stone;
        RunCommand("dig 20 20 -3");RunCommand("place nodwarf:anvil 20 20 -3");
        if(_world.Workers.Count!=0||!_world.Buildings.ContainsKey(new Cell(20,20,-3))||_world.Stone!=stock||_world.Jobs.Count!=0)
            throw new InvalidOperationException("Creative commands must work without residents or material spend");
        long tick=_clock.Tick;RunCommand("step 2");
        if(_clock.Tick!=tick+2||!_clock.Paused)throw new InvalidOperationException("Single-step clock incorrect");
        RunCommand("fog off");RunCommand("level -3");
        if(!CurrentView.RevealAll||CurrentView.Top!=-2)throw new InvalidOperationException("Reveal and level commands");
        int count=_world.Buildings.Count;RunCommand("place bad-id 20 21 -3");RunCommand("dig bad 20 -3");
        if(_world.Buildings.Count!=count)throw new InvalidOperationException("Malformed commands changed world");
        ToggleConsole();_commandInput.Text="wasd";_commandInput.GrabFocus();
        var focus=_focus;MoveCamera(1);
        if(_focus!=focus)throw new InvalidOperationException("Console moved camera");
        DeveloperInput(new InputEventKey{Pressed=true,Keycode=Key.Escape});
        if(_consolePanel.Visible||GetViewport().GuiGetFocusOwner()==_commandInput)throw new InvalidOperationException("Closing console retained input focus");
        RunCommand("dev off");
        if(_world.Workers.Count!=0)throw new InvalidOperationException("Disabling dev must not add residents implicitly");
        RunCommand("dev on");RunCommand("residents on");RunCommand("dev off");
        if(_world.Workers.Count!=3||CurrentView.RevealAll)throw new InvalidOperationException("Leaving dev mode must restore residents and fog");
        if(!File.Exists(_sessionLog.FilePath)||!_sessionLog.Snapshot().Any(r=>r.Category=="命令"))throw new InvalidOperationException("Session log missing");
        GD.Print("DEVELOPER_UI_OK: guarded commands, no-worker edits, single step, invalid input, console focus, restore and disk logs");
    }
}

