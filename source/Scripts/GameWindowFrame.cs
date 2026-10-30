using Godot;
namespace NoDwarf;

// Same chrome inside a modal Window or a docked map panel; the caller owns its lifecycle.
public partial class GameWindowFrame:PanelContainer
{
    public Label Heading {get;}
    public Button CloseButton {get;}
    public VBoxContainer Body {get;}
    public HBoxContainer Footer {get;}
    public Label Status {get;}
    public GameWindowFrame():this("窗口",()=>{}){}
    public GameWindowFrame(string title,Action close)
    {
        Theme=GameUi.CreateTheme();MouseFilter=MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel",GameUi.Box(GameUi.Surface,GameUi.Padding,GameUi.Padding));
        var column=GameUi.Column(this);column.AddThemeConstantOverride("separation",12);
        var header=new HBoxContainer();column.AddChild(header);
        Heading=GameUi.Label(header,title,size:20);Heading.SizeFlagsHorizontal=SizeFlags.ExpandFill;Heading.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        CloseButton=GameUi.Action(header,"×",close);CloseButton.CustomMinimumSize=new(34,34);CloseButton.TooltipText="关闭（Esc）";UiAccess.Name(CloseButton,"关闭"+title);
        column.AddChild(new HSeparator());
        Body=GameUi.Column(column);Body.SizeFlagsVertical=SizeFlags.ExpandFill;
        Status=GameUi.Label(column,"",muted:true,wrap:true);Status.Visible=false;Status.AccessibilityLive=AccessibilityServer.AccessibilityLiveMode.Polite;
        Footer=new HBoxContainer{Alignment=BoxContainer.AlignmentMode.End,Visible=false};column.AddChild(Footer);
    }
    public void SetStatus(string text){Status.Text=text;Status.Visible=!string.IsNullOrWhiteSpace(text);}
    public Button AddAction(string label,Action action,bool primary=false){Footer.Show();return GameUi.Action(Footer,label,action,primary);}
    public void EnableWindowDrag(Window window)
    {
        bool moving=false;Heading.MouseFilter=MouseFilterEnum.Stop;Heading.MouseDefaultCursorShape=CursorShape.Move;Heading.TooltipText="拖动窗口";
        Heading.GuiInput+=input=>
        {
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} button)moving=button.Pressed;
            if(input is InputEventMouseMotion motion&&moving)
            {
                if(!Input.IsMouseButtonPressed(MouseButton.Left)){moving=false;return;}
                var bounds=window.IsEmbedded()?new Rect2I(Vector2I.Zero,window.GetTree().Root.Size):DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
                var maximum=bounds.End-window.Size;maximum=new(Math.Max(bounds.Position.X,maximum.X),Math.Max(bounds.Position.Y,maximum.Y));
                window.Position=(window.Position+(Vector2I)motion.Relative).Clamp(bounds.Position,maximum);Heading.AcceptEvent();
            }
        };
    }
    public void FitCentered(Control host,Vector2 preferred)
    {
        void Fit(){Size=preferred.Min(new Vector2(Math.Max(0,host.Size.X-32),Math.Max(0,host.Size.Y-56)));Position=(host.Size-Size)/2;}
        host.Resized+=Fit;TreeExiting+=()=>host.Resized-=Fit;Fit();
    }
}
