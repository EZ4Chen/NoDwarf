using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private Node3D? _digRegionHighlight;
    private (DemoWorld World,string Id,int JobId,Cell Min,Cell Max,int Level)? _digHighlightKey;
    private void SyncDigRegionHighlight(DemoJob? selected)
    {
        if(selected is null||_tool!="select"||!_selectionPanel.Visible)
        {
            if(_digRegionHighlight is not null)_digRegionHighlight.Hide();
            _digHighlightKey=null;return;
        }
        var lo=selected.DigRegionMin??selected.Cell;
        var hi=selected.DigRegionMax??selected.Cell+new Cell(0,0,selected.DigHeight-1);
        var key=(_world,selected.DigRegion,selected.DigRegion==""?selected.Id:0,lo,hi,ActiveLevel);
        if(_digHighlightKey==key){_digRegionHighlight!.Show();return;}
        _digHighlightKey=key;
        if(_digRegionHighlight is null){_digRegionHighlight=new Node3D{Name="SelectedDigRegion"};AddChild(_digRegionHighlight);}
        Clear(_digRegionHighlight);_digRegionHighlight.Show();
        float w=hi.X-lo.X+1,d=hi.Z-lo.Z+1,h=hi.Level-lo.Level+1;
        _digRegionHighlight.Position=WorldPosition(lo)+new Vector3((w-1)/2,h/2,(d-1)/2);
        // Two draw calls regardless of voxel count; full volume remains visible through terrain.
        var outline=new StandardMaterial3D{AlbedoColor=new Color("62e5ff"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest=true,RenderPriority=20};
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(outline);
        void Bar(Vector3 p,Vector3 size)
        {using var mesh=new BoxMesh{Size=size};surface.AppendFrom(mesh,0,new Transform3D(Basis.Identity,p));}
        foreach(float a in new[]{-1f,1f})foreach(float b in new[]{-1f,1f})
        {
            Bar(new(a*w/2,b*h/2,0),new(.045f,.045f,d));
            Bar(new(0,a*h/2,b*d/2),new(w,.045f,.045f));
            Bar(new(a*w/2,0,b*d/2),new(.045f,h,.045f));
        }
        for(int y=1;y<h;y++)foreach(float sign in new[]{-1f,1f})
        {
            Bar(new(sign*w/2,y-h/2,0),new(.012f,.012f,d));
            Bar(new(0,y-h/2,sign*d/2),new(w,.012f,.012f));
        }
        bool inLayer=ActiveLevel>=lo.Level&&ActiveLevel<=hi.Level;
        float planeY=(inLayer?ActiveLevel+1:hi.Level+1)-lo.Level-h/2+.025f;
        if(inLayer)foreach(float sign in new[]{-1f,1f})
        {
            Bar(new(sign*w/2,planeY,0),new(.075f,.045f,d));
            Bar(new(0,planeY,sign*d/2),new(w,.045f,.075f));
        }
        _digRegionHighlight.AddChild(new MeshInstance3D{Name="Boundary",Mesh=surface.Commit(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        var fill=new StandardMaterial3D{AlbedoColor=new Color(.20f,.78f,1f,.16f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,NoDepthTest=true,CullMode=BaseMaterial3D.CullModeEnum.Disabled,RenderPriority=19};
        _digRegionHighlight.AddChild(new MeshInstance3D{Name="SelectedLayer",Position=new(0,planeY,0),Visible=inLayer,
            Mesh=new PlaneMesh{Size=new(w,d),Material=fill},CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
    }
    private void HideInactiveDigHighlight()
    {
        if(_digRegionHighlight is {Visible:true}&&(_tool!="select"||!_selectionPanel.Visible||_selectedWorker>=0||_selectedCell is null))
            SyncDigRegionHighlight(null);
    }
}
