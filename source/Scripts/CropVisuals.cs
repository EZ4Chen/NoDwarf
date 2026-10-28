using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

internal static class CropVisuals
{
    private static readonly Dictionary<(string,int),Mesh> Meshes=new();
    private static readonly Dictionary<bool,Mesh> Beds=new();
    public static int Stage(FarmPlot plot)
        =>!plot.Planted?0:1+(int)(Math.Clamp(plot.Growth/DemoWorld.GrowTime(plot.Crop),0,1)*(plot.Crop=="grain"?7:3));

    public static void AddTo(Node3D root,string crop,int stage)
    {
        bool fungus=crop=="mushroom";
        if(!Beds.TryGetValue(fungus,out var bed))
        {
            using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetMaterial(BlockTextures.Material(fungus?"nodwarf:crop_fungus_bed":"nodwarf:crop_soil"));
            BlockTextures.Quad(surface,new(-.49f,.025f,-.49f),new(-.49f,.025f,.49f),new(.49f,.025f,.49f),new(.49f,.025f,-.49f));
            surface.GenerateNormals();Beds[fungus]=bed=surface.Commit();
        }
        root.AddChild(new MeshInstance3D{Mesh=bed,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        if(stage==0||CropCatalog.IsTree(crop))return;
        if(!Meshes.TryGetValue((crop,stage),out var mesh))Meshes[(crop,stage)]=mesh=Build(crop,stage);
        root.AddChild(new MeshInstance3D{Mesh=mesh,Position=new(0,.035f,0),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }

    private static Mesh Build(string crop,int stage)
    {
        if(crop=="potato")
        {
            // The pack's potato models inherit the carrot geometry and replace
            // its texture. Keep its UVs and stage-specific buried root height.
            var holder=new Node3D();BlockTextures.Model(holder,Vector3.Zero,$"nodwarf:crop_potato_{stage-1}");
            using var merged=new SurfaceTool();merged.Begin(Mesh.PrimitiveType.Triangles);
            merged.SetMaterial(BlockTextures.Material($"nodwarf:crop_potato_{stage-1}"));
            foreach(var node in holder.GetChildren().OfType<MeshInstance3D>())
                for(int i=0;i<node.Mesh.GetSurfaceCount();i++)merged.AppendFrom(node.Mesh,i,node.Transform);
            var potato=merged.Commit();holder.Free();return potato;
        }
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetMaterial(BlockTextures.Material(crop=="grain"?$"nodwarf:crop_wheat_{stage-1}"
            :crop=="mushroom"?"nodwarf:crop_mushroom":"nodwarf:crop_cotton_foliage"));
        void Cross(Vector3 p,float width,float height)
        {
            float h=width/2;
            BlockTextures.Quad(surface,p+new Vector3(-h,height,-h),p+new Vector3(h,height,h),p+new Vector3(h,0,h),p+new Vector3(-h,0,-h));
            BlockTextures.Quad(surface,p+new Vector3(-h,height,h),p+new Vector3(h,height,-h),p+new Vector3(h,0,-h),p+new Vector3(-h,0,h));
        }
        if(crop=="grain")
        {
            foreach(float offset in new[]{-.24f,.24f})
            {
                BlockTextures.Quad(surface,new(-.47f,.9f,offset),new(.47f,.9f,offset),new(.47f,0,offset),new(-.47f,0,offset));
                BlockTextures.Quad(surface,new(offset,.9f,-.47f),new(offset,.9f,.47f),new(offset,0,.47f),new(offset,0,-.47f));
            }
        }
        else if(crop=="mushroom")
            foreach(var p in new[]{new Vector3(-.24f,0,-.2f),new(.22f,0,.2f),new(.25f,0,-.24f)})Cross(p,.18f+stage*.075f,.16f+stage*.075f);
        else Cross(Vector3.Zero,.35f+stage*.1f,.25f+stage*.12f);
        surface.GenerateNormals();var mesh=surface.Commit();
        if(crop=="cotton"&&stage>=3)
        {
            // No cotton asset in this pack: temporary foliage + distinct white
            // bolls, rather than presenting another food crop as cotton.
            var boll=new SphereMesh{Radius=stage==4?.085f:.045f,Height=stage==4?.17f:.09f,RadialSegments=8,Rings=4};
            using var bolls=new SurfaceTool();bolls.Begin(Mesh.PrimitiveType.Triangles);
            bolls.SetMaterial(new StandardMaterial3D{AlbedoColor=new("eee9dc"),Roughness=1});
            foreach(var p in new[]{new Vector3(-.20f,.42f,0),new(.15f,.5f,.08f),new(0,.62f,-.04f)})
                bolls.AppendFrom(boll,0,new Transform3D(Basis.Identity,p));
            bolls.Commit(mesh);
        }
        return mesh;
    }
}
