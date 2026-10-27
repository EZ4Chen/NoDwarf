using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private MeshInstance3D _waterMesh=null!;
    private Button _gateAction=null!;
    private StandardMaterial3D? _waterMaterial;
    private (DemoWorld? World,long Revision,float Top,int Stamp,int X,int Z,int Size) _waterKey;
    private void RefreshFluidVisuals()
    {
        var key=(_world,_world.Fluids.Revision,CurrentView.Top,_viewStamp,(int)_focus.X/8,(int)_focus.Z/8,(int)_camera.Size);
        if(_waterMesh is not null&&_waterKey==key)return;_waterKey=key;
        if(_waterMesh is null){_waterMesh=new MeshInstance3D{CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_waterMesh);}
        var st=new SurfaceTool();st.Begin(Mesh.PrimitiveType.Triangles);
        if(_waterMaterial is null)
        {
            _waterMaterial=(StandardMaterial3D)BlockTextures.Material("nodwarf:water").Duplicate();
            var tint=_waterMaterial.AlbedoColor;tint.A=.72f;_waterMaterial.AlbedoColor=tint;
            _waterMaterial.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;_waterMaterial.Roughness=.32f;_waterMaterial.CullMode=BaseMaterial3D.CullModeEnum.Disabled;
        }
        st.SetMaterial(_waterMaterial);
        int faces=0;float radius=_camera.Size*1.5f+12;
        foreach(var f in _world.Fluids.Cells.Values)
        {
            var c=f.Cell;var p=WorldPosition(c);
            if(!VisibleAt(c)||Math.Abs(p.X-_focus.X)>radius||Math.Abs(p.Z-_focus.Z)>radius)continue;
            float bottom=c.Level+(1000-_world.FluidCapacity(c))/1000f;
            float top=Math.Min(bottom+f.Amount/1000f,CurrentView.Top);if(top<=bottom)continue;
            Vector3 P(float x,float y,float z)=>TerrainView.Point(c.X+x,y,c.Z+z);
            bool above=_world.Fluids.Amount(c+new Cell(0,0,1))>0&&top>=c.Level+1&&CurrentView.Top>top;
            if(!above){BlockTextures.Quad(st,P(-.5f,top,-.5f),P(-.5f,top,.5f),P(.5f,top,.5f),P(.5f,top,-.5f));faces++;}
            foreach(var d in new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)})
            {
                var n=c+d;float adjacent=_world.Fluids.Amount(n)>0?n.Level+(1000-_world.FluidCapacity(n)+_world.Fluids.Amount(n))/1000f:bottom;
                float low=Math.Max(bottom,adjacent);if(low>=top)continue;
                float x=d.X*.5f,z=d.Z*.5f,dx=d.Z*.5f,dz=d.X*.5f;
                BlockTextures.Quad(st,P(x-dx,low,z-dz),P(x+dx,low,z+dz),P(x+dx,top,z+dz),P(x-dx,top,z-dz));faces++;
            }
        }
        if(faces==0){_waterMesh.Mesh=null;return;}st.GenerateNormals();_waterMesh.Mesh=st.Commit();
    }
    private void RefreshFluidSelection()
    {
        _gateAction.Visible=false;
        if(_selectedWorker>=0||_selectedCell is not Cell c||!CurrentView.Known(_world,c))return;
        var description=_world.FluidDescription(c);if(description!=""){_selection.Text+="\n"+description;BottomLeft(_selectionPanel,360,400);_selectionPanel.OffsetBottom=-76;}
        if(_world.Buildings.TryGetValue(c,out var b)&&b.Kind==BuildKind.Floodgate)
        {_gateAction.Visible=true;_gateAction.Text=_world.GateOpen(c)?"关闭闸门":"打开闸门";}
    }
    private void ToggleSelectedGate()
    {if(_selectedCell is Cell c){_world.ToggleFloodgate(c);SyncWorld();RefreshUi();}}
}
