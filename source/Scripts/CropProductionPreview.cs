using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureCropProductionPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            var origin=FarmingChecks.SurfacePlot(_world);
            for(int i=0;i<3;i++)
            {
                string crop=new[]{"grain","potato","cotton"}[i];var c=origin+new Cell(i,0);
                if(!_world.SetFarm(c,c+new Cell(0,2),crop))throw new InvalidOperationException("Preview plot");
                foreach(var p in _world.Farms.Values.Where(p=>p.Crop==crop)){p.Prepared=true;p.Planted=true;p.Growth=DemoWorld.GrowTime(crop)*(p.Cell.Z-origin.Z)/2;}
            }
            var mill=origin+new Cell(-1,-1);var loom=origin+new Cell(3,-1);
            _world.Buildings[mill]=new(mill,BuildKind.Mill);_world.Buildings[loom]=new(loom,BuildKind.Loom);
            _foundation=null;_heightSelected=false;_layerOffset=0;_cameraYaw=0;_topDown=false;_focus=WorldPosition(origin+new Cell(1,1));_camera.Size=13;
            CreatePeople();PositionCamera();SetViewMode(0);SelectTool("select");Act(origin+new Cell(2,0));SyncWorld();RefreshUi();
            if(_growingCrop.ItemCount!=CropCatalog.All.Length||SelectedGrowingZone?.Crop!="cotton")throw new InvalidOperationException("Crop selector options");
            async Task Snap(string file)
            {
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(_terrainView.PendingMeshCount>0)
                {
                    if(timer.Elapsed.TotalSeconds>60)throw new TimeoutException("Crop preview terrain loading timed out");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+file+".png"))!=Error.Ok)throw new IOException("Screenshot failed");
            }
            await Snap("crop-varieties");
            _growingCrop.EmitSignal(OptionButton.SignalName.ItemSelected,(long)Array.IndexOf(GrowingCrops,"pine_sapling"));
            if(SelectedGrowingZone?.Crop!="pine_sapling"||_world.Farms[origin+new Cell(2,0)].Crop!="cotton")throw new InvalidOperationException("Changing target must retain existing cotton");
            _selectionPanel.Hide();_world.SpawnItem("cotton",4,origin);if(!_world.QueueProcessing("cloth"))throw new InvalidOperationException("Weaving order");
            _inspector.Show();RefreshUi();_billOrderScroll.ScrollVertical=10000;await Snap("crop-processing-orders");
            if(!_billRows.Values.Any(r=>r.Name.Text.Contains("织布")))throw new InvalidOperationException("Recipe name missing from orders");
            _inspector.Hide();var tree=_world.Trees.Values.OrderBy(t=>Math.Abs(t.Cell.X-origin.X)+Math.Abs(t.Cell.Z-origin.Z)).First();
            _focus=WorldPosition(tree.Cell)+Vector3.Up*3;_camera.Size=35;PositionCamera();SyncWorld();await Snap("sparse-forest");
            GD.Print("CROP_PRODUCTION_UI_OK: crop catalog, retained crops on zone change, staged textured plants, mill/loom, recipe order and sparse forest");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
