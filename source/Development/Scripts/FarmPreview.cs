using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureFarmPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.Stockpiles.Clear();_overview.World=_world;_terrainRevision=-1;
            var origin=FarmingChecks.SurfacePlot(_world);SelectTool("growingzone");Act(origin);Act(origin+new Cell(2,1));
            if(_world.Farms.Count!=6)throw new InvalidOperationException("Two-corner farm UI failed");
            for(int i=0;i<120*30&&!_world.Farms.Values.All(p=>_world.Plants.ContainsKey(p.Cell));i++)_world.Update(1d/30);
            if(!_world.Farms.Values.All(p=>_world.Plants.ContainsKey(p.Cell)))throw new InvalidOperationException("Residents did not plant preview plots");
            _world.DeveloperResidents(false);for(int i=0;i<65*30;i++)_world.Update(1d/30);
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;
            _foundation=null;_heightSelected=false;_layerOffset=0;_cameraYaw=0;_topDown=false;_focus=WorldPosition(origin+new Cell(1,1));_camera.Size=12;
            _inspector.Hide();_selectionPanel.Hide();_worldMenu.Hide();CreatePeople();PositionCamera();SetViewMode(0);
            ShowBuildPage("区域");_buildMenu.Show();SelectTool("growingzone");SyncWorld();RefreshUi();
            async Task Snap(string name)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(_terrainView.PendingMeshCount>0)
                {
                    if(timer.Elapsed.TotalSeconds>60)throw new TimeoutException("Farm preview terrain did not finish loading");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Farm screenshot failed");
            }
            await Snap("grain-farm-preview");
            if(Pick(_camera.UnprojectPosition(WorldPosition(origin)))!=origin)throw new InvalidOperationException("Farm selection must hit its ground cell");
            if(PickTarget(_camera.UnprojectPosition(WorldPosition(origin)))!=origin)throw new InvalidOperationException("Clicking terrain must select the zone above its soil block");
            if(_world.Farms.Keys.Any(c=>!_farmNodes.ContainsKey(c))||_categoryButtons["区域"].GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Farm visuals or category layout failed");
            SelectTool("select");Act(origin);await Snap("growing-zone-settings");
            if(!_growingSettings.IsVisibleInTree()||_architectTools.IsVisibleInTree()||SelectedGrowingZone is null)throw new InvalidOperationException("Selecting a plot must open its zone settings");
            _growingSowing.ButtonPressed=false;
            if(SelectedGrowingZone.AllowSowing)throw new InvalidOperationException("Zone checkbox did not disable sowing");
            _growingCrop.EmitSignal(OptionButton.SignalName.ItemSelected,(long)Array.IndexOf(GrowingCrops,"mushroom"));
            if(SelectedGrowingZone.Crop!="mushroom"||_world.Plants.Values.Where(f=>_world.Farms.ContainsKey(f.Cell)).Any(f=>f.Crop!="grain"))throw new InvalidOperationException("Changing zone crop must preserve existing grain");
            _world.ConfigureGrowingZone(SelectedGrowingZone.Id,"grain",true);
            bool wasPaused=_clock.Paused;int plotsBefore=_world.Farms.Count,plantsBefore=_world.Plants.Count;
            SelectTool("unfarm");Act(origin);Act(origin+new Cell(2,1));
            if(_removeGrowingDialog?.Visible!=true||_world.Farms.Count!=plotsBefore||!_removeGrowingDialog.DialogText.Contains("6 格"))
                throw new InvalidOperationException("Farm removal must explain effects before changing plots");
            await Snap("growing-zone-removal");
            _removeGrowingDialog.Hide();_removeGrowingDialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            if(_world.Farms.Count!=plotsBefore||_clock.Paused!=wasPaused)throw new InvalidOperationException("Cancel farm removal changed plots or clock");
            SelectTool("unfarm");Act(origin);Act(origin+new Cell(2,1));
            _removeGrowingDialog!.Hide();_removeGrowingDialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            if(_world.Farms.Count!=plotsBefore-6||_world.Plants.Count!=plantsBefore||_clock.Paused!=wasPaused)throw new InvalidOperationException("Confirmed farm removal failed");
            SyncWorld();RefreshUi();await Snap("plants-after-zone-removal");
            if(!_world.Plants.ContainsKey(origin)||!_farmNodes.ContainsKey(origin)||_growingSettings.IsVisibleInTree())throw new InvalidOperationException("Removed zone must leave a visible independent plant");
            _world.Plants[origin].Growth=DemoWorld.GrowTime("grain");SelectTool("select");Act(origin);RefreshUi();
            if(!_harvestPlantAction.IsVisibleInTree())throw new InvalidOperationException("Unmanaged mature plant must offer harvest");
            _harvestPlantAction.EmitSignal(Godot.Button.SignalName.Pressed);
            if(!_world.Jobs.At(origin).Any(j=>j.Harvest)||!_cancelHarvestPlantAction.IsVisibleInTree())throw new InvalidOperationException("Harvest command not wired");
            _cancelHarvestPlantAction.EmitSignal(Godot.Button.SignalName.Pressed);
            if(_world.Jobs.At(origin).Any(j=>j.Harvest)||!_world.Plants.ContainsKey(origin))throw new InvalidOperationException("Cancel harvest removed plant");
            GD.Print("FARM_REMOVAL_OK: management removal, cancel and confirm both preserve plants");
            var p=new Cell(20,20,-5);
            for(int x=-1;x<=3;x++)for(int z=-1;z<=2;z++)for(int y=0;y<2;y++)_world.Terrain.Excavate(p+new Cell(x,z,y));
            _world.SetFarm(p,p+new Cell(2,1),"mushroom");_world.DeveloperResidents(true);
            foreach(var worker in _world.Workers){worker.X=p.X-1;worker.Z=p.Z;worker.Level=p.Level;}
            _world.SpawnItem("mushroom_spawn",6,p+new Cell(-1,0));_world.SpawnItem("soil",6,p+new Cell(-1,0));
            for(int i=0;i<120*30&&!_world.Farms.Values.Where(f=>f.Crop=="mushroom").All(f=>_world.Plants.ContainsKey(f.Cell));i++)_world.Update(1d/30);
            if(!_world.Farms.Values.Where(f=>f.Crop=="mushroom").All(f=>_world.Plants.ContainsKey(f.Cell)))throw new InvalidOperationException("Cave plots never planted");
            _world.DeveloperResidents(false);for(int i=0;i<90*30;i++)_world.Update(1d/30);
            _foundation=new FoundationPlan(p,p+new Cell(2,1));_heightSelected=true;_layerOffset=0;_focus=WorldPosition(p+new Cell(1,1));_camera.Size=11;
            _buildMenu.Hide();CreatePeople();PositionCamera();SetViewMode(1);SyncWorld();RefreshUi();await Snap("mushroom-farm-preview");
            GD.Print("FARM_UI_OK: two-corner tools, seed delivery, staged crop visuals, save/load and cave farms");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
