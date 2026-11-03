using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureInteriorPreview()
    {
        try
        {
            _camera.Size=95;SetViewMode(0);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/terrain-overview-preview.png"))!=Error.Ok)
                throw new IOException("Terrain preview save failed");
            StartUndergroundDemo();_world.DeveloperFinishPlans();_camera.Size=14;
            foreach(var mode in new[]{1,2,0})
            {
                SetViewMode(mode);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var path=ProjectSettings.GlobalizePath($"res://.local/interior-mode-{mode}.png");
                if(GetViewport().GetTexture().GetImage().SavePng(path)!=Error.Ok)throw new IOException("Preview save failed");
            }
            foreach(float yaw in new[]{90f,180f,270f})
            {
                _cameraYaw=Mathf.DegToRad(yaw);PositionCamera();SetViewMode(1);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/interior-angle-{yaw}.png"))!=Error.Ok)
                    throw new IOException("Rotated preview save failed");
            }
            var mineral=_world.Terrain.Ores.Keys.First(c=>c.Level<-32&&c.X>10&&c.Z>10&&c.X<TerrainMap.Size-10&&c.Z<TerrainMap.Size-10);
            var origin=mineral+new Cell(1,-2);
            for(int x=0;x<5;x++)for(int z=0;z<5;z++)for(int y=0;y<2;y++)
                _world.Terrain.Excavate(origin+new Cell(x,z,y));
            _foundation=new FoundationPlan(origin,origin+new Cell(4,4));_layerOffset=0;_heightSelected=true;
            _cameraYaw=0;_focus=WorldPosition(origin+new Cell(2,2));PositionCamera();SetViewMode(1);
            await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/deep-vein-preview.png"))!=Error.Ok)
                throw new IOException("Deep ore preview save failed");
            GD.Print("INTERIOR_PREVIEW_OK");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private void CheckInteriorReview()
    {
        _world=new DemoWorld(flat:true);_overview.World=_world;CreatePeople();
        var origin=new Cell(10,10,-3);
        for(int x=0;x<20;x++)for(int z=0;z<30;z++)for(int y=0;y<2;y++)
            _world.Terrain.Excavate(origin+new Cell(x,z,y));
        _foundation=new FoundationPlan(origin,origin+new Cell(19,29));_layerOffset=0;_placement=0;
        _interiorView=true;_cutaway=true;PrepareInteriorWalls();
        if(_wallCaps!.Count==0)throw new InvalidOperationException("Large interior must retain front-wall cutaway");
        int stamp=_viewStamp;PrepareInteriorWalls();
        if(_viewStamp!=stamp)throw new InvalidOperationException("Unchanged view must not rebuild terrain");
        _world=new DemoWorld(flat:true);_overview.World=_world;CreatePeople();
        origin=new Cell(20,20,-3);
        for(int x=0;x<3;x++)for(int z=0;z<3;z++)for(int y=0;y<2;y++)
            _world.Terrain.Excavate(origin+new Cell(x,z,y));
        _foundation=new FoundationPlan(origin,origin+new Cell(2,2));_cameraYaw=0;
        PrepareInteriorWalls();
        if(!_wallCaps!.Any(p=>Math.Abs(p.Value-(origin.Level+.2f))<.001f))throw new InvalidOperationException("Cutaway must retain a low wall base at the room boundary");
        var hiddenForeground=_wallCaps!.Keys.Where(c=>!CurrentView.Known(_world,c)&&!CurrentView.CornerRim(_world,c)).ToArray();
        if(hiddenForeground.Length==0)throw new InvalidOperationException("Camera obstruction must include unknown foreground beyond room wall");
        if(hiddenForeground.Any(c=>!_world.NaturalSolid(c)||CurrentView.Known(_world,c)))throw new InvalidOperationException("Hiding foreground must not excavate or discover it");
        _interiorView=false;PrepareInteriorWalls();
        GD.Print("FOREGROUND_OK: unknown foreground is hidden without discovery or terrain changes");
        GD.Print("REVIEW_OK: large-room traversal and stable view cache");
    }
}
