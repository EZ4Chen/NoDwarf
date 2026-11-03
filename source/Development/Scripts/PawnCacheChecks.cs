using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class DirectionalDwarfView
{
    internal static void CheckVisualCache(Node parent)
    {
        static void Check(bool ok,string why){if(!ok)throw new Exception("Pawn cache: "+why);}
        static ItemStack Gear(string item,string slot)=>new(){Item=item,Slot=slot,Count=1};
        var a=new CharacterAppearance{Body="male",Skin="warm",Hair="sweep",Beard="full"};
        var dressed=new[]{Gear("armor","body"),Gear("helmet","head")};
        var hidden=a with{Hair="braid",HairColor="gold",Cloth="plum",Outfit="coat",BodySex="female"};
        var front=Preview(a,dressed,0);
        Check(front==Preview(hidden,dressed,0),"hidden hair/clothing/sex split identical portraits");
        Check(front==Preview(a,dressed.Append(Gear("sword","main")),0),"held tool split a body-only portrait");
        Check(Preview(a,[],0)!=Preview(hidden,[],0),"uncovering hair reused an obsolete portrait");
        Check(Preview(a,[],2)==Preview(a with{Beard="none",BeardColor="white"},[],2),"rear beard split the cache");
        Check(Preview(a,[],0)!=Preview(a with{BeardColor="white"},[],0),"visible beard failed to update");
        Check(Preview(a,[],0)==Preview(a with{Skin="#efb17f",Body="male"},[],0),"equivalent palette failed to reuse");
        var right=Preview(a,[],1);var left=Preview(a,[],3);
        using(var expected=right.GetImage())using(var actual=left.GetImage())
        {expected.FlipX();Check(expected.GetData().SequenceEqual(actual.GetData()),"mirrored preview pixels changed");}
        // Compare canonical keys with the original composition inputs: equality
        // must describe the pixels, not merely make cache counters look better.
        foreach(string body in CharacterAppearance.Bodies)foreach(int d in new[]{0,1,2})
        foreach(var clothes in new[]{Clothes.None,Clothes.Shirt|Clothes.Trousers,Clothes.Armor|Clothes.Shirt|Clothes.Trousers|Clothes.Helmet|Clothes.Cap})
        {
            var source=(a with{Body=body,BodySex="female",Outfit="apron",HairColor="gold",BeardColor="white"}).Normalize();
            string[] items=Enum.GetValues<Clothes>().Where(c=>c!=Clothes.None&&(clothes&c)!=0).Select(c=>c.ToString().ToLowerInvariant()).ToArray();
            using var reference=Compose(source,clothes,d).Item1;
            using var expected=reference.GetImage();expected.ClearMipmaps();
            using var actual=Preview(source,items.Select(i=>Gear(i,"body")),d).GetImage();
            Check(expected.GetData().SequenceEqual(actual.GetData()),"canonical key changed pixels: "+body+"/"+d+"/"+clothes);
        }

        long pinned=Wardrobes.PinnedBytes,palms=Hands.PinnedBytes;
        var view=new DirectionalDwarfView();
        try
        {
            // Initialization before EnterTree, and mutation while detached, must not leak leases.
            view.Initialize();view.SetAppearance(a);Check(Wardrobes.PinnedBytes==pinned,"detached initialization pinned a body");
            parent.AddChild(view);view.SetEquipment(dressed);
            var original=view._body.Texture;long hits=Wardrobes.Hits,misses=Wardrobes.Misses;
            view.SetAppearance(hidden);view.SetEquipment(dressed.Append(Gear("sword","main")));
            var sword=view._hands["main"].GetNode<Sprite3D>("Held").Texture;
            view.SetEquipment(dressed.Append(Gear("pickaxe","main")));
            Check(original==view._body.Texture&&Wardrobes.Hits==hits&&Wardrobes.Misses==misses,"unrelated change reacquired/rebuilt body");
            Check(sword!=view._hands["main"].GetNode<Sprite3D>("Held").Texture&&view.Wears("pickaxe"),"held tool failed to update separately");
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++)view.SetAppearance(hidden);
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(allocated<1024,"unchanged frames allocate appearance copies");
            GD.Print($"PAWN_STABLE_APPEARANCE_MEASURE: calls=10000 allocated_bytes={allocated}");
            parent.RemoveChild(view);view.SetAppearance(a with{Skin="deep"});
            Check(Wardrobes.PinnedBytes==pinned&&Hands.PinnedBytes==palms,"detached changes retained leases");
            parent.AddChild(view);
            Check(view._body.Texture is not null,"reentry did not restore body");
        }
        finally{view.Free();}
        Check(Wardrobes.PinnedBytes==pinned&&Hands.PinnedBytes==palms,"freed view retained leases");

        long budget=Previews.Budget;
        var holder=new TextureRect();parent.AddChild(holder);
        try
        {
            Previews.Budget=0; // Cache eviction must not dispose a caller's shared resource.
            holder.Texture=front;
            using(var image=holder.Texture.GetImage())Check(image.GetWidth()==200,"UI portrait disposed by eviction");
            Previews.Budget=3L*200*280*4;
            var appearances=Enumerable.Range(0,4).Select(i=>a with{Skin=$"#9080{10+i:x2}"}).ToArray();
            var t=appearances.Take(3).Select(x=>Preview(x,[],0)).ToArray();
            Check(Preview(appearances[0],[],0)==t[0],"hot portrait lookup changed identity");
            _=Preview(appearances[3],[],0);
            Check(Previews.Count==3&&Previews.Bytes<=Previews.Budget,"portrait budget not bounded");
            Check(Preview(appearances[0],[],0)==t[0]&&Preview(appearances[2],[],0)==t[2],"LRU evicted a recent portrait");
            Check(Preview(appearances[1],[],0)!=t[1],"oldest portrait was not evicted");
            using(var image=t[1].GetImage())Check(image.GetHeight()==280,"evicted texture invalidated its caller");
        }
        finally{holder.Free();Previews.Budget=budget;}
        CheckCollectedPortraits(parent);
        GD.Print("PAWN_INVALIDATION_OK: visible-only keys, hidden gear/palettes, separate hands, mirror pixels, shared UI eviction, LRU and detached lifecycle");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (WeakReference Appearance,ulong Texture) EphemeralPortrait(TextureRect? owner,int serial)
    {
        var source=new CharacterAppearance{Skin=$"#{0xa01000+serial:x6}",Hair="braid"};
        var texture=Preview(source,[],0);if(owner is not null)owner.Texture=texture;
        return (new WeakReference(source),texture.GetInstanceId());
    }
    private static void CollectDrafts()
    {for(int i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();}}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ReadAndReleasePortrait(TextureRect owner)
    {
        // Keep the getter's managed wrapper out of the caller's Debug/JIT lifetime.
        using var pixels=owner.Texture.GetImage();
        if(pixels.GetWidth()!=200)throw new Exception("UI portrait cannot be read after GC");
        owner.Texture=null;
    }
    private static void CheckCollectedPortraits(Node parent)
    {
        long budget=Previews.Budget;var holder=new TextureRect();parent.AddChild(holder);
        try
        {
            Previews.Budget=0;
            var retained=EphemeralPortrait(holder,0);
            var discarded=Enumerable.Range(1,64).Select(i=>EphemeralPortrait(null,i)).ToArray();
            CollectDrafts();
            if(retained.Appearance.IsAlive||discarded.Any(x=>x.Appearance.IsAlive))throw new Exception("Cached visual keys retained dead appearance drafts");
            if(discarded.Any(x=>GodotObject.IsInstanceIdValid(x.Texture)))throw new Exception("Evicted unowned portraits retained native resources after GC");
            if(!GodotObject.IsInstanceIdValid(retained.Texture))throw new Exception("GC freed a portrait still held by UI");
            ReadAndReleasePortrait(holder);CollectDrafts();
            if(GodotObject.IsInstanceIdValid(retained.Texture))throw new Exception("Released UI portrait retained native resources after GC");
            GD.Print("PAWN_RESOURCE_LIFETIME_OK: 64 discarded portraits and drafts collected; UI-owned texture survives eviction/GC and releases afterward");
        }
        finally{holder.Free();Previews.Budget=budget;}
    }
}
