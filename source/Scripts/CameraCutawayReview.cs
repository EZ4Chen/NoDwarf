using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewCameraCutaway()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("CAMERA_CUTAWAY: "+why);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Snap(string name)
            {
                SyncWorld();_terrainView.FlushMeshes();RefreshUi();await Frame();
                if(DisplayServer.GetName()=="headless")return;
                RenderingServer.ForceDraw();
                Check(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))==Error.Ok,"screenshot");
            }
            _clock.Paused=true;_developerMode=false;_creative=false;
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_world.Items.Clear();_world.Stockpiles.Clear();
            _overview.World=_world;_foundation=null;_heightSelected=false;_cutaway=false;_topDown=false;
            CreatePeople();SelectTool("select");_buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();_selectionPanel.Hide();
            SyncWorld();_terrainView.FlushMeshes();await Frame();
            int pans=0;
            foreach(var viewport in new[]{new Vector2I(960,640),new Vector2I(1280,800)})
            {
                GetWindow().ContentScaleSize=viewport;await Frame();
                foreach(float size in new[]{12f,95f,240f})foreach(float pitch in new[]{20f,37f,80f})foreach(float yaw in new[]{0f,90f,225f})
                {
                    _focus=Vector3.Zero;_camera.Size=size;_orbitPitch=Mathf.DegToRad(pitch);_cameraYaw=Mathf.DegToRad(yaw);PositionCamera();
                    var start=GetViewport().GetVisibleRect().GetCenter();var end=start+new Vector2(23,31);
                    var anchored=CameraPlanePoint(start,0);var basis=_camera.GlobalBasis;
                    _UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=true,Position=start});
                    _UnhandledInput(new InputEventMouseMotion{Position=end});
                    _UnhandledInput(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=false,Position=end});
                    Check(_camera.UnprojectPosition(anchored).DistanceTo(end)<.75f,$"pan slip {viewport}/{size}/{pitch}/{yaw}: got {_camera.UnprojectPosition(anchored)}, expected {end}");
                    Check(_camera.GlobalBasis.IsEqualApprox(basis)&&_camera.Size==size,"pan changed angle or scale");pans++;
                }
            }
            _focus=new(0,-12,0);_camera.Size=95;_cameraYaw=0;_orbitPitch=DefaultCameraPitch;PositionCamera();await Frame();
            var center=GetViewport().GetVisibleRect().GetCenter();var hit=PickObject(center,1);Check(hit.Count>0,"no centre terrain");
            var target=hit["position"].AsVector3();var sample=new Vector3(7,3,2);var before=_camera.UnprojectPosition(sample);
            AnchorCameraOrbit();Check(_camera.UnprojectPosition(sample).DistanceTo(before)<.08f,"rebasing moved scene");
            _orbitHeld=true;_orbitMoved=false;_orbitOrigin=_orbitLast=center;
            foreach(int dy in new[]{12,24,36,48})HandleMapGesture(new InputEventMouseMotion{Position=center+new Vector2(0,dy)});
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=false,Position=center+new Vector2(0,48)});
            Check(_camera.UnprojectPosition(target).DistanceTo(center)<.08f,"orbit pivot drift");
            Check(_camera.Projection==Camera3D.ProjectionType.Orthogonal,"projection switched");

            var origin=new Cell(124,124,0);RoomEnvironmentChecks.Shell(_world,origin);
            for(int x=1;x<5;x++)for(int z=1;z<5;z++)
            {var c=origin+new Cell(x,z,-1);_world.Terrain.Excavate(c);_world.Buildings[c]=new(c,BuildKind.Planks);}
            var bed=origin+new Cell(1,1);_world.Buildings[bed]=new(bed,BuildKind.Bed);
            var bench=origin+new Cell(3,1);_world.Buildings[bench]=new(bench,BuildKind.Workbench);
            _world.Navigation.Invalidate(origin);_foundation=new FoundationPlan(origin,origin+new Cell(5,5));_foundation.SetFloorHeight(3);
            _heightSelected=true;_layerOffset=0;_focus=WorldPosition(origin)+new Vector3(2.5f,0,2.5f);_camera.Size=13;
            _cameraYaw=0;_orbitPitch=DefaultCameraPitch;PositionCamera();SetViewMode(3);await Snap("wall-cutaway-full");
            int buildings=_world.Buildings.Count;var terrainRevision=_world.Terrain.Revision;
            foreach(float pitch in new[]{20f,37f,80f,90f})foreach(float yaw in new[]{0f,180f})
            {
                _topDown=pitch==90;_orbitPitch=Mathf.DegToRad(pitch);_cameraYaw=Mathf.DegToRad(yaw);PositionCamera();SetViewMode(1);
                var front=origin+(yaw==0?new Cell(5,2):new Cell(0,2));var rear=origin+(yaw==0?new Cell(0,2):new Cell(5,2));
                Check(_wallCaps is not null&&_wallCaps.TryGetValue(front,out float cap)&&Math.Abs(cap-.2f)<.001f,$"front wall not lowered {pitch}/{yaw}");
                Check(!_wallCaps!.ContainsKey(rear),$"rear wall lost {pitch}/{yaw}");
                Check(!_wallCaps.ContainsKey(bed)&&!_wallCaps.ContainsKey(bench),"furniture cut");
            }
            _topDown=false;_orbitPitch=DefaultCameraPitch;_cameraYaw=0;PositionCamera();SetViewMode(1);await Snap("wall-cutaway-front");
            _cameraYaw=Mathf.Pi;PositionCamera();SetViewMode(1);await Snap("wall-cutaway-reverse");
            SetViewMode(3);Check(_wallCaps is null,"full walls not restored");
            Check(_world.Buildings.Count==buildings&&_world.Terrain.Revision==terrainRevision,"view changed world");
            GD.Print($"CAMERA_CUTAWAY_OK: {pans} pan poses, screen-stable orbit anchor, front/rear walls through 20–90 degrees, furniture and world preserved");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
