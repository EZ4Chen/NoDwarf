using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    // A game HUD panel, not a second OS window. Pages share typography and compact rows.
    private PanelContainer _residentPanel=null!;
    private Control _residentBackdrop=null!;
    private VBoxContainer _residentBody=null!;
    private Label _residentHeader=null!,_residentSubtitle=null!,_residentStatus=null!;
    private TextureRect _residentPortrait=null!;
    private Button _residentDetails=null!;
    private OptionButton _residentPicker=null!;
    private readonly List<Button> _residentTabs=new();
    private readonly List<ScrollContainer> _residentScrolls=new();
    private readonly Dictionary<string,Button> _residentSkillRows=new();
    private readonly Dictionary<string,Button> _residentTalentButtons=new();
    private readonly Dictionary<string,Button> _residentResearchButtons=new();
    private Button _residentNext=null!,_residentClose=null!;
    private string _dossierId="",_dossierSkill="mining",_dossierResearch="foundations";
    private int _dossierPage;
    private bool _dossierPaused,_dossierNearby;
    private static readonly Color RInk=new("e7e6de"),RMuted=new("9da5a5"),RGold=new("cfb77c"),RGreen=new("a6bd91"),RRed=new("dc9987"),RBlue=new("9dbbc7");
    private bool DossierOpen=>_residentPanel is not null&&_residentPanel.Visible;
    private DemoWorker? DossierWorker=>_world.Workers.FirstOrDefault(w=>w.Id==_dossierId);

    private void BuildResidentPanel(Control root)
    {
        _residentBackdrop=new Control{Visible=false,ZIndex=19,MouseFilter=Control.MouseFilterEnum.Stop};
        root.AddChild(_residentBackdrop);_residentBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _residentBackdrop.OffsetBottom=-40; // Keep the colony navigation reachable while a panel is open.
        _residentBackdrop.GuiInput+=input=>
        {
            if(input is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left})
            { _residentBackdrop.AcceptEvent();CloseResidentPanel(); }
        };
        _residentPanel=new PanelContainer{Name="ResidentDossier",Visible=false,Theme=root.Theme,ZIndex=20};
        _residentPanel.AddThemeStyleboxOverride("panel",RStyle("181e26","66727a",14,10));
        root.AddChild(_residentPanel);
        var column=RV(_residentPanel,8);
        var header=RH(column,14);
        _residentPortrait=new TextureRect{CustomMinimumSize=new(46,60),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore};header.AddChild(_residentPortrait);
        var identity=RV(header,4);identity.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        _residentHeader=RT(identity,"",21,RInk);
        _residentSubtitle=RT(identity,"",14,RMuted);
        _residentStatus=RT(identity,"",14,RGreen);
        var navigation=RV(header,5);navigation.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
        var pickerRow=RH(navigation,4);
        RB(pickerRow,"‹",()=>CycleResident(-1),"上一位居民",30);
        _residentPicker=new OptionButton{CustomMinimumSize=new(106,30),FocusMode=Control.FocusModeEnum.All};StyleResidentChoice(_residentPicker);UiAccess.Name(_residentPicker,"当前居民");pickerRow.AddChild(_residentPicker);
        _residentPicker.ItemSelected+=i=>SelectDossierResident((int)i);
        _residentNext=RB(pickerRow,"›",()=>CycleResident(1),"下一位居民",30);
        var closeRow=RH(navigation,8);
        RT(closeRow,"已暂停",12,RMuted).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        _residentClose=RB(closeRow,"×",CloseResidentPanel,"关闭人物面板 · Esc",30);
        var tabs=RH(column,0);
        string[] names=["人物","需求","健康","装备","社交","成长","研究","作息","工作"];
        for(int i=0;i<names.Length;i++)
        {
            int page=i;var b=RB(tabs,names[i],()=>ShowResidentPage(page));b.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;b.CustomMinimumSize=new(0,34);_residentTabs.Add(b);
        }
        // Keep long/wrapped page contents from changing the size of the HUD itself.
        var bodyViewport=new Control{SizeFlagsVertical=Control.SizeFlags.ExpandFill,ClipContents=true};column.AddChild(bodyViewport);
        _residentBody=RV(bodyViewport,8);_residentBody.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.Resized+=LayoutResidentPanel;
        LayoutResidentPanel();
    }
    private void LayoutResidentPanel()
    {
        if(_residentPanel is null)return;
        var size=_residentPanel.GetParent<Control>().Size;
        float width=Math.Min(ColonyPage?900:780,Math.Max(640,size.X-24));
        float preferred=ColonyPage&&_dossierPage!=6?Math.Min(628,350+Math.Min(8,_world.Workers.Count)*36):628;
        float height=Math.Min(preferred,Math.Max(430,size.Y-90));
        _residentPanel.Position=new(0,Math.Max(8,size.Y-height-42));
        _residentPanel.Size=new(width,height);
    }
    private void OpenResidentPanel()
    {
        if(DossierOpen||(!ColonyPage&&(_selectedWorker<0||_selectedWorker>=_world.Workers.Count)))return;
        _dossierPaused=_clock.Paused;_clock.Paused=true;_dragging=false;_orbitHeld=false;
        _dossierId=_selectedWorker>=0&&_selectedWorker<_world.Workers.Count?_world.Workers[_selectedWorker].Id:"";
        _residentPicker.Clear();foreach(var w in _world.Workers)_residentPicker.AddItem(w.Name);if(_selectedWorker>=0)_residentPicker.Select(_selectedWorker);
        _buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();_combatPanel.Hide();_selectionPanel.Hide();
        _storagePanel?.QueueFree();_storagePanel=null;
        RenderResidentPanel();LayoutResidentPanel();_residentBackdrop.Show();_residentPanel.Show();RefreshUi();
    }
    private void CloseResidentPanel()
    {
        if(!DossierOpen)return;
        _residentPanel.Hide();_residentBackdrop.Hide();_clock.Paused=_dossierPaused;
        if(_selectedWorker>=0)_selectionPanel.Show();RefreshUi();
    }
    private void SelectDossierResident(int index)
    {
        if(index<0||index>=_world.Workers.Count)return;
        _selectedResidents.Clear();_residentSelectionWorld=_world;
        _selectedWorker=index;_dossierId=_world.Workers[index].Id;_residentPicker.Select(index);_residentRosterPage=index/RosterPageSize;
        RenderResidentPanel();RefreshUi();
    }
    private void CycleResident(int step)
    {if(_world.Workers.Count>0)SelectDossierResident((_selectedWorker+step+_world.Workers.Count)%_world.Workers.Count);}
    private void ShowResidentPage(int page)
    {
        _dossierPage=page;RenderResidentPanel();LayoutResidentPanel();RefreshUi();
    }
    private void ResidentDo(string action,string target="",int item=0,string? actor=null)
    {
        var result=_world.ActResident(new(actor??_dossierId,action,target,item));
        ShowActionFeedback(result.Message,!result.Ok);
        RenderResidentPanel();RefreshUi();
    }
    private void RenderResidentPanel()
    {
        var worker=DossierWorker;
        _residentScrolls.Clear();_residentSkillRows.Clear();_residentTalentButtons.Clear();_residentResearchButtons.Clear();
        foreach(Node n in _residentBody.GetChildren()){_residentBody.RemoveChild(n);n.QueueFree();}
        _residentPortrait.Visible=!ColonyPage&&worker is not null;
        _residentHeader.Text=ColonyPage?(_dossierPage==6?"研究":_dossierPage==7?"每日作息":"工作优先级"):worker?.Name??"人物";
        _residentSubtitle.Text=ColonyPage?$"聚落管理  ·  {_world.Workers.Count} 名居民":$"{worker!.Person.Age} 岁  ·  {worker.Person.Adulthood}";
        _residentStatus.Text=ColonyPage?(_dossierPage==7?"选择安排，然后涂抹小时格":""):
            worker!.Person.Dead?"已死亡":$"{worker.Status}   /   心情 {worker.Person.Mood:F0}%   /   技能点 {worker.Person.Points}";
        _residentStatus.Visible=!ColonyPage;
        _residentStatus.AddThemeColorOverride("font_color",worker?.Person.Dead==true?RRed:RGreen);
        if(worker is not null)_residentPortrait.Texture=_people[_world.Workers.IndexOf(worker)].GetChild<Sprite3D>(0).Texture;
        for(int i=0;i<_residentTabs.Count;i++)
        {
            _residentTabs[i].Visible=ColonyPage?i>=6:i<6;
            var style=RStyle(i==_dossierPage?"66583d":"3b3930",i==_dossierPage?"b49e6f":"71674f",8,4);
            style.BorderWidthLeft=style.BorderWidthRight=style.BorderWidthTop=0;style.BorderWidthBottom=i==_dossierPage?2:1;
            _residentTabs[i].AddThemeStyleboxOverride("normal",style);_residentTabs[i].AddThemeColorOverride("font_color",i==_dossierPage?RGold:RMuted);
        }
        if(worker is null)
        {RT(_residentBody,"营地中还没有居民",18,RGold);RT(_residentBody,_developerMode?"开发模式：按 ～ 打开控制台，输入 residents on 启用居民。":"居民到来后可安排工作、作息与研究。",14,RMuted);return;}
        switch(_dossierPage)
        {
            case 0:RenderResidentBio(worker);break;
            case 1:RenderResidentNeeds(worker);break;
            case 2:RenderResidentHealth(worker);break;
            case 3:RenderResidentGear(worker);break;
            case 4:RenderResidentSocial(worker);break;
            case 5:RenderResidentGrowth(worker);break;
            case 6:RenderResidentResearch(worker);break;
            case 7:RenderResidentSchedule(worker);break;
            case 8:RenderResidentWork(worker);break;
        }
    }

    private static StyleBoxFlat RStyle(string background,string border="3c4547",int horizontal=8,int vertical=4)=>new()
    {
        BgColor=new(background),BorderColor=new(border),BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,
        ContentMarginLeft=horizontal,ContentMarginRight=horizontal,ContentMarginTop=vertical,ContentMarginBottom=vertical
    };
    private static Label RT(Node parent,string text,int size=15,Color? color=null,bool wrap=false)
    {
        var label=new Label{Text=text,MouseFilter=Control.MouseFilterEnum.Ignore,AutowrapMode=wrap?TextServer.AutowrapMode.WordSmart:TextServer.AutowrapMode.Off};
        label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",color??RInk);parent.AddChild(label);return label;
    }
    private static VBoxContainer RV(Node parent,int separation=8)
    {var box=new VBoxContainer();box.AddThemeConstantOverride("separation",separation);parent.AddChild(box);return box;}
    private static HBoxContainer RH(Node parent,int separation=8)
    {var box=new HBoxContainer();box.AddThemeConstantOverride("separation",separation);parent.AddChild(box);return box;}
    private static void RRule(Node parent)
    {
        var line=new HSeparator();line.AddThemeStyleboxOverride("separator",new StyleBoxLine{Color=new("41494a"),Thickness=1});parent.AddChild(line);
    }
    private static void RSection(Node parent,string title,string detail="")
    {
        var row=RH(parent);RT(row,title,16,RGold).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        if(detail!="")RT(row,detail,12,RMuted);
        RRule(parent);
    }
    private static Button RB(Node parent,string text,Action action,string hint="",float width=0,bool primary=false)
    {
        var b=new Button{Text=text,TooltipText=hint,CustomMinimumSize=new(width,29),FocusMode=Control.FocusModeEnum.All,MouseDefaultCursorShape=Control.CursorShape.PointingHand};
        UiAccess.Focus(b);if(text=="×")b.AccessibilityName="关闭人物面板";
        b.AddThemeFontSizeOverride("font_size",14);
        b.AddThemeColorOverride("font_color",primary?RGold:RInk);b.AddThemeColorOverride("font_disabled_color",new Color("717b7b"));
        b.AddThemeStyleboxOverride("normal",RStyle(primary?"3e4035":"303739",primary?"8a805f":"525e5f"));
        b.AddThemeStyleboxOverride("hover",RStyle("455052","9aa7a2"));b.AddThemeStyleboxOverride("pressed",RStyle("555c51","cfb77c"));
        b.AddThemeStyleboxOverride("disabled",RStyle("242a2c","394143"));b.Pressed+=action;parent.AddChild(b);return b;
    }
    private static void StyleResidentChoice(OptionButton choice)
    {
        UiAccess.Focus(choice);
        choice.AddThemeFontSizeOverride("font_size",14);choice.AddThemeColorOverride("font_color",RInk);
        choice.AddThemeStyleboxOverride("normal",RStyle("303739","525e5f"));choice.AddThemeStyleboxOverride("hover",RStyle("455052","9aa7a2"));
    }
    private (VBoxContainer Left,VBoxContainer Right) RColumns(float leftWidth)
    {
        var row=RH(_residentBody,20);row.SizeFlagsVertical=Control.SizeFlags.ExpandFill;
        var left=RV(row,8);left.CustomMinimumSize=new(leftWidth,0);
        var separator=new VSeparator();separator.AddThemeStyleboxOverride("separator",new StyleBoxLine{Color=new("41494a"),Thickness=1,Vertical=true});row.AddChild(separator);
        var right=RV(row,8);right.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;return(left,right);
    }
    private VBoxContainer RScroll(Node parent)
    {
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};parent.AddChild(scroll);
        var content=RV(scroll,5);content.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;_residentScrolls.Add(scroll);
        var bar=scroll.GetVScrollBar();bar.CustomMinimumSize=new(7,0);
        bar.AddThemeStyleboxOverride("scroll",RStyle("202527","202527",0,0));bar.AddThemeStyleboxOverride("grabber",RStyle("586261","586261",0,0));bar.AddThemeStyleboxOverride("grabber_highlight",RStyle("89958a","89958a",0,0));
        return content;
    }
    private static void RStat(Node parent,string name,string value,Color? color=null)
    {
        var row=RH(parent);row.CustomMinimumSize=new(0,25);RT(row,name,14,RMuted).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;RT(row,value,14,color??RInk);
    }
    private static ProgressBar RMeter(Node parent,double value,double max,Color color,float height=8)
    {
        var bar=new ProgressBar{Value=value,MaxValue=max,ShowPercentage=false,CustomMinimumSize=new(0,height),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,MouseFilter=Control.MouseFilterEnum.Ignore};
        bar.AddThemeStyleboxOverride("background",RStyle("171d1f","171d1f",0,0));var fill=new StyleBoxFlat{BgColor=color};bar.AddThemeStyleboxOverride("fill",fill);parent.AddChild(bar);return bar;
    }
    private string GearDescription(string item)
    {
        var definition=_world.Content.Item(item);
        if(definition.Edible is {} food)return $"可食用 · 饱腹 +{food.Nutrition:0.#} · 心情 {food.Mood:+0;-0;0} · 单件 {definition.Weight:0.##} kg";
        var g=ResidentGear.Get(item);if(g is null)return "";
        return $"重量 {g.Weight:F1} kg"+(g.Protection>0?$" · 防护 {g.Protection}%":"")+
            (g.Skill!=""?$" · {g.WorkUse switch{"forestry"=>"伐木","earth"=>"挖掘土砂","sowing"=>"播种 / 种树",_=>ResidentCatalog.Skill(g.Skill).Name}}效率 +{g.Bonus:P0}":"")+(g.TwoHanded?" · 占用双手":"");
    }
}
