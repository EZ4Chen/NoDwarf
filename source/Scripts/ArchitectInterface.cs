using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private PanelContainer _architectTools=null!;
    private Label _architectDescription=null!;
    private static string ArchitectHelp(string category)=>category switch
    {
        "全部"=>"浏览所有建筑与命令，也可搜索材料或用途。",
        "命令"=>"选择工具，再在地图上指定对象或范围。",
        "区域"=>"划定范围后，点击区域设置。",
        "结构"=>"选择材料，在地图上安排施工。",
        "家具"=>"选择设施，在地图上安排施工。",
        "楼梯"=>"连接上下平台；R 旋转朝向。",
        "生产"=>"建设设备；在底栏“生产”安排订单。",
        _=>"设置地基、层高或使用蓝图。"
    };
    private void BuildArchitect(Control root)
    {
        _buildMenu=Panel(root,Vector2.Zero,Vector2.Zero);
        LayoutCatalogPanel(_buildMenu);
        _buildMenu.AddThemeStyleboxOverride("panel",RStyle("171e24f5","817457",10,8));
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);_buildMenu.AddChild(column);
        var heading=new HBoxContainer();column.AddChild(heading);
        heading.AddChild(Text("建筑规划",17,Accent));
        _architectSearch=CatalogSearch(heading,"搜索建筑、材料或命令");
        _architectListButton=Button(heading,"列表",()=>SetArchitectList(!_architectList));
        UiAccess.Name(_architectListButton,"切换建筑列表与图标网格");
        _architectSort=new OptionButton{CustomMinimumSize=new(112,32)};heading.AddChild(_architectSort);
        foreach(string label in new[]{"默认排序","按名称","耐久 ↓","耐久 ↑","耗料 ↑","耗料 ↓"})_architectSort.AddItem(label);
        UiAccess.Name(_architectSort,"建筑排序方式","按名称、方块耐久或总材料数量排序；全部分类按属性排序时只显示有该属性的建筑。");
        _architectSort.ItemSelected+=i=>{_architectSortMode=(int)i;FilterArchitect();};
        _architectAutoClose=new CheckBox{Text="选择后关闭",ButtonPressed=true};heading.AddChild(_architectAutoClose);UiAccess.Name(_architectAutoClose,"选择工具后关闭建筑菜单");
        UiAccess.Name(Button(heading,"×",()=>_buildMenu.Hide()),"关闭建筑规划");
        var body=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",10);column.AddChild(body);
        var categories=new VBoxContainer{CustomMinimumSize=new(112,0)};_architectCategories=categories;body.AddChild(categories);
        _categoryButtons["全部"]=Button(categories,"全部",()=>{SelectTool("select");ShowBuildPage("全部");});
        _architectTools=new PanelContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_architectTools.AddThemeStyleboxOverride("panel",new StyleBoxEmpty());body.AddChild(_architectTools);
        _architectScroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};_architectTools.AddChild(_architectScroll);
        var pages=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};pages.AddThemeConstantOverride("separation",8);_architectScroll.AddChild(pages);
        _architectSorted=new GridContainer{Columns=3,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};pages.AddChild(_architectSorted);_architectSorted.Hide();
        var detail=CatalogDetail(body,out _architectDetailIcon,out _architectDetailTitle,out var detailActions);
        _architectDescription=Text("选择建筑或命令查看用途与材料。",14,Ink,true);detail.AddChild(_architectDescription);
        _architectHint=Text("",12,Muted,true);detailActions.AddChild(_architectHint);
        _architectEmpty=Text("没有匹配项，试试材料名或其他关键词。",14,Muted,true);pages.AddChild(_architectEmpty);_architectEmpty.Hide();
        foreach(var name in new[]{"命令","区域","结构","家具","楼梯","生产","规划"})
        {
            var page=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};pages.AddChild(page);_buildPages[name]=page;
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",2);categories.AddChild(row);_categoryRows[name]=row;
            var button=Button(row,name,()=>{SelectTool("select");ShowBuildPage(name);});button.CustomMinimumSize=new(72,36);button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;_categoryButtons[name]=button;
            var pin=Button(row,"☆",()=>PinArchitectCategory(name));pin.CustomMinimumSize=new(26,36);UiAccess.Name(pin,"置顶"+name+"分类");pin.TooltipText="将此分类固定到顶部；再次点击取消置顶。";_categoryPins[name]=pin;
        }
        _architectSearch.TextChanged+=_=>FilterArchitect();
        _structureTab=_categoryButtons["结构"];_stairsTab=_categoryButtons["楼梯"];
        Button Tool(Node parent,string id,string name,string icon,string help)
        {
            var button=IconButton(parent,icon,name,()=>{SelectTool(id);ShowArchitectDetail(name,icon,help);buttonFocusRelease();if(_architectAutoClose.ButtonPressed)_buildMenu.Hide();},true);_toolButtons[id]=button;
            void buttonFocusRelease()=>GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;button.CustomMinimumSize=new(104,92);button.AddThemeConstantOverride("icon_max_width",44);
            button.AutowrapMode=TextServer.AutowrapMode.WordSmart;
            string category=_buildPages.First(p=>p.Value==parent.GetParent()).Key;
            _architectEntries.Add((button,category,name,help,icon));
            button.MouseEntered+=()=>ShowArchitectDetail(name,icon,help);button.FocusEntered+=()=>ShowArchitectDetail(name,icon,help);
            button.TooltipText=help;button.AccessibilityDescription=help;return button;
        }
        var commands=new GridContainer{Columns=3,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_buildPages["命令"].AddChild(commands);
        Tool(commands,"select","选择","select","点击人物、建筑或区域查看详情；拖框多选居民，Shift 追加。");
        Tool(commands,"cancelplan","取消规划","cancel","撤销地格上未完成的任务，释放材料；保留已有建筑。");
        Tool(commands,"erase","拆除","erase","拆除已有建筑；居民执行，退回材料。也可取消施工规划。");
        Tool(commands,"dig","开采","dig","两次左键确定长方体，+/− 调整层高；右键或 Esc 取消框选。");
        Tool(commands,"canceldig","取消开采","cancel","两次左键跨层框选，取消未完成挖掘；不填回地形，不影响其他任务。相交的旧多米挖掘任务整项取消。");
        Tool(commands,"chop","伐木","chop","左键拖框批量标记树木，也可点击单棵；松开确认，右键或 Esc 取消。产出木料和树苗。");
        var zones=new GridContainer{Columns=3,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_buildPages["区域"].AddChild(zones);
        Tool(zones,"stockpile","储存区","stockpile","左键拖框或点击同高两角划定；点击储存区设置物品筛选与优先级。");
        Tool(zones,"growingzone","种植区","growingzone","左键拖框或点击同高两角划定，最多 16×16。点击区域选择作物、树木与播种设置。");
        Tool(zones,"unstockpile","移除储存区","unstockpile","拖框或点击两角移除储存范围；物品留在地上。");
        Tool(zones,"unfarm","移除种植区","unfarm","拖框或点击两角选择范围，确认后移除。未收获作物会损失，树木保留。");
        var rows=new Dictionary<string,GridContainer>();
        foreach(var name in new[]{"结构","家具","楼梯","生产"}){var row=new GridContainer{Columns=3,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_buildPages[name].AddChild(row);rows[name]=row;}
        foreach(var kind in new[]{BuildKind.Table,BuildKind.Chair,BuildKind.GameTable,BuildKind.Ladder,BuildKind.Wall,BuildKind.Door,BuildKind.Floodgate,BuildKind.Log,BuildKind.Planks,BuildKind.Cobblestone,BuildKind.StoneBricks,BuildKind.LogStairs,BuildKind.PlankStairs,BuildKind.CobbleStairs,BuildKind.BrickStairs,BuildKind.Furnace,BuildKind.Anvil,BuildKind.Stove,BuildKind.Bed,BuildKind.Campfire,BuildKind.Mill,BuildKind.Loom,BuildKind.Workbench})
        {
            string id=kind.ToString(),category=(BlockCatalog.IsStairs(kind)||kind==BuildKind.Ladder)?"楼梯":kind is BuildKind.Table or BuildKind.Chair or BuildKind.GameTable or BuildKind.Bed or BuildKind.Campfire?"家具":kind is BuildKind.Furnace or BuildKind.Anvil or BuildKind.Stove or BuildKind.Mill or BuildKind.Loom or BuildKind.Workbench?"生产":"结构";
            Tool(rows[category],id,DemoWorld.NameOf(kind),id,DemoWorld.NameOf(kind)+" · "+DemoWorld.CostOf(kind)+"\n点击规划，居民搬料施工。");
        }
        IconButton(_buildPages["楼梯"],"rotate","旋转 [R]",RotateStairs).TooltipText="旋转楼梯或梯子朝向";
        var modes=new HBoxContainer();_buildPages["结构"].AddChild(modes);
        _placementButtons[0]=IconButton(modes,"wall","当前层",()=>SetPlacement(0));
        _placementButtons[-1]=IconButton(modes,"floor","楼板",()=>SetPlacement(-1));
        _placementButtons[1]=IconButton(modes,"ceiling","天花板",()=>SetPlacement(1));
        var planning=_buildPages["规划"];var foundation=new VBoxContainer();planning.AddChild(foundation);
        IconButton(foundation,"foundation","划定地基",()=>{_foundationStart=null;SelectTool("foundation");if(_architectAutoClose.ButtonPressed)_buildMenu.Hide();});
        foundation.AddChild(Text("层高 / 米",12,Muted));
        _floorHeight=new SpinBox{MinValue=3,MaxValue=12,Value=3,CustomMinimumSize=new(85,30)};foundation.AddChild(_floorHeight);
        UiAccess.Name(_floorHeight,"层高（米）");
        _floorHeight.ValueChanged+=value=>{_foundation?.SetFloorHeight((int)value);if(_foundation is not null){_focus.Y=_foundation.WorldLevel;PositionCamera();DrawFoundation();_revision=-1;}};
        _floorLabel=Text("",12,Muted,true);planning.AddChild(_floorLabel);
        var blueprints=new VBoxContainer();planning.AddChild(blueprints);
        IconButton(blueprints,"blueprint","保存蓝图",()=>{_world.SaveBlueprint();RefreshUi();});
        IconButton(blueprints,"blueprint","放置蓝图",()=>{if(_world.Blueprint.Count>0){SelectTool("blueprint");if(_architectAutoClose.ButtonPressed)_buildMenu.Hide();}else _world.Log("先建好工坊，再保存蓝图。");});
        ShowBuildPage("命令");
        root.Resized+=()=>LayoutCatalogPanel(_buildMenu);
    }
}
