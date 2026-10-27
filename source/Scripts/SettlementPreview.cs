using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureSettlementPreview()
    {
        try
        {
            _clock.Paused=true;_world=new DemoWorld();_world.Stockpiles.Clear();_overview.World=_world;_terrainRevision=-1;
            var origin=_world.Camp;
            for(int x=0;x<=6;x++)for(int z=0;z<=5;z++)
            {
                var c=origin+new Cell(x,z);
                if(x==0||z==0||x==6||z==5)_world.Buildings[c]=new(c,x==3&&z==5?BuildKind.Door:BuildKind.Planks);
            }
            foreach(var (offset,kind) in new[]{(new Cell(1,1),BuildKind.Stove),(new Cell(3,1),BuildKind.Furnace),(new Cell(5,1),BuildKind.Anvil),(new Cell(5,3),BuildKind.Bed),(new Cell(1,3),BuildKind.Campfire)})
                _world.Buildings[origin+offset]=new(origin+offset,kind);
            var fire=origin+new Cell(1,3);_world.FireFuel[fire]=200;
            _world.DeveloperResidents(false);CreatePeople();_foundation=new FoundationPlan(origin,origin+new Cell(6,5));_heightSelected=true;_layerOffset=0;
            _focus=WorldPosition(origin+new Cell(3,2));_camera.Size=14;_cameraYaw=0;_topDown=false;PositionCamera();SetViewMode(1);SyncWorld();RefreshUi();
            async Task Snap(string name)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Settlement screenshot failed");
            }
            await Snap("settlement-facilities");
            var flame=_buildingVisuals[fire].Root.FindChild("Flame",true,false) as Node3D;
            if(flame is null||!flame.Visible)throw new InvalidOperationException("Lit fire not visible");
            _world.FireFuel.Clear();SyncWorld();if(flame.Visible)throw new InvalidOperationException("Extinguished fire still visible");
            SelectTool("stockpile");Act(origin+new Cell(2,3));Act(origin+new Cell(3,4));
            if(_storagePanel is null||_world.StorageZones.Count!=1)throw new InvalidOperationException("Two-corner storage UI missing");
            var panel=_storagePanel;var column=panel.GetChild<VBoxContainer>(0);
            column.GetChildren().OfType<OptionButton>().Single().Selected=4;
            var boxes=column.GetChildren().OfType<ScrollContainer>().Single().GetChild<VBoxContainer>(0).GetChildren().OfType<CheckBox>().ToArray();
            foreach(var box in boxes)box.ButtonPressed=box.Text=="木料";
            await Snap("settlement-storage");
            column.GetChildren().OfType<HBoxContainer>().Last().GetChild<Button>(0).EmitSignal(Godot.Button.SignalName.Pressed);
            var zone=_world.StorageZones.Single();if(zone.Priority!=5||!zone.Allowed.SetEquals(new[]{"wood"}))throw new InvalidOperationException("Storage apply did not persist selected filters");
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;_terrainRevision=-1;
            _world.DeveloperResidents(false);CreatePeople();
            var tree=_world.Trees.Values.OrderBy(t=>Math.Abs(t.Cell.X-origin.X)+Math.Abs(t.Cell.Z-origin.Z)).First();
            _focus=WorldPosition(tree.Cell)+Vector3.Up*4;_camera.Size=28;_foundation=null;_heightSelected=false;_layerOffset=0;PositionCamera();SetViewMode(0);SyncWorld();RefreshUi();
            ShowBuildPage("区域");_buildMenu.Show();await Snap("settlement-tall-trees");
            SelectTool("chop");Act(tree.Cell);if(!_world.Jobs.Any(j=>j.Chop&&j.Cell==tree.Cell))throw new InvalidOperationException("Felling UI did not create a task");
            GD.Print("SETTLEMENT_UI_OK: facilities, fire visibility, two-corner storage, five-priority filter controls, save/load, tall trees and felling tool");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
