using Godot;
namespace NoDwarf;

// Compress detail around each texture's own colour, rather than washing out the
// whole scene. Cached at first use: no additional shader or per-frame processing.
internal static class WorldMaterialStyle
{
    private static readonly bool Original=OS.GetCmdlineUserArgs().Contains("--material-original");
    private static readonly Dictionary<(ulong,float),StandardMaterial3D> Materials=new();
    private static readonly Dictionary<(ulong,float),Texture2D> Textures=new();

    public static StandardMaterial3D Apply(StandardMaterial3D source,string id)
    {
        if(Original)return source; // Command-line visual comparison only.
        float contrast=id.Contains("ore",StringComparison.Ordinal)? .90f
            :id.EndsWith("_leaves",StringComparison.Ordinal)||id.StartsWith("nodwarf:cover_",StringComparison.Ordinal)? .45f
            :id.Contains("log",StringComparison.Ordinal)? .60f
            :id is "nodwarf:furnace" or "nodwarf:stove" or "nodwarf:anvil"? .85f
            : .68f;
        if(source.AlbedoTexture is not {} original)return source;
        var key=(source.GetInstanceId(),contrast);
        if(Materials.TryGetValue(key,out var cached))return cached;
        var textureKey=(original.GetInstanceId(),contrast);
        if(!Textures.TryGetValue(textureKey,out var texture))
        {
            using var image=original.GetImage();
            if(image.IsCompressed()&&image.Decompress()!=Error.Ok)throw new IOException("Cannot decode material texture");
            image.Convert(Image.Format.Rgba8);image.ClearMipmaps();
            var sum=Vector3.Zero;float weight=0;
            for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)
            {
                var pixel=image.GetPixel(x,y);
                sum+=new Vector3(pixel.R,pixel.G,pixel.B)*pixel.A;weight+=pixel.A;
            }
            // Transparent leaf holes must not darken the average or become opaque.
            var mean=weight>0?sum/weight:Vector3.Zero;
            for(int y=0;y<image.GetHeight();y++)for(int x=0;x<image.GetWidth();x++)
            {
                var pixel=image.GetPixel(x,y);
                var rgb=mean.Lerp(new Vector3(pixel.R,pixel.G,pixel.B),contrast);
                image.SetPixel(x,y,new Color(rgb.X,rgb.Y,rgb.Z,pixel.A));
            }
            if(image.GenerateMipmaps()!=Error.Ok)throw new IOException("Cannot generate styled material mipmaps");
            texture=ImageTexture.CreateFromImage(image);Textures[textureKey]=texture;
        }
        var result=(StandardMaterial3D)source.Duplicate();result.AlbedoTexture=texture;
        Materials[key]=result;return result;
    }
}
