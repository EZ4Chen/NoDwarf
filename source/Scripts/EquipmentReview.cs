using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewEquipment()
    {
        try
        {
            _world=CombatChecks.Fixture();_overview.World=_world;_clock.Paused=true;_creative=false;_foundation=null;_cutaway=false;_interiorView=false;
            var w=_world.Workers[0];w.X=60;w.Z=60;
            foreach(var definition in ResidentGear.All)
            {
                if(EquipmentArt.Icon(definition.Item) is null)throw new Exception("Missing equipment icon: "+definition.Item);
                var stack=_world.SpawnItem(definition.Item,1,w.Tile);stack.Owner=w.Id;
                if(definition.Slot!="main"||definition.Item=="sword")
                    if(!_world.ActResident(new(w.Id,"equip",ItemId:stack.Id)).Ok)throw new Exception("Equip "+definition.Item);
            }
            var full=EquipmentArt.Texture(w.Appearance,_world.Inventory(w));
            if(full!=EquipmentArt.Texture(w.Appearance,_world.Inventory(w).Reverse()))throw new Exception("Inventory order invalidates texture cache");
            var sword=_world.Inventory(w).Single(s=>s.Item=="sword");
            _world.ActResident(new(w.Id,"unequip",ItemId:sword.Id));var unarmed=EquipmentArt.Texture(w.Appearance,_world.Inventory(w));
            if(unarmed.GetImage().GetData().SequenceEqual(full.GetImage().GetData()))throw new Exception("Unequip did not remove sword");
            _world.ActResident(new(w.Id,"equip",ItemId:sword.Id));
            var pickaxe=_world.Inventory(w).Single(s=>s.Item=="pickaxe");
            var shield=_world.Inventory(w).Single(s=>s.Item=="shield");
            _world.ActResident(new(w.Id,"equip",ItemId:pickaxe.Id));
            if(_world.Inventory(w).Any(s=>s.Slot=="off")||EquipmentArt.Texture(w.Appearance,_world.Inventory(w))==full)throw new Exception("Two-handed tool did not remove shield appearance");
            _world.ActResident(new(w.Id,"equip",ItemId:sword.Id));_world.ActResident(new(w.Id,"equip",ItemId:shield.Id));
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;w=_world.Workers[0];
            if(EquipmentArt.Texture(w.Appearance,_world.Inventory(w))!=full)throw new Exception("Loaded outfit differs");
            // Show sword/shield, two-handed pickaxe, and hammer side by side.
            for(int i=1;i<3;i++)
            {
                var worker=_world.Workers[i];worker.X=60+i*3;worker.Z=60;
                var item=_world.SpawnItem(i==1?"pickaxe":"hammer",1,worker.Tile);item.Owner=worker.Id;
                _world.ActResident(new(worker.Id,"equip",ItemId:item.Id));
            }
            _focus=WorldPosition(new(63,60));_camera.Size=9;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();AnimatePeople();
            var sprite=_people[0].GetChild<Sprite3D>(0);var before=sprite.Texture;
            _world.ActResident(new(w.Id,"unequip",ItemId:sword.Id));SyncEquipmentVisuals();
            if(sprite.Texture==before||sprite.Texture!=_residentButtons[0].Icon)throw new Exception("World/roster equip synchronization");
            _world.ActResident(new(w.Id,"equip",ItemId:sword.Id));SyncEquipmentVisuals();
            for(int i=0;i<100;i++)SyncEquipmentVisuals();if(sprite.Texture!=before)throw new Exception("Idle texture stability");
            full.GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-paperdoll.png"));
            _selectionPanel.Hide();RefreshUi();
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-lineup.png"));
            SelectResident(0);OpenPersonPage(3);RefreshUi();
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-0.5.png"));
            GD.Print("EQUIPMENT_UI_OK: all gear icons, wearable/held layers, equip/unequip, cache reuse, save/load and roster synchronization");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
