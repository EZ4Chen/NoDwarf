using Godot;
using System.Text.Json;
using NoDwarf.Simulation;
namespace NoDwarf;

// Material IDs stay independent of this temporary art pack. Missing textures use registry colours.
public static class BlockTextures
{
    private static readonly Dictionary<string, StandardMaterial3D> Cache = new();
    public sealed class Entry
    {
        public bool Cutout {get;set;}
        public string? Alias {get;set;} public string? Side {get;set;} public string? Top {get;set;}
        public string? Bottom {get;set;} public string? Front {get;set;} public string? Model {get;set;}
        public string Tint {get;set;}="ffffff"; public string? TopTint {get;set;}
    }
    public sealed class Manifest
    {
        public string Root {get;set;}="res://Assets/Textures/PureBDcraft/";
        public Dictionary<string,Entry> Blocks {get;set;}=new();
    }
    private static readonly Manifest Pack=JsonSerializer.Deserialize<Manifest>(Godot.FileAccess.GetFileAsString("res://Assets/Textures/materials.json"),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new();
    private static Entry Resolve(string id)
    {
        for(int i=0;i<8;i++)
        {
            if(!Pack.Blocks.TryGetValue(id,out var entry)) return new Entry{Side=id.Replace("nodwarf:","")+".png"};
            if(entry.Alias is null)return entry; id=entry.Alias;
        }
        throw new InvalidOperationException("Material alias cycle: "+id);
    }
    public static StandardMaterial3D Material(string id,bool top=false,bool bottom=false,bool front=false)
    {
        var e=Resolve(id);string file=(top?e.Top:bottom?e.Bottom:front?e.Front:null)??e.Side??id.Replace("nodwarf:","")+".png";
        string tint=top?(e.TopTint??e.Tint):e.Tint;string path=Pack.Root+file;string key=path+":"+tint+":"+e.Cutout;
        if(Cache.TryGetValue(key,out var cached))return cached;
        var mat=new StandardMaterial3D{Roughness=1,TextureFilter=BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        if(ResourceLoader.Exists(path)){mat.AlbedoTexture=GD.Load<Texture2D>(path);mat.AlbedoColor=new Color(tint);}
        else mat.AlbedoColor=new Color(BlockCatalog.Get(id).Color);
        if(e.Cutout){mat.Transparency=BaseMaterial3D.TransparencyEnum.AlphaScissor;mat.AlphaScissorThreshold=.4f;}
        Cache[key]=mat;return mat;
    }
    private static Image? _biomeGrassMap;
    public static Color BiomeGrassColor(RegionProfile profile)
    {
        if(!ResourceLoader.Exists(Pack.Root+"biome_grass_colormap.png"))return new Color(profile.Humidity<.35?"a6a65b":"78a34e");
        var map=_biomeGrassMap??=GD.Load<Texture2D>(Pack.Root+"biome_grass_colormap.png").GetImage();
        double heat=Math.Clamp((profile.Mean+10)/40,0,1),wet=profile.Humidity*heat;
        return map.GetPixel((int)((1-heat)*(map.GetWidth()-1)),(int)((1-wet)*(map.GetHeight()-1)));
    }
    public static StandardMaterial3D TerrainMaterial(string id,bool top,RegionProfile? profile)
    {
        if(profile?.Modern!=true||id is not ("nodwarf:grass" or "nodwarf:dry_grass" or "nodwarf:dry_grass_ramp"))return Material(id,top);
        var tint=BiomeGrassColor(profile);string key=$"biome:{id}:{top}:{tint.ToHtml()}";
        if(Cache.TryGetValue(key,out var found))return found;
        var basis=Material(id,top);
        if(basis.AlbedoTexture is null)return basis;
        var material=(StandardMaterial3D)basis.Duplicate();material.AlbedoColor=Colors.White;
        if(top)material.AlbedoColor=tint;
        else
        {
            // Tint the grass overlay only; the dirt part retains its original colour.
            if(!ResourceLoader.Exists(Pack.Root+"grass_block_side_overlay.png")){Cache[key]=material;return material;}
            var image=material.AlbedoTexture.GetImage();image.Convert(Image.Format.Rgba8);
            var overlay=GD.Load<Texture2D>(Pack.Root+"grass_block_side_overlay.png").GetImage();
            for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)
            {var a=overlay.GetPixel(x*overlay.GetWidth()/image.GetWidth(),y*overlay.GetHeight()/image.GetHeight());float alpha=a.A;a*=tint;a.A=1;image.SetPixel(x,y,image.GetPixel(x,y).Lerp(a,alpha));}
            image.ClearMipmaps();image.GenerateMipmaps();material.AlbedoTexture=ImageTexture.CreateFromImage(image);
        }
        Cache[key]=material;return material;
    }
    // An overlay conveys the wall volume without intercepting selection behind it.
    public static void Fade(Node parent,float alpha=.20f)
    {
        parent.SetMeta("view_overlay",true);
        foreach(var mesh in parent.GetChildren().OfType<MeshInstance3D>())
        {
            mesh.CastShadow=GeometryInstance3D.ShadowCastingSetting.Off;
            for(int i=0;i<mesh.Mesh.GetSurfaceCount();i++)
            {
                if(mesh.Mesh.SurfaceGetMaterial(i) is not StandardMaterial3D original)continue;
                var mat=(StandardMaterial3D)original.Duplicate();
                mat.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;
                mat.AlbedoColor=new Color(original.AlbedoColor,alpha);
                mat.CullMode=BaseMaterial3D.CullModeEnum.Back;
                mat.DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled;
                mesh.SetSurfaceOverrideMaterial(i,mat);
            }
        }
    }
    // Supports the cuboid elements, rotations and UVs in the supplied anvil model.
    // An alternative model/texture mapping can be supplied through materials.json.
    public static bool Model(Node parent,Vector3 position,string id)
    {
        var e=Resolve(id);if(e.Model is null||!Godot.FileAccess.FileExists(Pack.Root+e.Model))return false;
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsString(Pack.Root+e.Model));
        foreach(var element in document.RootElement.GetProperty("elements").EnumerateArray())
        {
            Vector3 Vec(JsonElement a)=>new(a[0].GetSingle()/16,a[1].GetSingle()/16,a[2].GetSingle()/16);
            var lo=Vec(element.GetProperty("from"));var hi=Vec(element.GetProperty("to"));
            Vector3 Transform(Vector3 p)
            {
                if(element.TryGetProperty("rotation",out var r))
                {var origin=Vec(r.GetProperty("origin"));var axis=r.GetProperty("axis").GetString() switch{"x"=>Vector3.Right,"y"=>Vector3.Up,_=>Vector3.Back};p=origin+(p-origin).Rotated(axis,Mathf.DegToRad(r.GetProperty("angle").GetSingle()));}
                return p-new Vector3(.5f,0,.5f);
            }
            var mesh=new ArrayMesh();
            foreach(var face in element.GetProperty("faces").EnumerateObject())
            {
                Vector3[] vertices=face.Name switch
                {
                    "up"=>new[]{new Vector3(lo.X,hi.Y,lo.Z),new(lo.X,hi.Y,hi.Z),new(hi.X,hi.Y,hi.Z),new(hi.X,hi.Y,lo.Z)},
                    "down"=>new[]{new Vector3(lo.X,lo.Y,hi.Z),new(lo.X,lo.Y,lo.Z),new(hi.X,lo.Y,lo.Z),new(hi.X,lo.Y,hi.Z)},
                    "north"=>new[]{new Vector3(lo.X,hi.Y,lo.Z),new(hi.X,hi.Y,lo.Z),new(hi.X,lo.Y,lo.Z),new(lo.X,lo.Y,lo.Z)},
                    "south"=>new[]{new Vector3(hi.X,hi.Y,hi.Z),new(lo.X,hi.Y,hi.Z),new(lo.X,lo.Y,hi.Z),new(hi.X,lo.Y,hi.Z)},
                    "east"=>new[]{new Vector3(hi.X,hi.Y,lo.Z),new(hi.X,hi.Y,hi.Z),new(hi.X,lo.Y,hi.Z),new(hi.X,lo.Y,lo.Z)},
                    _=>new[]{new Vector3(lo.X,hi.Y,hi.Z),new(lo.X,hi.Y,lo.Z),new(lo.X,lo.Y,lo.Z),new(lo.X,lo.Y,hi.Z)}
                };
                var v=face.Value;var uv=v.GetProperty("uv");float u0=uv[0].GetSingle()/16,v0=uv[1].GetSingle()/16,u1=uv[2].GetSingle()/16,v1=uv[3].GetSingle()/16;
                var coords=new[]{new Vector2(u0,v0),new(u1,v0),new(u1,v1),new(u0,v1)};
                int rotate=v.TryGetProperty("rotation",out var rotation)?rotation.GetInt32()/90:0;
                var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(Material(id,v.GetProperty("texture").GetString()=="#top"));
                foreach(int i in new[]{0,2,1,0,3,2}){surface.SetUV(coords[(i+rotate)%4]);surface.AddVertex(Transform(vertices[i]));}
                surface.GenerateNormals();surface.Commit(mesh);
            }
            parent.AddChild(new MeshInstance3D{Position=position,Mesh=mesh});
        }
        return true;
    }
    public static void Quad(SurfaceTool s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float width=1, float height=1)
    {
        var points=new[]{a,b,c,d}; var uv=new[]{new Vector2(0,0),new Vector2(width,0),new Vector2(width,height),new Vector2(0,height)};
        foreach(int i in new[]{0,2,1,0,3,2}) { s.SetUV(uv[i]); s.AddVertex(points[i]); }
    }
    public static void Box(Node parent, Vector3 position, Vector3 size, string id)
    {
        var mesh=new ArrayMesh(); var h=size/2;
        void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Material mat,float w,float v)
        { var s=new SurfaceTool();s.Begin(Mesh.PrimitiveType.Triangles);s.SetMaterial(mat);Quad(s,a,b,c,d,w,v);s.GenerateNormals();s.Commit(mesh); }
        Face(new(-h.X,h.Y,-h.Z),new(-h.X,h.Y,h.Z),new(h.X,h.Y,h.Z),new(h.X,h.Y,-h.Z),Material(id,true),size.Z,size.X);
        Face(new(-h.X,-h.Y,h.Z),new(-h.X,-h.Y,-h.Z),new(h.X,-h.Y,-h.Z),new(h.X,-h.Y,h.Z),Material(id,bottom:true),size.Z,size.X);
        Face(new(-h.X,h.Y,-h.Z),new(h.X,h.Y,-h.Z),new(h.X,-h.Y,-h.Z),new(-h.X,-h.Y,-h.Z),Material(id),size.X,size.Y);
        Face(new(h.X,h.Y,h.Z),new(-h.X,h.Y,h.Z),new(-h.X,-h.Y,h.Z),new(h.X,-h.Y,h.Z),Material(id,front:true),size.X,size.Y);
        Face(new(h.X,h.Y,-h.Z),new(h.X,h.Y,h.Z),new(h.X,-h.Y,h.Z),new(h.X,-h.Y,-h.Z),Material(id),size.Z,size.Y);
        Face(new(-h.X,h.Y,h.Z),new(-h.X,h.Y,-h.Z),new(-h.X,-h.Y,-h.Z),new(-h.X,-h.Y,h.Z),Material(id),size.Z,size.Y);
        parent.AddChild(new MeshInstance3D { Position=position,Mesh=mesh });
    }
}
