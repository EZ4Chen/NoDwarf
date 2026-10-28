using Godot;
using NoDwarf.Simulation;
using System.Text.Json;
namespace NoDwarf;

// Replaceable item art; generated textures are rebuilt only when equipment/appearance changes.
public static class EquipmentArt
{
    private static readonly Dictionary<string,Texture2D> Icons=new();
    private static readonly Dictionary<string,ImageTexture> Portraits=new();
    private static Dictionary<string,Art>? _definitions;
    public sealed class Art
    {
        public string Icon {get;set;}="";
        public string Tint {get;set;}="";
        public int[]? Region {get;set;}
        public string Clothing {get;set;}="";
        public string Head {get;set;}="";
        public int[]? HeldRect {get;set;}
    }
    private static Dictionary<string,Art> Definitions=>_definitions??=JsonSerializer.Deserialize<Dictionary<string,Art>>(Godot.FileAccess.GetFileAsString("res://Assets/Textures/equipment.json"))!;
    public static Texture2D? Icon(string item)
    {
        if(Icons.TryGetValue(item,out var icon))return icon;
        if(!Definitions.TryGetValue(item,out var art)||art.Icon=="")return null;
        var source=GD.Load<Texture2D>(art.Icon);
        icon=art.Region is {Length:4} r?new AtlasTexture{Atlas=source,Region=new Rect2(r[0],r[1],r[2],r[3])}:source;
        if(art.Tint!="")
        {
            var pixels=icon.GetImage();if(pixels.IsCompressed())pixels.Decompress();pixels.Convert(Image.Format.Rgba8);
            var tint=new Color(art.Tint);
            for(int y=0;y<pixels.GetHeight();y++)for(int x=0;x<pixels.GetWidth();x++)pixels.SetPixel(x,y,pixels.GetPixel(x,y)*tint);
            icon=ImageTexture.CreateFromImage(pixels);
        }
        Icons[item]=icon;return icon;
    }
    public static string Key(CharacterAppearance appearance,IEnumerable<ItemStack> items)=>JsonSerializer.Serialize(appearance)+"|"+string.Join(";",items.Where(i=>i.Slot!="").OrderBy(i=>i.Slot).Select(i=>i.Slot+":"+i.Item));
    public static ImageTexture Texture(CharacterAppearance appearance,IEnumerable<ItemStack> items)
    {
        var worn=items.Where(i=>i.Slot!="").OrderBy(i=>Array.IndexOf(new[]{"legs","body","feet","hands","head","main","off"},i.Slot)).ToArray();string key=Key(appearance,worn);
        if(Portraits.TryGetValue(key,out var cached))return cached;
        string clothing="",head="";
        foreach(var item in worn)if(Definitions.TryGetValue(item.Item,out var art)){clothing+=art.Clothing;head+=art.Head;}
        var body=new Image();if(body.LoadSvgFromString(CharacterArt.Svg(appearance,clothing,head),3)!=Error.Ok)throw new InvalidOperationException("Equipment SVG");
        var image=Image.CreateEmpty(336,312,false,Image.Format.Rgba8);body.Convert(Image.Format.Rgba8);image.BlendRect(body,new Rect2I(0,0,240,312),new Vector2I(48,0));
        foreach(var item in worn.Where(i=>i.Slot is "main" or "off"))
        {
            if(Icon(item.Item) is not {} texture)continue;
            var held=texture.GetImage();held.Convert(Image.Format.Rgba8);
            var rect=Definitions[item.Item].HeldRect??(item.Slot=="off"?new[]{6,50,24,40}:new[]{74,41,36,36});
            held.Resize(rect[2]*3,rect[3]*3,Image.Interpolation.Lanczos);
            image.BlendRect(held,new Rect2I(0,0,held.GetWidth(),held.GetHeight()),new Vector2I(rect[0]*3,rect[1]*3));
        }
        var result=ImageTexture.CreateFromImage(image);
        if(Portraits.Count>=256)Portraits.Clear();Portraits[key]=result;return result;
    }
}
public partial class Main
{
    private DemoWorld? _equipmentWorld;
    private int _equipmentRevision=-1;
    private readonly Dictionary<string,(CharacterAppearance Appearance,string Gear)> _equipmentKeys=new();
    private void SyncEquipmentVisuals(bool force=false)
    {
        if(!force&&_equipmentWorld==_world&&_equipmentRevision==_world.Revision)return;
        if(_equipmentWorld!=_world){_equipmentKeys.Clear();_equipmentWorld=_world;}
        _equipmentRevision=_world.Revision;
        var owned=_world.Items.Where(i=>i.Owner!=""&&i.Slot!="").ToLookup(i=>i.Owner);
        for(int i=0;i<_people.Count;i++)
        {
            var w=_world.Workers[i];var key=(w.Appearance,string.Join(";",owned[w.Id].OrderBy(s=>s.Slot).Select(s=>s.Slot+":"+s.Item)));
            if(!force&&_equipmentKeys.GetValueOrDefault(w.Id)==key)continue;
            _equipmentKeys[w.Id]=key;var texture=EquipmentArt.Texture(w.Appearance,owned[w.Id]);
            _people[i].GetChild<Sprite3D>(0).Texture=texture;
            _people[i].GetNodeOrNull<ActorModelView>("ModelView")?.UpdateEquipment(owned[w.Id],w.Appearance.Beard);
            if(i<_residentButtons.Count)_residentButtons[i].Icon=texture;
            if(_dossierId==w.Id&&_residentPortrait is not null)_residentPortrait.Texture=texture;
        }
        foreach(var w in _world.Enemies)
        {
            if(!_enemyVisuals.TryGetValue(w.Id,out var node))continue;
            var key=(w.Appearance,string.Join(";",owned[w.Id].OrderBy(s=>s.Slot).Select(s=>s.Slot+":"+s.Item)));
            if(!force&&_equipmentKeys.GetValueOrDefault(w.Id)==key)continue;
            _equipmentKeys[w.Id]=key;node.GetChild<Sprite3D>(0).Texture=EquipmentArt.Texture(w.Appearance,owned[w.Id]);
            node.GetNodeOrNull<ActorModelView>("ModelView")?.UpdateEquipment(owned[w.Id],w.Appearance.Beard);
        }
    }
}
