using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public static class FlatPawnAppearanceChecks
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var a=new CharacterAppearance{Body="male",Hair="bald",Beard="none"}.Normalize();
        var custom=(a with{Hair="topknot",HairColor="#AABBCC",Skin="#987654",BeardColor="#34ABCD"}).Normalize();
        var restored=AppearanceStorage.Deserialize(AppearanceStorage.Serialize([new DemoWorker{Id="palette",Name="配色测试",Appearance=custom}]))["palette"];
        Check(restored==custom&&restored.HairColor=="#aabbcc"&&restored.BeardColor=="#34abcd","Independent palette archive roundtrip");
        Check((a with{HairColor="#abc",Skin="invalid",BeardColor="#ffffff00"}).Normalize() is {HairColor:"chestnut",Skin:"warm",BeardColor:"silver"},"Invalid palette validation");
        Image Preview(CharacterAppearance appearance,int d=0)=>DirectionalDwarfView.Preview(appearance,[],d).GetImage();
        var light=Preview(a);var dark=Preview(a with{Skin="deep"});
        Check(light.GetPixel(100,185)!=dark.GetPixel(100,185),"Body skin unchanged");
        Check(!light.GetRegion(new(45,30,112,78)).GetData().SequenceEqual(dark.GetRegion(new(45,30,112,78)).GetData()),"Head skin unchanged");
        Check(light.GetRegion(new(70,230,60,16)).GetData().SequenceEqual(dark.GetRegion(new(70,230,60,16)).GetData()),"Skin recoloring tinted underwear");
        var h=a with{Hair="sweep",Beard="full",HairColor="gold",BeardColor="black"};
        var before=Preview(h);var beard=Preview(h with{BeardColor="white"});var hair=Preview(h with{HairColor="auburn"});
        Check(before.GetRegion(new(30,0,140,70)).GetData().SequenceEqual(beard.GetRegion(new(30,0,140,70)).GetData()),"Beard color changed head hair");
        Check(!before.GetData().SequenceEqual(beard.GetData()),"Beard color has no effect");
        Check(!before.GetData().SequenceEqual(hair.GetData()),"Hair color has no effect");
        Check(before.GetRegion(new(30,175,140,90)).GetData().SequenceEqual(hair.GetRegion(new(30,175,140,90)).GetData()),"Hair color changed body/underwear");
        foreach(string face in CharacterAppearance.Faces)
        {
            var root=DirectionalDwarfView.Atlas("roots-"+(face=="oval"?"lean":face)).GetImage();
            Check(root.GetPixel(262,75).A>.5f&&root.GetPixel(270,105).A>.5f,"Missing rear scalp root coverage: "+face);
            foreach(string style in CharacterAppearance.Hairs)
            {
                var right=Preview(a with{Face=face,Hair=style},1);var left=Preview(a with{Face=face,Hair=style},3);right.FlipX();
                Check(right.GetData().SequenceEqual(left.GetData()),"Left/right hairstyle mirroring: "+style);
            }
        }
        GD.Print("FLAT_APPEARANCE_OK: custom RGB archive, independent hair/beard/skin, underwear preservation, three skulls, eight hair choices and mirroring");
    }
}
