using Godot;
namespace NoDwarf;

// Broad terrain has its own stronger softening; start from raw textures so the
// separate building/foliage style is not applied twice.
internal static class TerrainTextureStyle
{
    private readonly record struct Detail(float Contrast,int Resolution,float Grain=0);
    private static readonly Dictionary<(ulong,Detail),StandardMaterial3D> Materials=new();
    private static readonly Dictionary<(ulong,Detail),Texture2D> Textures=new();
    public static StandardMaterial3D Apply(StandardMaterial3D source,string blockId,bool top)
    {
        // Retain fine grass blades and sand ripples at close range. Mipmaps fade
        // that detail with distance, while broad rock surfaces stay quiet.
        bool grass=top&&(blockId is "nodwarf:grass" or "nodwarf:dry_grass" or "nodwarf:dry_grass_ramp");
        bool sand=blockId is "nodwarf:sand" or "nodwarf:sand_ramp";
        var detail=grass?new Detail(.46f,64):sand?new Detail(.38f,64,.018f)
            :new Detail(blockId.Contains("ore",StringComparison.Ordinal)?.80f:.24f,32);
        var key=(source.GetInstanceId(),detail);
        if(Materials.TryGetValue(key,out var existing))return existing;
        var result=(StandardMaterial3D)source.Duplicate();
        result.VertexColorUseAsAlbedo=true;
        result.TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        if(source.AlbedoTexture is {} original)
        {
            var id=(original.GetInstanceId(),detail);
            if(!Textures.TryGetValue(id,out var texture))
            {
                using var image=original.GetImage();
                if(image.IsCompressed())image.Decompress();
                image.Convert(Image.Format.Rgba8);image.ClearMipmaps();
                image.Resize(Math.Min(detail.Resolution,image.GetWidth()),Math.Min(detail.Resolution,image.GetHeight()),Image.Interpolation.Lanczos);
                var mean=new Color(0,0,0,0);int count=image.GetWidth()*image.GetHeight();
                for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)mean+=image.GetPixel(x,y);
                mean/=count;
                for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)
                {
                    var pixel=image.GetPixel(x,y);var softened=mean.Lerp(pixel,detail.Contrast);
                    if(detail.Grain>0)
                    {
                        // Fixed, zero-centred grain; no animated noise or per-frame work.
                        uint hash=unchecked((uint)(x+y*image.GetWidth())*747796405u+2891336453u);
                        hash=unchecked(((hash>>((int)(hash>>28)+4))^hash)*277803737u);hash=(hash>>22)^hash;
                        float grain=((hash&65535)/65535f*2-1)*detail.Grain;
                        softened.R=Math.Clamp(softened.R+grain,0,1);softened.G=Math.Clamp(softened.G+grain,0,1);softened.B=Math.Clamp(softened.B+grain,0,1);
                    }
                    softened.A=pixel.A;image.SetPixel(x,y,softened);
                }
                image.GenerateMipmaps();texture=ImageTexture.CreateFromImage(image);Textures[id]=texture;
            }
            result.AlbedoTexture=texture;
        }
        Materials[key]=result;return result;
    }
}
