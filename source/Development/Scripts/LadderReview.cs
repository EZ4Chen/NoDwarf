using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewLadders()
    {
        try
        {
            LadderChecks.Run();
            _world=LadderChecks.Fixture();_overview.World=_world;_clock.Paused=true;_cutaway=false;_interiorView=false;_foundation=null;
            for(int y=0;y<4;y++)_world.DeveloperPlace(new(60,60,y),BuildKind.Ladder);
            var worker=new DemoWorker{Id="ladder-view",Name="攀爬测试",X=60,Z=60};_world.Workers.Add(worker);
            foreach(var c in _world.FindPath(worker.Tile,new(61,60,4))!)worker.Path.Enqueue(c);
            _focus=WorldPosition(new(60,60,1));_camera.Size=9;_cameraYaw=Mathf.Pi;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();
            if(!_toolButtons.ContainsKey(nameof(BuildKind.Ladder))||_toolButtons[nameof(BuildKind.Ladder)].Icon is null)throw new Exception("Ladder menu/icon missing");
            _toolButtons[nameof(BuildKind.Ladder)].EmitSignal(Godot.Button.SignalName.Pressed);
            if(_tool!=nameof(BuildKind.Ladder))throw new Exception("Ladder tool callback");SelectTool("select");
            var mat=BlockTextures.Material("nodwarf:ladder");if(mat.AlbedoTexture is null||mat.Transparency!=BaseMaterial3D.TransparencyEnum.AlphaScissor)throw new Exception("Ladder texture/cutout missing");
            for(int i=0;i<15;i++)_world.Update(1d/30);
            if(worker.ClimbProgress<=0||worker.ClimbProgress>=1)throw new Exception("Ladder movement not animated");AnimatePeople();RefreshUi();
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/ladder-climbing.png"));
            for(int i=0;i<300;i++)_world.Update(1d/30);
            if(worker.Tile!=new Cell(61,60,4))throw new Exception("Cannot exit onto upper floor");
            GD.Print("LADDER_UI_OK: textured cutout, toolbar/icon, vertical animation and top landing");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
