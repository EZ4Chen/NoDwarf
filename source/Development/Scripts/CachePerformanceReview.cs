using System.Diagnostics;
using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private void ReviewCachePerformance()
    {
        try
        {
            Func<CharacterAppearance,IEnumerable<string>,int,ulong> composite=DirectionalDwarfView.CompositeIdentity;
            var appearances=Enumerable.Range(0,100).Select(i=>new CharacterAppearance{Skin=$"#{64+i:x2}8060"}.Normalize()).ToArray();
            string[] worn=["shirt","trousers"];
            var initial=new ulong[300];
            for(int d=0;d<3;d++)for(int i=0;i<100;i++)initial[d*100+i]=composite(appearances[i],worn,d);
            int rebuilt=0;long bytes=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
            for(int pass=0;pass<3;pass++)for(int d=0;d<3;d++)for(int i=0;i<100;i++)
                if(composite(appearances[i],worn,d)!=initial[d*100+i])rebuilt++;
            timer.Stop();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            GD.Print($"PAWN_CACHE_MEASURE: actors=100 directions=3 warm_passes=3 regenerated={rebuilt} elapsed_ms={timer.Elapsed.TotalMilliseconds:F2} allocated_bytes={bytes}");
            GD.Print("PAWN_CACHE_STATS: "+System.Text.Json.JsonSerializer.Serialize(DirectionalDwarfView.CacheStatistics));
            if(OS.GetCmdlineUserArgs().Contains("--cache-assert")&&rebuilt!=0)throw new Exception("Warm pawn directions were regenerated");
            var portraits=Enumerable.Range(0,300).Select(i=>new CharacterAppearance{Skin=$"#{0x805000+i:x6}"}).ToArray();
            var originals=portraits.Select(a=>EquipmentArt.Texture(a,[])).ToArray();
            int portraitRebuilt=0;bytes=GC.GetAllocatedBytesForCurrentThread();timer.Restart();
            for(int pass=0;pass<2;pass++)for(int i=0;i<portraits.Length;i++)
                if(EquipmentArt.Texture(portraits[i],[]).GetInstanceId()!=originals[i].GetInstanceId())portraitRebuilt++;
            timer.Stop();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            GD.Print($"PORTRAIT_CACHE_MEASURE: actors=300 warm_passes=2 regenerated={portraitRebuilt} elapsed_ms={timer.Elapsed.TotalMilliseconds:F2} allocated_bytes={bytes}");
            if(OS.GetCmdlineUserArgs().Contains("--cache-assert")&&portraitRebuilt!=0)throw new Exception("Warm portraits were regenerated");
            DirectionalDwarfView.CheckVisualCache(this);
            GD.Print("PAWN_CACHE_FINAL_STATS: "+System.Text.Json.JsonSerializer.Serialize(DirectionalDwarfView.CacheStatistics));
            DirectionalDwarfView.CheckPinnedTextures(this);
            GD.Print("CACHE_PERFORMANCE_OK: headless texture generation only; not a GPU/FPS measurement");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
