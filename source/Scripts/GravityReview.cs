using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewGravity()
    {
        try
        {
            BuildingGravityChecks.Run();
            _world=new DemoWorld(flat:true){FallBreakChance=0};_world.DeveloperResidents(false);_overview.World=_world;
            _clock.Paused=true;_creative=true;_foundation=null;_cutaway=false;_interiorView=false;
            var origin=new Cell(127,127);
            for(int y=0;y<3;y++)_world.DeveloperPlace(origin+new Cell(0,0,y),BuildKind.StoneBricks);
            for(int x=1;x<5;x++)_world.DeveloperPlace(origin+new Cell(x,0,2),BuildKind.Planks);
            while(_world.SupportPending)_world.AdvanceSupport();
            CreatePeople();_focus=WorldPosition(origin);_camera.Size=16;PositionCamera();SyncWorld();_terrainView.FlushMeshes();RefreshUi();
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/gravity-before.png"));
            _world.DeveloperErase(origin);
            for(int i=0;i<120&&_world.SupportPending;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(_world.SupportPending||!_world.Buildings.ContainsKey(origin+new Cell(4,0,1)))throw new InvalidOperationException("Paused developer gravity did not settle");
            SyncWorld();_terrainView.FlushMeshes();RefreshUi();
            if(_fallVisuals.Count==0)throw new InvalidOperationException("Missing fall animation");
            AnimateBuildingFalls(.08);
            if(!_fallVisuals.Any(f=>f.Root.Position.Y<0&&f.Root.Position.Y>-f.Distance))throw new InvalidOperationException("Fall must interpolate vertically");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/gravity-mid.png"));
            AnimateBuildingFalls(1);
            if(_fallVisuals.Count!=0||_buildingVisuals.Values.Any(b=>!b.Root.Visible))throw new InvalidOperationException("Landing must restore final visuals");
            _world.FallBreakChance=1;_world.DeveloperErase(origin);
            while(_world.SupportPending)_world.AdvanceSupport();
            SyncWorld();AnimateBuildingFalls(0);
            if(_fallVisuals.Count==0||_world.Buildings.Count!=0)throw new InvalidOperationException("Debris landing/animation missing");
            foreach(var f in _fallVisuals)foreach(var id in f.ItemIds)
                if(_itemNodes.TryGetValue(id,out var node)&&node.Root.Visible)throw new InvalidOperationException("Debris visible before impact");
            AnimateBuildingFalls(1);
            if(_fallHidden.Count!=0||_fallVisuals.Count!=0)throw new InvalidOperationException("Animation leaked state");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/gravity-after.png"));
            _world.FallBreakChance=0;
            for(int x=100;x<120;x++)for(int z=100;z<120;z++)
            {var c=new Cell(x,z,4);_world.Buildings[c]=new(c,BuildKind.Planks);}
            SyncWorld();
            while(_world.SupportPending)_world.AdvanceSupport();
            SyncWorld();AnimateBuildingFalls(0);
            var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int frame=0;frame<100;frame++)AnimateBuildingFalls(.001);
            double animationMs=timer.Elapsed.TotalMilliseconds/100;
            if(_fallVisuals.Count!=1||_fallVisuals[0].Targets.Count!=400)throw new InvalidOperationException("Batch animation should share one transform");
            AnimateBuildingFalls(1);
            GD.Print($"FALL_ANIMATION_OK: intermediate position, impact debris visibility, cleanup, 400 blocks / one transform / {animationMs:F3} ms CPU animation update");
            GD.Print("GRAVITY_UI_OK: paused developer demolition, connected group landing, refreshed geometry");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
