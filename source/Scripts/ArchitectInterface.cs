using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private PanelContainer _architectTools=null!;
    private Label _architectDescription=null!;
    private static string ArchitectHelp(string category)=>category switch
    {
        "命令"=>"给地图上的对象下达任务。\n\n开采支持拖选矩形；伐木点击树根。\n\n取消规划只撤销任务；拆除会拆掉已有建筑。",
        "区域"=>"先划范围，再点区域设置。\n\n种植区：选择作物、允许播种。\n储存区：筛选物品、设置优先级。\n\n点击同高的两个角确认范围。",
        "结构"=>"建造墙、门与方块。\n\n下方可切换当前层、楼板或天花板。居民会搬料施工。",
        "家具"=>"安排居民休息与取暖设施。\n\n点击地图放置，右键退出工具。",
        "楼梯"=>"连接不同高度的平台。\n\n先开采出所需空间，再放楼梯；R 旋转朝向。",
        "生产"=>"建造炉灶、熔炉与铁砧。\n\n生产订单从底栏“生产”安排。",
        _=>"设定地基与层高，保存或放置建筑蓝图。\n\n示例会直接添加施工规划。"
    };
    private void BuildArchitect(Control root)
    {
        _buildMenu=Panel(root,Vector2.Zero,new Vector2(200,350));
        BottomLeft(_buildMenu,426,200);_buildMenu.OffsetLeft=0;_buildMenu.OffsetRight=200;_buildMenu.OffsetBottom=-76;
        _buildMenu.AddThemeStyleboxOverride("panel",RStyle("171e24f5","586570",8,8));
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",8);_buildMenu.AddChild(column);
        var heading=new HBoxContainer();column.AddChild(heading);
        var title=Text("建筑规划",16,Accent);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;heading.AddChild(title);
        var close=Button(heading,"×",()=>_buildMenu.Hide());close.CustomMinimumSize=new(26,28);
        _architectDescription=Text("",13,Ink,true);_architectDescription.CustomMinimumSize=new(178,0);_architectDescription.SizeFlagsVertical=Control.SizeFlags.ExpandFill;column.AddChild(_architectDescription);
        var categories=new GridContainer{Columns=2};categories.AddThemeConstantOverride("h_separation",2);categories.AddThemeConstantOverride("v_separation",2);column.AddChild(categories);
        _architectTools=Panel(root,Vector2.Zero,Vector2.Zero);
        _architectTools.AnchorTop=_architectTools.AnchorBottom=_architectTools.AnchorRight=1;
        _architectTools.OffsetLeft=206;_architectTools.OffsetRight=-314;_architectTools.OffsetTop=-226;_architectTools.OffsetBottom=-76;
        _architectTools.AddThemeStyleboxOverride("panel",RStyle("171e24ee","586570",8,8));
        var scroll=new ScrollContainer{VerticalScrollMode=ScrollContainer.ScrollMode.Disabled,HorizontalScrollMode=ScrollContainer.ScrollMode.Auto};_architectTools.AddChild(scroll);
        var pages=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(pages);
        _buildMenu.VisibilityChanged+=()=>{_architectTools.Visible=_buildMenu.Visible;};
        foreach(var name in new[]{"命令","区域","结构","家具","楼梯","生产","规划"})
        {
            var page=new VBoxContainer();pages.AddChild(page);_buildPages[name]=page;
            var button=Button(categories,name,()=>{SelectTool("select");ShowBuildPage(name);});button.CustomMinimumSize=new(88,34);_categoryButtons[name]=button;
        }
        Button(categories,"收起",()=>_buildMenu.Hide()).CustomMinimumSize=new(88,34);
        _structureTab=_categoryButtons["结构"];_stairsTab=_categoryButtons["楼梯"];
        Button Tool(Node parent,string id,string name,string icon,string help)
        {
            var button=IconButton(parent,icon,name,()=>SelectTool(id),true);_toolButtons[id]=button;
            button.CustomMinimumSize=new(94,88);button.AddThemeConstantOverride("icon_max_width",48);
            button.TooltipText=help;button.MouseEntered+=()=>_architectDescription.Text=name+"\n\n"+help;
            button.MouseExited+=()=>_architectDescription.Text=ArchitectHelp(_buildCategory);return button;
        }
        var commands=new HBoxContainer();_buildPages["命令"].AddChild(commands);
        Tool(commands,"select","选择","select","退出当前工具。点击人物、建筑或区域查看详情。");
        Tool(commands,"cancelplan","取消规划","cancel","撤销地格上未完成的任务，释放材料；保留已有建筑。");
        Tool(commands,"erase","拆除","erase","拆除已有建筑；居民执行，退回材料。也可取消施工规划。");
        Tool(commands,"dig","开采","dig","点击或拖选矩形，开采一层岩土。");
        Tool(commands,"chop","伐木","chop","点击树根安排伐木，产出木料和树苗。");
        var zones=new HBoxContainer();_buildPages["区域"].AddChild(zones);
        Tool(zones,"stockpile","储存区","stockpile","点击同高两角划定；点已划好的储存区设置物品筛选与优先级。");
        Tool(zones,"growingzone","种植区","growingzone","点击同高两角划定，最多 16×16。点区域选择谷物、洞穴蘑菇或树木，并设置是否播种。");
        Tool(zones,"unstockpile","移除储存区","unstockpile","点击两个角移除储存范围；物品留在地上。");
        Tool(zones,"unfarm","移除种植区","unfarm","点击两个角移除种植范围。未收获的农作物会损失，树木保留。");
        _buildPages["区域"].AddChild(Text("划定范围后，右键退出工具 → 点击区域设置",12,Muted));
        var rows=new Dictionary<string,HBoxContainer>();
        foreach(var name in new[]{"结构","家具","楼梯","生产"}){var row=new HBoxContainer();_buildPages[name].AddChild(row);rows[name]=row;}
        foreach(var kind in new[]{BuildKind.Wall,BuildKind.Door,BuildKind.Floodgate,BuildKind.Log,BuildKind.Planks,BuildKind.Cobblestone,BuildKind.StoneBricks,BuildKind.LogStairs,BuildKind.PlankStairs,BuildKind.CobbleStairs,BuildKind.BrickStairs,BuildKind.Furnace,BuildKind.Anvil,BuildKind.Stove,BuildKind.Bed,BuildKind.Campfire,BuildKind.Mill,BuildKind.Loom})
        {
            string id=kind.ToString(),category=BlockCatalog.IsStairs(kind)?"楼梯":kind is BuildKind.Bed or BuildKind.Campfire?"家具":kind is BuildKind.Furnace or BuildKind.Anvil or BuildKind.Stove or BuildKind.Mill or BuildKind.Loom?"生产":"结构";
            Tool(rows[category],id,DemoWorld.NameOf(kind),id,DemoWorld.NameOf(kind)+" · "+DemoWorld.CostOf(kind)+"\n点击规划，居民搬料施工。");
        }
        IconButton(rows["楼梯"],"rotate","旋转 [R]",RotateStairs,true);
        var modes=new HBoxContainer();_buildPages["结构"].AddChild(modes);
        _placementButtons[0]=IconButton(modes,"wall","当前层",()=>SetPlacement(0));
        _placementButtons[-1]=IconButton(modes,"floor","楼板",()=>SetPlacement(-1));
        _placementButtons[1]=IconButton(modes,"ceiling","天花板",()=>SetPlacement(1));
        var planning=_buildPages["规划"];var foundation=new HBoxContainer();planning.AddChild(foundation);
        IconButton(foundation,"foundation","划定地基",()=>{_foundationStart=null;SelectTool("foundation");});
        foundation.AddChild(Text("层高 / 米",12,Muted));
        _floorHeight=new SpinBox{MinValue=3,MaxValue=12,Value=3,CustomMinimumSize=new(85,30)};foundation.AddChild(_floorHeight);
        _floorHeight.ValueChanged+=value=>{_foundation?.SetFloorHeight((int)value);if(_foundation is not null){_focus.Y=_foundation.WorldLevel;PositionCamera();DrawFoundation();_revision=-1;}};
        _floorLabel=Text("",12,Muted);planning.AddChild(_floorLabel);
        var blueprints=new HBoxContainer();planning.AddChild(blueprints);
        IconButton(blueprints,"blueprint","保存蓝图",()=>{_world.SaveBlueprint();RefreshUi();});
        IconButton(blueprints,"blueprint","放置蓝图",()=>{if(_world.Blueprint.Count>0)SelectTool("blueprint");else _world.Log("先建好工坊，再保存蓝图。");});
        IconButton(blueprints,"blueprint","单层示例",()=>{_world.QueueBlueprint(_world.Camp,DemoWorld.ExampleBlueprint());FocusCamp();RefreshUi();});
        IconButton(blueprints,"layers","两层示例",StartTwoStoreyDemo);IconButton(blueprints,"digstairs","地下示例",StartUndergroundDemo);
        ShowBuildPage("命令");
    }
}
