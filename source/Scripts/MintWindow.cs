using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    // Coordinate equivalents of MintArchitect.DoWindowContents / DrawGridHeader.
    private Control _mintArchitectDetail=null!;
    private Label _mintCategoryTitle=null!;
    private VBoxContainer _mintStats=null!;
    private readonly Dictionary<string,Control> _mintSectionHeaders=new();
    private static Texture2D MintTexture(string name)=>ImageTexture.CreateFromImage(Image.LoadFromFile(ProjectSettings.GlobalizePath("res://ThirdParty/DubsMintMenus/Textures/"+name+".png")));
    private Button MintButton(Node parent,string texture,string name,Action action,string fallback="")
    {
        var button=Button(parent,fallback,action);button.CustomMinimumSize=new(26,26);button.Flat=true;
        button.AddThemeConstantOverride("icon_max_width",22);
        button.AddThemeStyleboxOverride("normal",RStyle("20232900","20232900",2,2));
        button.AddThemeStyleboxOverride("hover",RStyle("454b52","60666c",2,2));
        button.AddThemeStyleboxOverride("pressed",RStyle("59616a","60666c",2,2));
        if(texture!=""){button.Icon=MintTexture(texture);button.ExpandIcon=true;}
        UiAccess.Name(button,name);button.TooltipText=name;return button;
    }
    private void MintDrag(Control handle,Control window)
    {
        bool moving=false;
        handle.GuiInput+=input=>
        {
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} b)moving=b.Pressed;
            if(input is InputEventMouseMotion motion&&moving)
            {
                var max=window.GetParent<Control>().Size-window.Size;
                window.Position=(window.Position+motion.Relative).Clamp(Vector2.Zero,new Vector2(Math.Max(0,max.X),Math.Max(0,max.Y-40)));
                handle.AcceptEvent();
            }
        };
        handle.MouseDefaultCursorShape=Control.CursorShape.Move;handle.TooltipText="拖动窗口";
    }
    private void ResizeMintGrid()
    {
        if(_architectSorted is null)return;
        int columns=_architectList?1:Math.Max(1,(int)((_architectScroll.Size.X-16)/90));
        _architectSorted.Columns=columns;
        foreach(var grid in _buildPages.Values.SelectMany(p=>p.GetChildren().OfType<GridContainer>()))
        {grid.Columns=columns;grid.AddThemeConstantOverride("h_separation",10);grid.AddThemeConstantOverride("v_separation",10);}
    }
    private void MintStatRows(string id)
    {
        foreach(var child in _mintStats.GetChildren()){_mintStats.RemoveChild(child);child.QueueFree();}
        if(!Enum.TryParse<BuildKind>(id,out var kind))return;
        var def=BlockCatalog.Get(kind);
        foreach(var (label,value) in new[]{("耐久",def.Durability.ToString()),("材料",DemoWorld.CostOf(kind)),("方块 ID",def.Id)})
        {
            var row=new HBoxContainer();_mintStats.AddChild(row);var key=Text(label,14,Muted);key.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(key);row.AddChild(Text(value,14,Ink));
        }
    }
}
