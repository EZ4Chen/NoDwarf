using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public enum AtlasLayer { Biome, Elevation, Wetness, Temperature, Rainfall, Drainage, Water }
public partial class WorldAtlas : Control
{
    private WorldGenerationSettings _settings=new();
    private ImageTexture? _texture;
    private AtlasTile[,]? _tiles;
    private AtlasLayer _layer;
    private WorldGenerationSettings? _fieldKey;
    public bool Interactive {get;set;}=true;
    public Action<int,int>? Selected;
    internal int TextureBuildCount {get;private set;}
    internal Rect2 MapBounds=>MapRect;
    public AtlasLayer Layer
    {
        get=>_layer;
        set {if(_layer==value)return;_layer=value;BuildTexture();UpdateAccessibleDescription();QueueRedraw();}
    }
    public string Legend => Layer switch
    {
        AtlasLayer.Elevation=>"蓝：海洋 · 绿→棕→浅灰：海拔升高 · 细线：8 m 等高分区",
        AtlasLayer.Rainfall=>"棕→蓝：降水由少到多",
        AtlasLayer.Drainage=>"棕→青：排水由弱到强",
        AtlasLayer.Water=>"浅蓝线：河流及流向 · 蓝色：湖泊／海洋",
        AtlasLayer.Wetness=>"蓝：海洋 · 棕→青绿：由干到湿",
        AtlasLayer.Temperature=>"蓝：海洋 · 蓝紫→暖橙：年均温度由低到高",
        _=>"蓝：海洋 · 绿：植被区 · 黄：干旱区 · 灰：高山 · 亮线：海岸"
    };
    public override void _Ready(){FocusEntered+=QueueRedraw;FocusExited+=QueueRedraw;}
    private void UpdateAccessibleDescription()
    {
        var site=_settings.Site;
        FocusMode=Interactive?FocusModeEnum.All:FocusModeEnum.None;
        AccessibilityName=Interactive?"世界地图选址":"世界预览";
        AccessibilityDescription=(Interactive?$"方向键选择地区，Tab 离开地图。当前地区 {_settings.SiteX+1}, {_settings.SiteY+1}，"+
            (site.Land?$"{RegionProfile.Get(site.Region).Name}，年均温度 {site.Temperature:F1} 度。":"海洋，不能建立营地。"):"世界参数预览。")+Legend;
    }
    public void SetWorld(WorldGenerationSettings settings)
    {
        _settings=settings;UpdateAccessibleDescription();
        var key=settings with {SiteX=0,SiteY=0};
        if(_fieldKey!=key)
        {
            _fieldKey=key;_tiles=new AtlasTile[_settings.Width,_settings.Height];
            for(int x=0;x<_settings.Width;x++)for(int y=0;y<_settings.Height;y++)_tiles[x,y]=settings.Tile(x,y);
            BuildTexture();
        }
        QueueRedraw();
    }
    private void BuildTexture()
    {
        if(_tiles is null)return;
        using var image=Image.CreateEmpty(_settings.Width,_settings.Height,false,Image.Format.Rgba8);
        for(int x=0;x<image.GetWidth();x++)for(int y=0;y<image.GetHeight();y++)
        {
            var tile=_tiles[x,y];Color color;
            if(!tile.Land||tile.Lake)color=new("213f59");
            else if(Layer==AtlasLayer.Elevation)
            {
                float height=Math.Clamp((tile.Height-60)/48f,0,1);
                color=height<.5f?new Color("548557").Lerp(new("b4a373"),height*2):new Color("b4a373").Lerp(new("ece9dc"),(height-.5f)*2);
            }
            else if(Layer==AtlasLayer.Rainfall)color=new Color("c99d5c").Lerp(new("3c86cc"),(float)tile.Rainfall);
            else if(Layer==AtlasLayer.Drainage)color=new Color("b9956a").Lerp(new("46b59c"),(float)tile.Drainage);
            else if(Layer==AtlasLayer.Water)color=new Color("394c3b");
            else if(Layer==AtlasLayer.Wetness)color=new Color("c99d5c").Lerp(new("3caaa3"),(float)tile.Wetness);
            else if(Layer==AtlasLayer.Temperature)color=new Color("747ac7").Lerp(new("f1a45c"),Math.Clamp((float)(tile.Temperature+25)/65,0,1));
            else
            {
                color=tile.Region switch{"desert"=>new("ddc28a"),"arid"=>new("baa174"),"grassland"=>new("91ad5b"),"rainforest"=>new("356747"),"wetland"=>new("537c62"),"tundra"=>new("9ca88e"),"boreal"=>new("577e7b"),"savanna"=>new("b19b65"),"alpine"=>new("c5c8bf"),_=>new("628764")};
                float shade=(float)(.78+(tile.Height-60)/160d);color=new(color.R*shade,color.G*shade,color.B*shade);
            }
            image.SetPixel(x,y,color);
        }
        if(_texture is null||_texture.GetWidth()!=image.GetWidth()||_texture.GetHeight()!=image.GetHeight())
        {_texture?.Dispose();_texture=ImageTexture.CreateFromImage(image);}else _texture.Update(image);
        TextureFilter=TextureFilterEnum.Nearest;TextureBuildCount++;
    }
    private Rect2 MapRect
    {
        get{float width=Math.Min(Size.X,Size.Y*_settings.Width/_settings.Height);float height=width*_settings.Height/_settings.Width;return new((Size-new Vector2(width,height))*.5f,new(width,height));}
    }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("101d26"));if(_texture is null||_tiles is null)return;
        var rect=MapRect;DrawTextureRect(_texture,rect,false);
        var unit=rect.Size/new Vector2(_settings.Width,_settings.Height);
        // Draw water links directly from the generated drainage graph.
        for(int x=0;x<_settings.Width;x++)for(int y=0;y<_settings.Height;y++)
        {
            var tile=_tiles[x,y];var p=rect.Position+new Vector2(x,y)*unit;
            void Edge(AtlasTile other,Vector2 a,Vector2 b)
            {
                if(tile.Land!=other.Land)DrawLine(a,b,new Color("c9d9bc"),1.5f);
                else if(Layer is AtlasLayer.Biome or AtlasLayer.Elevation && tile.Land && tile.Height/8!=other.Height/8)
                    DrawLine(a,b,new Color("28302370"),1);
            }
            if(x+1<_settings.Width)Edge(_tiles[x+1,y],p+new Vector2(unit.X,0),p+unit);
            if(y+1<_settings.Height)Edge(_tiles[x,y+1],p+new Vector2(0,unit.Y),p+unit);
        }
        for(int x=0;x<_settings.Width;x++)for(int y=0;y<_settings.Height;y++)
        {
            var tile=_tiles[x,y];if(tile.RiverWidth==0)continue;
            var from=rect.Position+(new Vector2(x,y)+Vector2.One*.5f)*unit;
            var to=tile.DownstreamX>=0?rect.Position+(new Vector2(tile.DownstreamX,tile.DownstreamY)+Vector2.One*.5f)*unit:from;
            DrawLine(from,to,new Color("74c8ed"),Layer==AtlasLayer.Water?2:1.2f);
            if(Layer==AtlasLayer.Water&&unit.X>=5&&to!=from)
            {var direction=(to-from).Normalized();var end=from+(to-from)*.65f;DrawLine(end,end-direction.Rotated(.6f)*3,new Color("d5efff"));DrawLine(end,end-direction.Rotated(-.6f)*3,new Color("d5efff"));}
        }
        if(Interactive)
        {
            var cell=new Rect2(rect.Position+new Vector2(_settings.SiteX,_settings.SiteY)*unit,unit);
            DrawRect(cell.Grow(1),new Color("101820"),false,4);DrawRect(cell,new Color("fff4bf"),false,2);
            var center=cell.GetCenter();DrawLine(center-new Vector2(13,0),center-new Vector2(6,0),Colors.White,1);
            DrawLine(center+new Vector2(6,0),center+new Vector2(13,0),Colors.White,1);
            DrawLine(center-new Vector2(0,13),center-new Vector2(0,6),Colors.White,1);
            DrawLine(center+new Vector2(0,6),center+new Vector2(0,13),Colors.White,1);
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
            {Selected?.Invoke(Math.Clamp(_settings.SiteX+delta.X,0,_settings.Width-1),Math.Clamp(_settings.SiteY+delta.Y,0,_settings.Height-1));AcceptEvent();return;}
        }
        if(!Interactive||e is not InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Left} click||!MapRect.HasPoint(click.Position))return;
        GrabFocus();
        var p=(click.Position-MapRect.Position)/MapRect.Size;Selected?.Invoke(Math.Clamp((int)(p.X*_settings.Width),0,_settings.Width-1),Math.Clamp((int)(p.Y*_settings.Height),0,_settings.Height-1));AcceptEvent();
    }
}
