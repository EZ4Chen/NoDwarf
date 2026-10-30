using Godot;
namespace NoDwarf;
public partial class Main
{
    private Vector3 _panAnchor;
    private float _panHeight;

    private Vector3 CameraPlanePoint(Vector2 screen,float height)
    {
        var origin=_camera.ProjectRayOrigin(screen);
        var direction=_camera.ProjectRayNormal(screen);
        return origin+direction*((height-origin.Y)/direction.Y);
    }
    private void BeginCameraPan(Vector2 screen)
    {
        _dragging=true;_panHeight=_focus.Y;
        _panAnchor=CameraPlanePoint(screen,_panHeight);
    }
    private void DragCameraPan(Vector2 screen)
    {
        _focus+=_panAnchor-CameraPlanePoint(screen,_panHeight);
        PositionCamera();
    }
    private void PanCameraPixels(Vector2 delta)
    {
        var center=GetViewport().GetVisibleRect().GetCenter();
        _focus+=CameraPlanePoint(center+delta,_focus.Y)-CameraPlanePoint(center,_focus.Y);
        PositionCamera();
    }
    private void AnchorCameraOrbit()
    {
        // Rebase along the centre ray, preserving every screen position. Do this
        // once per gesture: repeated picking while rotating makes the pivot swim.
        var center=GetViewport().GetVisibleRect().GetCenter();
        var hit=PickObject(center,1);
        if(hit.Count==0)return;
        var point=hit["position"].AsVector3();
        float edge=(NoDwarf.Simulation.TerrainMap.Size-1)/2f;
        if(Math.Abs(point.X)>edge||Math.Abs(point.Z)>edge)return;
        _focus=point;PositionCamera();
    }
    private float OrbitSensitivity=>.006f*(float)PreferencesRuntime.Current.OrbitSpeed*Mathf.Clamp(38f/_camera.Size,.25f,1f);
}
