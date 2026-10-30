using Godot;
using NoDwarf.Collections;
namespace NoDwarf;

internal static class PawnTextureCache
{
    public static ByteBudgetCache<TKey,ImageTexture> Create<TKey>(string setting,int fallbackMiB,bool shared=false) where TKey:notnull
    {
        long mib=ProjectSettings.GetSetting(setting,fallbackMiB).AsInt64();
        if(mib is <1 or >1024)throw new InvalidOperationException($"{setting} must be between 1 and 1024 MiB");
        // Shared UI resources can outlive their cache entry: controls and callers
        // retain RefCounted textures. Eviction drops our reference, never disposes theirs.
        return new(mib*1024*1024,shared?static _=>{}:static texture=>texture.Dispose());
    }
    // Payload estimate, including mip levels; driver/resource overhead is not VRAM telemetry.
    public static long RgbaBytes(Image image)
    {
        long bytes=0;int w=image.GetWidth(),h=image.GetHeight();
        while(true){bytes+=(long)w*h*4;if(!image.HasMipmaps()||w==1&&h==1)return bytes;w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
    }
}
