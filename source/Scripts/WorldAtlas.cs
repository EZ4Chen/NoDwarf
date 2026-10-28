using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class WorldAtlas : Control
{
    private WorldGenerationSettings _settings=new();
    private ImageTexture? _texture;
    public bool Interactive {get;set;}=true;
    public Action<int,int>? Selected;
    public override void _Ready(){FocusEntered+=QueueRedraw;FocusExited+=QueueRedraw;}
    public void SetWorld(WorldGenerationSettings settings)
    {
        _settings=settings;
        FocusMode=Interactive?FocusModeEnum.All:FocusModeEnum.None;
        AccessibilityName=Interactive?"世界地图选址":"世界预览";
        var site=settings.Site;
        AccessibilityDescription=Interactive?$"方向键选择地区，Tab 离开地图。当前地区 {settings.SiteX+1}, {settings.SiteY+1}，"+
            (site.Land?$"{RegionProfile.Get(site.Region).Name}，年均温度 {site.Temperature:F1} 度。":"海洋，不能建立营地。"):"世界参数预览。";
        var image=Image.CreateEmpty(WorldGenerationSettings.AtlasWidth,WorldGenerationSettings.AtlasHeight,false,Image.Format.Rgba8);
        for(int x=0;x<image.GetWidth();x++)for(int y=0;y<image.GetHeight();y++)
        {
            var tile=settings.Tile(x,y);
            var color=!tile.Land?new Color("294758"):tile.Region switch{"desert"=>new Color("ddc28a"),"arid"=>new Color("baa174"),"grassland"=>new Color("91ad5b"),"rainforest"=>new Color("356747"),"wetland"=>new Color("537c62"),"tundra"=>new Color("9ca88e"),"boreal"=>new Color("577e7b"),"savanna"=>new Color("b19b65"),"alpine"=>new Color("a3aaa6"),_=>new Color("628764")};
            float shade=(float)(.78+(tile.Height-60)/160d);image.SetPixel(x,y,new Color(color.R*shade,color.G*shade,color.B*shade));
        }
        _texture=ImageTexture.CreateFromImage(image);TextureFilter=TextureFilterEnum.Nearest;QueueRedraw();
    }
    private Rect2 MapRect
    {
        get{float width=Math.Min(Size.X,Size.Y*WorldGenerationSettings.AtlasWidth/WorldGenerationSettings.AtlasHeight);float height=width*WorldGenerationSettings.AtlasHeight/WorldGenerationSettings.AtlasWidth;return new((Size-new Vector2(width,height))*.5f,new(width,height));}
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("101d26"));if(_texture is null)return;
        var rect=MapRect;DrawTextureRect(_texture,rect,false);
        var unit=rect.Size/new Vector2(WorldGenerationSettings.AtlasWidth,WorldGenerationSettings.AtlasHeight);
        if(Interactive)
        {
            var cell=new Rect2(rect.Position+new Vector2(_settings.SiteX,_settings.SiteY)*unit,unit);
            var center=cell.GetCenter();DrawCircle(center,9,new Color("11222acc"));DrawCircle(center,4,new Color("e3cc91"));
            DrawArc(center,12,0,Mathf.Tau,32,new Color("e3cc91"),2);
        }
        DrawRect(rect,new Color("6b7b7b"),false,1);
        if(HasFocus())DrawRect(new Rect2(Vector2.One*2,Size-Vector2.One*4),new Color("edcf87"),false,2);
    }
    public override void _GuiInput(InputEvent e)
    {
        if(Interactive&&e is InputEventKey{Pressed:true} key)
        {
            var delta=key.Keycode switch{Key.Left=>new Vector2I(-1,0),Key.Right=>new Vector2I(1,0),Key.Up=>new Vector2I(0,-1),Key.Down=>new Vector2I(0,1),_=>Vector2I.Zero};
            if(delta!=Vector2I.Zero)
            {Selected?.Invoke(Math.Clamp(_settings.SiteX+delta.X,0,WorldGenerationSettings.AtlasWidth-1),Math.Clamp(_settings.SiteY+delta.Y,0,WorldGenerationSettings.AtlasHeight-1));AcceptEvent();return;}
        }
        if(!Interactive||e is not InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Left} click||!MapRect.HasPoint(click.Position))return;
        GrabFocus();
        var p=(click.Position-MapRect.Position)/MapRect.Size;Selected?.Invoke(Math.Clamp((int)(p.X*WorldGenerationSettings.AtlasWidth),0,WorldGenerationSettings.AtlasWidth-1),Math.Clamp((int)(p.Y*WorldGenerationSettings.AtlasHeight),0,WorldGenerationSettings.AtlasHeight-1));AcceptEvent();
    }
}
