using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private bool _showLevelGuide=true;
    private MeshInstance3D? _levelGuide;
    private Label3D? _levelGuideLabel;
    private void UpdateLevelGuide()
    {
        bool visible=_showLevelGuide&&CurrentView.Level is not null&&_camera.Size<=70&&
            !DossierOpen&&_titleDialog?.Visible!=true&&_characterEditor?.Visible!=true;
        if(!visible){if(_levelGuide is not null)_levelGuide.Hide();return;}
        // One reusable mesh, no physics bodies or terrain queries. This is a
        // translucent reference plane, not terrain or newly discovered cells.
        if(_levelGuide is null)
        {
            var mesh=new ImmediateMesh();
            var material=new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency=BaseMaterial3D.TransparencyEnum.Alpha,VertexColorUseAsAlbedo=true,
                NoDepthTest=true,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
            mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles,material);
            void Line(Vector3 a,Vector3 b,float width,Color color)
            {
                var side=(b-a).Normalized().Cross(Vector3.Up)*width/2;
                mesh.SurfaceSetColor(color);
                foreach(var p in new[]{a-side,a+side,b+side,a-side,b+side,b-side})mesh.SurfaceAddVertex(p);
            }
            for(int i=-4;i<=5;i++)for(int j=-4;j<5;j++)
            {
                float x=i-.5f,z=j-.5f;
                float fade=1-Math.Max(Math.Abs(x),Math.Abs(z+.5f))/5;
                var color=new Color(.65f,.80f,.82f,.28f*fade);
                Line(new(x,0,z),new(x,0,z+1),.025f,color);
                Line(new(z,0,x),new(z+1,0,x),.025f,color);
            }
            foreach(float side in new[]{-.5f,.5f})
            {
                Line(new(side,0,-.5f),new(side,0,.5f),.028f,new Color(.91f,.79f,.51f,.65f));
                Line(new(-.5f,0,side),new(.5f,0,side),.028f,new Color(.91f,.79f,.51f,.65f));
            }
            mesh.SurfaceEnd();
            _levelGuide=new MeshInstance3D{Name="CurrentLayerGuide",Mesh=mesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_levelGuide);
            _levelGuideLabel=new Label3D{FontSize=28,PixelSize=.009f,OutlineSize=7,Modulate=new Color("d2c397"),
                Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,Position=new(0,.25f,0)};
            _levelGuide.AddChild(_levelGuideLabel);
        }
        int level=ActiveLevel;
        // Precise view exposes the top of the chosen voxel; room views mark
        // the floor residents stand on. Keep the label tied to the chosen layer.
        float height=(_interiorView||_floorWalls?level:Math.Max(level,BuildLevel)+1)+.025f;
        var screen=GetViewport().GetVisibleRect().GetCenter();
        var origin=_camera.ProjectRayOrigin(screen);var ray=_camera.ProjectRayNormal(screen);
        var center=origin+ray*((height-origin.Y)/ray.Y);
        center.X=Mathf.Round(center.X);center.Z=Mathf.Round(center.Z);center.Y=height;
        _levelGuide.Position=center;_levelGuide.Show();
        string label=$"当前层 {level} m";
        _levelGuideLabel!.PixelSize=.014f*_camera.Size/18;
        _levelGuideLabel.Position=new(0,_camera.Size*.04f,0);
        if(_levelGuideLabel.Text!=label)_levelGuideLabel.Text=label;
    }
}
