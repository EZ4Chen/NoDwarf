using Godot;
namespace NoDwarf;

// Shared window tokens and small composition helpers. World HUDs keep their own transparent styling.
public static class GameUi
{
    public const string Surface="1b2228",Inset="151b20",Raised="28333b",Hover="364650",Selected="455963",Border="48565e",Accent="c4ad78",Ink="e0e4e2",Muted="9eaaaF";
    public const int Space=8,Padding=16,ControlHeight=34;
    public static StyleBoxFlat Box(string color=Surface,int horizontal=12,int vertical=8)=>new()
    {
        BgColor=new(color),BorderColor=new(Border),BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,
        ContentMarginLeft=horizontal,ContentMarginRight=horizontal,ContentMarginTop=vertical,ContentMarginBottom=vertical
    };
    private static Texture2D? _checked,_unchecked;
    private static Texture2D CheckIcon(bool selected)
    {
        ref Texture2D? cached=ref (selected?ref _checked:ref _unchecked);
        if(cached is not null)return cached;
        using var image=new Image();
        string mark=selected?"<path d='M4 9l3 3 7-7' fill='none' stroke='#e0e4e2' stroke-width='2'/>":"";
        image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='18' height='18'><rect x='1.5' y='1.5' width='15' height='15' rx='1' fill='#151b20' stroke='#9eaaaf' stroke-width='1.5'/>"+mark+"</svg>");
        return cached=ImageTexture.CreateFromImage(image);
    }
    public static Theme CreateTheme()
    {
        var theme=new Theme{DefaultFont=GameFont.Regular,DefaultFontSize=15};
        foreach(string type in new[]{"Label","Button","OptionButton","LineEdit","CheckBox","TabContainer","TabBar"})theme.SetColor("font_color",type,new(Ink));
        foreach(string type in new[]{"Button","OptionButton"})
        {
            theme.SetStylebox("normal",type,Box(Raised));theme.SetStylebox("hover",type,Box(Hover));theme.SetStylebox("pressed",type,Box(Selected));
            theme.SetStylebox("disabled",type,Box(Surface));theme.SetColor("font_disabled_color",type,new(Muted));theme.SetStylebox("focus",type,UiAccess.FocusRing());
        }
        foreach(string state in new[]{"normal","hover","pressed","disabled","hover_pressed"})theme.SetStylebox(state,"CheckBox",new StyleBoxEmpty());
        theme.SetStylebox("focus","CheckBox",UiAccess.FocusRing());
        theme.SetIcon("checked","CheckBox",CheckIcon(true));theme.SetIcon("unchecked","CheckBox",CheckIcon(false));
        theme.SetStylebox("panel","PanelContainer",Box());theme.SetStylebox("panel","PopupMenu",Box());
        theme.SetStylebox("normal","LineEdit",Box(Inset));theme.SetStylebox("focus","LineEdit",UiAccess.FocusRing());
        theme.SetStylebox("panel","TabContainer",Box(Inset,12,12));
        foreach(string type in new[]{"TabContainer","TabBar"})
        {
            theme.SetStylebox("tab_selected",type,Box(Selected));theme.SetStylebox("tab_unselected",type,Box(Surface));theme.SetStylebox("tab_hovered",type,Box(Hover));
            theme.SetColor("font_selected_color",type,new(Ink));theme.SetColor("font_unselected_color",type,new(Muted));
        }
        foreach(string type in new[]{"HScrollBar","VScrollBar"})
        {theme.SetStylebox("scroll",type,Box(Inset,3,3));theme.SetStylebox("grabber",type,Box(Border,5,5));theme.SetStylebox("grabber_highlight",type,Box(Selected,5,5));theme.SetStylebox("grabber_pressed",type,Box(Hover,5,5));}
        theme.SetConstant("separation","VBoxContainer",Space);theme.SetConstant("separation","HBoxContainer",Space);
        return theme;
    }
    public static Label Label(Node parent,string text,bool muted=false,int size=15,bool wrap=false)
    {
        var label=new Label{Text=text,AutowrapMode=wrap?TextServer.AutowrapMode.WordSmart:TextServer.AutowrapMode.Off};
        label.AddThemeFontSizeOverride("font_size",size);label.AddThemeColorOverride("font_color",new(muted?Muted:Ink));parent.AddChild(label);return label;
    }
    public static Button Action(Node parent,string text,Action action,bool primary=false)
    {
        var button=new Button{Text=text,CustomMinimumSize=new(0,ControlHeight),MouseDefaultCursorShape=Control.CursorShape.PointingHand};
        parent.AddChild(button);UiAccess.Name(button,text);button.Pressed+=action;
        if(primary){button.AddThemeStyleboxOverride("normal",Box("625b46"));button.AddThemeStyleboxOverride("hover",Box("776d50"));}
        return button;
    }
    public static T Field<T>(Node parent,string label,T control) where T:Control
    {
        var row=new HBoxContainer();parent.AddChild(row);
        var caption=Label(row,label,wrap:true);caption.CustomMinimumSize=new(210,ControlHeight);caption.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
        control.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;control.CustomMinimumSize=new(180,ControlHeight);row.AddChild(control);UiAccess.Name(control,label);return control;
    }
    public static ScrollContainer Scroll(Node parent)
    {
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,FollowFocus=true,SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};parent.AddChild(scroll);return scroll;
    }
    public static VBoxContainer Column(Node parent)
    {var box=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};parent.AddChild(box);return box;}
}
