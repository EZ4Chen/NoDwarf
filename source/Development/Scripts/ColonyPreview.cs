using Godot;
using NoDwarf.Simulation;
using System.Text.Json;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureColonyPreview()
    {
        try
        {
            _world=new DemoWorld();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            _world.QueueBlueprint(_world.Camp,DemoWorld.ExampleBlueprint());_world.DeveloperFinishPlans();
            _world.SpawnItem("raw_iron",6,_world.Camp+new Cell(3,8));
            _world.SetStockpile(_world.Camp+new Cell(0,8),_world.Camp+new Cell(5,9));
            _foundation=new FoundationPlan(_world.Camp,_world.Camp+new Cell(5,4));_heightSelected=true;_layerOffset=0;
            _focus=WorldPosition(_world.Camp+new Cell(2,4));_camera.Size=17;_cameraYaw=0;PositionCamera();CreatePeople();DrawFoundation();
            async Task Snap(string name)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Screenshot failed");
            }
            SetViewMode(3);await Snap("colony-walls");
            if(!_floorWalls||_interiorView||CurrentView.Top!=ActiveLevel+2)throw new InvalidOperationException("Full floor walls must omit ceiling and upper floors");
            SetViewMode(1);await Snap("colony-cutaway");
            ToggleTopDown();await Snap("colony-topdown");
            var target=_world.Camp+new Cell(2,2);
            if(Pick(_camera.UnprojectPosition(WorldPosition(target)),1)!=target)throw new InvalidOperationException("Top-down picking must hit same floor cell");
            SyncWorld();
            var raw=_world.Items.Single(i=>i.Item=="raw_iron");
            ulong itemNode=_itemNodes[raw.Id].Root.GetInstanceId(),buildingNode=_structures.GetChild(0).GetInstanceId();
            _world.SpawnItem("raw_iron",1,raw.Cell);SyncWorld();
            if(_itemNodes[raw.Id].Root.GetInstanceId()!=itemNode||_structures.GetChild(0).GetInstanceId()!=buildingNode||_itemNodes[raw.Id].Label.Text!="7")
                throw new InvalidOperationException("Stock count change rebuilt unchanged geometry or failed label update");
            if(!_world.QueueProductionRecipe("smelt"))throw new InvalidOperationException("Preview smelting order unavailable");
            _world.DeveloperPlace(_world.Camp+new Cell(3,1),BuildKind.Stove);
            if(!_world.QueueProductionRecipe("cook"))throw new InvalidOperationException("Preview cooking order unavailable");
            _orbitPitch=Mathf.DegToRad(55);
            await SaveSessionAsync();if(!File.Exists(SavePath))throw new InvalidOperationException("Session save missing");
            _world.Items.Clear();_topDown=false;_orbitPitch=MinCameraPitch;await LoadSessionAsync();
            if(!_topDown||_world.Total("raw_iron")!=7||!_world.Jobs.Any(j=>(j.Recipe=="smelt"))||_world.Workers.Count!=0||_foundation?.WorldLevel!=_world.Camp.Level)throw new InvalidOperationException("Session restore lost world, editor or camera state");
            ToggleTopDown();_inspector.Show();RefreshUi();await Snap("colony-orders");
            if(!Mathf.IsEqualApprox(_cameraPitch,Mathf.DegToRad(55)))throw new InvalidOperationException("Session restore lost saved pitch");
            if(new SessionState().Pitch!=DefaultCameraPitch)throw new InvalidOperationException("Default camera angle changed");
            var order=_world.Jobs.Single(j=>(j.Recipe=="smelt"));
            if(!_billRows.TryGetValue(order.ProductionOrderId,out var row)||!row.Count.TooltipText.Contains("尚未启用居民"))throw new InvalidOperationException("Order list must show individual waiting reason");
            _world.RemoveProductionOrder(order.ProductionOrderId);RefreshOrders();
            if(_world.Jobs.Any(j=>(j.Recipe=="smelt"))||_world.Available("raw_iron")!=7||_billRows.ContainsKey(order.ProductionOrderId))throw new InvalidOperationException("Per-order cancel must release reservations and remove its row");
            if(_inspector.GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Order panel blocks bottom tabs");
            _inspector.Hide();_worldMenu.Show();await Snap("colony-save-menu");
            if(_worldMenu.GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Save panel blocks bottom tabs");
            GD.Print("COLONY_UI_OK: full walls, cutaway, top-down picking, stock rendering, session roundtrip and panel bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
