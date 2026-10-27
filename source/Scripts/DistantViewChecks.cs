using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void CheckDistantView()
    {
        try
        {
            _clock.Paused=true;
            foreach(var id in new[]{"nodwarf:stone","nodwarf:grass","nodwarf:stone_wall","nodwarf:planks"})
            {
                using var image=BlockTextures.Material(id,true).AlbedoTexture?.GetImage();
                if(image is null||!image.HasMipmaps())throw new InvalidOperationException("Distant texture has no mip levels: "+id);
            }
            // The old fixed 63-unit distance cut through the map when zoomed out.
            // Exercise map corners, underground focus, all orbit quadrants and top-down.
            foreach(float size in new[]{12f,95f,240f})
            foreach(float pitch in new[]{20f,80f,90f})
            foreach(float yaw in new[]{0f,90f,180f,270f})
            foreach(var focus in new[]{Vector3.Zero,new Vector3(127,127,127),new Vector3(-127,-64,-127)})
            {
                _camera.Size=size;_focus=focus;_cameraYaw=Mathf.DegToRad(yaw);
                _topDown=pitch==90;_orbitPitch=Mathf.DegToRad(pitch);PositionCamera();
                foreach(float x in new[]{-127.5f,127.5f})foreach(float z in new[]{-127.5f,127.5f})
                foreach(float y in new[]{(float)TerrainMap.MinLevel,TerrainMap.MaxLevel+16f})
                {
                    var local=_camera.GlobalTransform.AffineInverse()*new Vector3(x,y,z);
                    if(-local.Z<=_camera.Near||-local.Z>=_camera.Far)
                        throw new InvalidOperationException($"World clipped: size {size}, pitch {pitch}, yaw {yaw}, focus {focus}");
                }
            }
            var scenario=new RealtimeStressScenario();_world=scenario.World;
            _overview.World=_world;_overview.RefreshMap();_terrainRevision=-1;_developerMode=false;_creative=false;
            _foundation=new FoundationPlan(scenario.ViewCenter+new Cell(-32,-25),scenario.ViewCenter+new Cell(30,35));
            _layerOffset=0;_placement=0;_cutaway=true;_interiorView=false;_topDown=false;_orbitPitch=DefaultCameraPitch;
            _focus=WorldPosition(scenario.ViewCenter);_cameraYaw=0;_camera.Size=240;PositionCamera();
            CreatePeople();DrawFoundation();SyncWorld();_terrainView.FlushMeshes();RefreshUi();
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Snap(string name)
            {
                await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(DisplayServer.GetName()!="headless"&&GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)
                    throw new IOException("Distant view screenshot failed");
            }
            await Snap("distant-view-wide");
            _orbitPitch=Mathf.DegToRad(20);PositionCamera();await Snap("distant-view-shallow");
            // Actual physics picking must cover the new camera depth, near and far.
            var target=_world.Surface(110,80);_focus=WorldPosition(target);_orbitPitch=DefaultCameraPitch;
            foreach(float size in new[]{12f,95f,240f})
            {
                _camera.Size=size;PositionCamera();await Frame();
                var picked=Pick(_camera.UnprojectPosition(WorldPosition(target)),1);
                if(picked!=target)throw new InvalidOperationException($"Distant ground pick failed at {size}: {picked}, expected {target}");
            }
            _camera.Size=22;PositionCamera();await Snap("distant-view-close");
            _world=new DemoWorld(region:"grassland");_overview.World=_world;_overview.RefreshMap();
            _foundation=null;_heightSelected=false;_cutaway=false;_layerOffset=0;_terrainRevision=-1;
            _camera.Size=240;FocusCamp();CreatePeople();DrawFoundation();SyncWorld();_terrainView.FlushMeshes();RefreshUi();
            await Snap("distant-view-surface");
            GD.Print("DISTANT_VIEW_OK: actual mip levels, world depth bounds, wide/shallow/close views and physics picking");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
