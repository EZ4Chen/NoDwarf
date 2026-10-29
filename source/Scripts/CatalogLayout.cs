using Godot;
using NoDwarf.MenuLogic;
namespace NoDwarf;
public partial class Main
{
    private static void LayoutCatalogPanel(Control panel)
    {
        var size=panel.GetParent<Control>().Size;
        float width=Math.Min(900,Math.Max(720,size.X-24));
        float height=Math.Min(470,Math.Max(320,size.Y-150));
        panel.AnchorLeft=panel.AnchorRight=panel.AnchorTop=panel.AnchorBottom=0;
        panel.Position=new(12,Math.Max(90,size.Y-height-48));panel.Size=new(width,height);
    }
    private LineEdit CatalogSearch(Node parent,string label)
    {
        var input=new LineEdit{PlaceholderText=label,ClearButtonEnabled=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(160,34)};
        input.AddThemeStyleboxOverride("normal",RStyle("20262a","66583d",8,4));
        parent.AddChild(input);UiAccess.Name(input,label,"输入关键词筛选；清除文字恢复分类。Esc 先退出输入，再按 Esc 关闭菜单。");
        input.GuiInput+=e=>{if(e is InputEventKey{Pressed:true,Keycode:Key.Escape}){input.ReleaseFocus();input.AcceptEvent();}};
        return input;
    }
    private VBoxContainer CatalogDetail(Node parent,out TextureRect icon,out Label title,out VBoxContainer actions)
    {
        var panel=new PanelContainer{CustomMinimumSize=new(240,0)};parent.AddChild(panel);panel.AddThemeStyleboxOverride("panel",RStyle("20262a","514b3c",10,10));
        var shell=new VBoxContainer();shell.AddThemeConstantOverride("separation",8);panel.AddChild(shell);
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=Control.SizeFlags.ExpandFill};shell.AddChild(scroll);
        var column=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};column.AddThemeConstantOverride("separation",10);scroll.AddChild(column);
        icon=new TextureRect{CustomMinimumSize=new(64,64),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};column.AddChild(icon);
        title=Text("详情",18,Accent,true);column.AddChild(title);actions=new VBoxContainer();shell.AddChild(actions);return column;
    }
    private LineEdit _architectSearch=null!;
    private Label _architectDetailTitle=null!,_architectHint=null!,_architectEmpty=null!;
    private TextureRect _architectDetailIcon=null!;
    private Button _architectListButton=null!;
    private CheckBox _architectAutoClose=null!;
    private ScrollContainer _architectScroll=null!;
    private bool _architectList;
    private readonly List<(Button Button,string Category,string Name,string Help,string Icon)> _architectEntries=new();
    private void ShowArchitectDetail(string name,string icon,string help)
    {_architectDetailTitle.Text=name;_architectDetailIcon.Show();_architectDetailIcon.Texture=UiIcons.Get(icon);_architectDescription.Text=help;}
    private void FilterArchitect()
    {
        if(_architectSearch is null)return;
        string query=_architectSearch.Text.Trim();bool searching=query.Length>0;int count=0;
        foreach(var entry in _architectEntries)
        {
            bool match=MintMenuLogic.Matches(ArchitectEntry(entry.Icon,entry.Name,entry.Category,entry.Help),query)&&(searching||_buildCategory=="全部"||entry.Category==_buildCategory);
            entry.Button.Visible=match;
            if(match&&(searching||_buildCategory=="全部"||entry.Category==_buildCategory))count++;
        }
        foreach(var (category,page) in _buildPages)
        {
            page.Visible=searching?_architectEntries.Any(e=>e.Category==category&&e.Button.Visible):_buildCategory=="全部"||category==_buildCategory;
            // Placement controls remain available in their own category, but do not pollute search results.
            foreach(var child in page.GetChildren().OfType<Control>().Where(c=>c is not GridContainer))child.Visible=!searching;
        }
        foreach(var (key,button) in _categoryButtons)button.AddThemeStyleboxOverride("normal",ButtonStyle(key==(searching?"全部":_buildCategory)?"66583d":"303942"));
        _architectEmpty.Visible=count==0&&(_buildCategory!="规划"||searching);
        SortArchitectResults();
        _architectScroll.ScrollVertical=0;
    }
    private void SetArchitectList(bool list)
    {
        _architectSorted.Columns=list?1:3;
        _architectList=list;_architectListButton.Text=list?"网格":"列表";
        foreach(var grid in _buildPages.Values.SelectMany(p=>p.GetChildren().OfType<GridContainer>()))grid.Columns=list?1:3;
        foreach(var e in _architectEntries)
        {
            e.Button.CustomMinimumSize=list?new(316,42):new(104,92);
            e.Button.IconAlignment=list?HorizontalAlignment.Left:HorizontalAlignment.Center;
            e.Button.VerticalIconAlignment=list?VerticalAlignment.Center:VerticalAlignment.Top;
            e.Button.Alignment=list?HorizontalAlignment.Left:HorizontalAlignment.Center;
            e.Button.AddThemeConstantOverride("icon_max_width",list?28:44);
        }
    }
}
