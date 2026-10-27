using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private DirectionalLight3D _sectionLight=null!;
    private bool UndergroundView
    {
        get
        {
            int x=Math.Clamp((int)Math.Round(_focus.X+(TerrainMap.Size-1)/2f),0,TerrainMap.Size-1);
            int z=Math.Clamp((int)Math.Round(_focus.Z+(TerrainMap.Size-1)/2f),0,TerrainMap.Size-1);
            return _cutaway&&_focus.Y<_world.Terrain[x,z].Level-.25f;
        }
    }
    private void ConfigureDepthLighting()
    {
        _dayEnvironment.SsaoEnabled=OS.GetCmdlineUserArgs().Contains("--depth-ssao");
        _dayEnvironment.SsaoRadius=.85f;
        _dayEnvironment.SsaoIntensity=1.1f;
        _sectionLight=new DirectionalLight3D
        {
            RotationDegrees=new(-60,-35,0),LightColor=new("dce5ef"),LightEnergy=.5f,
            ShadowEnabled=true,DirectionalShadowMaxDistance=100,Visible=false
        };
        AddChild(_sectionLight);
    }
    private void RefreshDepthLighting(float daylight,float weather)
    {
        bool below=UndergroundView;
        _sun.Visible=!below;_sectionLight.Visible=below;
        // Cutaway illumination is a readable section view, independent of outdoor time.
        // Both terrain and furnishings use these same lights; unknown rock stays hidden.
        _dayEnvironment.AmbientLightColor=below?new Color("b5c0cf"):new Color("9eafd0").Lerp(new("e1edda"),daylight);
        _dayEnvironment.AmbientLightEnergy=below?.38f:.26f+.12f*daylight*weather;
        var tint=below?new Color("c2cbd4"):new Color("8695ae").Lerp(Colors.White,Math.Clamp(daylight*weather,0,1));
        foreach(var person in _people)
            if(person.GetChildOrNull<Sprite3D>(0) is {} sprite)sprite.Modulate=tint;
    }
    private static StandardMaterial3D? _contactMaterial;
    private static void AddResidentContact(Node3D person)
    {
        if(_contactMaterial is null)
        {
            var gradient=new Gradient{Colors=new[]{new Color(0,0,0,.28f),new Color(0,0,0,0)},Offsets=new[]{0f,1f}};
            var texture=new GradientTexture2D{Gradient=gradient,Width=32,Height=32,Fill=GradientTexture2D.FillEnum.Radial,FillFrom=new(.5f,.5f),FillTo=new(1,.5f)};
            _contactMaterial=new StandardMaterial3D{AlbedoTexture=texture,Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,DepthDrawMode=BaseMaterial3D.DepthDrawModeEnum.Disabled};
        }
        person.AddChild(new MeshInstance3D{Name="GroundContact",Position=new(0,.018f,0),
            Mesh=new PlaneMesh{Size=new(.68f,.48f),Material=_contactMaterial},CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }
}
