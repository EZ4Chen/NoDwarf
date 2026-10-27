using Godot;
namespace NoDwarf;

// Quiet only broad terrain surfaces. Buildings/items keep their detailed textures.
internal static class TerrainTextureStyle
{
    private static readonly Dictionary<ulong,StandardMaterial3D> Materials=new();
    private static readonly Dictionary<ulong,Texture2D> Textures=new();
    public static StandardMaterial3D Apply(StandardMaterial3D source)
    {
        ulong key=source.GetInstanceId();
        if(Materials.TryGetValue(key,out var existing))return existing;
        var result=(StandardMaterial3D)source.Duplicate();
        result.VertexColorUseAsAlbedo=true;
        result.TextureFilter=BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        if(source.AlbedoTexture is {} original)
        {
            ulong id=original.GetInstanceId();
            if(!Textures.TryGetValue(id,out var texture))
            {
                using var image=original.GetImage();
                if(image.IsCompressed())image.Decompress();
                image.Convert(Image.Format.Rgba8);image.ClearMipmaps();
                image.Resize(Math.Min(32,image.GetWidth()),Math.Min(32,image.GetHeight()),Image.Interpolation.Lanczos);
                var mean=new Color(0,0,0,0);int count=image.GetWidth()*image.GetHeight();
                for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)mean+=image.GetPixel(x,y);
                mean/=count;
                for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)
                {
                    var pixel=image.GetPixel(x,y);var softened=mean.Lerp(pixel,.48f);softened.A=pixel.A;image.SetPixel(x,y,softened);
                }
                image.GenerateMipmaps();texture=ImageTexture.CreateFromImage(image);Textures[id]=texture;
            }
            result.AlbedoTexture=texture;
        }
        Materials[key]=result;return result;
    }
}
