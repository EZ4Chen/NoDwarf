using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureRegionPreview()
    {
        try
        {
            _clock.Paused=true;
            async Task Snap(string name)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+name+".png"))!=Error.Ok)throw new IOException("Region screenshot");
            }
            foreach(var profile in RegionProfile.All)
            {
                _world=new DemoWorld(74219,region:profile.Id);_world.DeveloperResidents(false);
                _overview.World=_world;CreatePeople();_foundation=null;_heightSelected=false;_layerOffset=0;
                _focus=WorldPosition(_world.Surface(87,87));_camera.Size=120;_cameraYaw=0;PositionCamera();SetViewMode(0);SyncWorld();RefreshUi();
                await Snap("region-"+profile.Id);
                var cover=(from x in Enumerable.Range(1,253) from z in Enumerable.Range(1,253) let cell=_world.Surface(x,z) where GroundCover.At(_world,cell) is not null orderby Math.Abs(x-87)+Math.Abs(z-87) select cell).First();
                int count=_terrainView.VisibleCoverInstances;if(count==0)throw new InvalidOperationException("Missing ground cover: "+profile.Id);
                _focus=WorldPosition(cover);_camera.Size=18;PositionCamera();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                var screen=_camera.UnprojectPosition(WorldPosition(cover));
                if(DigPick(screen) is not Cell grassPick||grassPick.X!=cover.X||grassPick.Z!=cover.Z)throw new InvalidOperationException("Grass intercepts ground picking");
                if(!_world.DeveloperPlace(cover,BuildKind.Planks))throw new InvalidOperationException("Cover blocks construction");SyncWorld();
                if(_terrainView.VisibleCoverInstances!=count-1)throw new InvalidOperationException("Building did not hide grass");
                _world.DeveloperErase(cover);SyncWorld();if(_terrainView.VisibleCoverInstances!=count)throw new InvalidOperationException("Removed building did not restore grass");
                if(profile.Id=="savanna")_focus=WorldPosition(_world.Trees.Keys.OrderBy(t=>Math.Abs(t.X-cover.X)+Math.Abs(t.Z-cover.Z)).First());
                _camera.Size=22;PositionCamera();await Snap("ecology-"+profile.Id);
                GD.Print($"COVER_UI: {profile.Id} instances={count} ground-picking=true construction-clears=true");
                var c=_world.Camp;_focus=WorldPosition(c);_camera.Size=18;PositionCamera();
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                if(DigPick(_camera.UnprojectPosition(WorldPosition(c))) is not Cell pick||pick.X!=c.X||pick.Z!=c.Z)throw new InvalidOperationException("Regional ground picking: "+profile.Id);
            }
            // Exercise the real console -> confirmation -> map reset path.
            ExecuteCommand(new[]{"region","boreal","12345"});
            if(!_resetDialog.Visible)throw new InvalidOperationException("Region reset must ask before clearing colony");
            _resetDialog.Hide();ConfirmReset();
            if(_world.Terrain.Region?.Id!="boreal"||_world.Terrain.Seed!=12345)throw new InvalidOperationException($"Region console selection: {_world.Terrain.Region?.Id} / {_world.Terrain.Seed}");
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;CreatePeople();
            var origin=new Cell(20,20,-3);
            for(int x=0;x<5;x++)for(int z=0;z<5;z++)for(int y=0;y<2;y++)_world.Terrain.Excavate(origin+new Cell(x,z,y));
            _foundation=new FoundationPlan(origin,origin+new Cell(4,4));_layerOffset=0;_placement=0;
            _cameraYaw=0;_focus=WorldPosition(origin+new Cell(2,2));_camera.Size=14;PositionCamera();SetViewMode(1);
            await Snap("region-cutaway-before");int topology=_roomTopologyBuilds;
            foreach(float angle in new[]{45f,90f,135f,180f,225f,270f,315f})
            {
                _cameraYaw=Mathf.DegToRad(angle);PositionCamera();SyncWorld();
                if(_roomTopologyBuilds!=topology)throw new InvalidOperationException("Rotation repeated room search");
                if(_terrainView.LastRebuiltChunks>9)throw new InvalidOperationException("Rotation remeshed unrelated terrain");
                GD.Print($"CUTAWAY_CHUNKS: angle={angle} chunks={_terrainView.LastRebuiltChunks} topology-reused=true");
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                var cell=origin+new Cell(2,2);var screen=_camera.UnprojectPosition(WorldPosition(cell));
                if(PickTarget(screen) is not Cell hit||hit.X!=cell.X||hit.Z!=cell.Z)throw new InvalidOperationException("Rotated floor picking");
            }
            await Snap("region-cutaway-after");
            GD.Print("REGION_UI_OK: region previews, ground picking, console confirmation and local cutaway remeshing");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
