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
            CreatePeople();
            DirectionalDwarfView View()=>_people[0].GetNode<DirectionalDwarfView>("ModelView/DwarfSprite");
            Texture2D? Held(string slot)=>View().GetNode<Sprite3D>("Paper/"+slot+"/Held").Texture;
            var full=EquipmentArt.Texture(w.Appearance,_world.Inventory(w));
            if(full!=EquipmentArt.Texture(w.Appearance,_world.Inventory(w).Reverse()))throw new Exception("Inventory order invalidates texture cache");
            var sword=_world.Inventory(w).Single(s=>s.Item=="sword");
            _world.ActResident(new(w.Id,"unequip",ItemId:sword.Id));var unarmed=EquipmentArt.Texture(w.Appearance,_world.Inventory(w));
            SyncEquipmentVisuals();
            if(unarmed!=full||Held("main") is not null||Held("off") is null)throw new Exception("Unequip must remove held sword while preserving body portrait/shield");
            _world.ActResident(new(w.Id,"equip",ItemId:sword.Id));
            var pickaxe=_world.Inventory(w).Single(s=>s.Item=="pickaxe");
            var shield=_world.Inventory(w).Single(s=>s.Item=="shield");
            _world.ActResident(new(w.Id,"equip",ItemId:pickaxe.Id));
            SyncEquipmentVisuals();
            if(_world.Inventory(w).Any(s=>s.Slot=="off")||Held("off") is not null||!View().Wears("pickaxe")||Held("main") is null||EquipmentArt.Texture(w.Appearance,_world.Inventory(w))!=full)throw new Exception("Two-handed tool must update hands without rebuilding body portrait");
            _world.ActResident(new(w.Id,"equip",ItemId:sword.Id));_world.ActResident(new(w.Id,"equip",ItemId:shield.Id));
            var garment=_world.Inventory(w).Single(s=>s.Item=="shirt");
            var bodyBefore=View().GetNode<Sprite3D>("Paper/body").Texture;
            _world.ActResident(new(w.Id,"unequip",ItemId:garment.Id));SyncEquipmentVisuals();
            if(EquipmentArt.Texture(w.Appearance,_world.Inventory(w))==full||View().GetNode<Sprite3D>("Paper/body").Texture==bodyBefore)throw new Exception("Visible shirt removal failed to update portrait/body");
            _world.ActResident(new(w.Id,"equip",ItemId:garment.Id));SyncEquipmentVisuals();
            if(EquipmentArt.Texture(w.Appearance,_world.Inventory(w))!=full)throw new Exception("Reequipped outfit failed to reuse portrait");
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
            if(sprite.Texture!=before||sprite.Texture!=_residentButtons[0].Icon||Held("main") is not null)throw new Exception("World/roster equip synchronization");
            _world.ActResident(new(w.Id,"equip",ItemId:sword.Id));SyncEquipmentVisuals();
            if(Held("main") is null||!View().Wears("sword"))throw new Exception("Sword did not return to hand");
            for(int i=0;i<100;i++)SyncEquipmentVisuals();if(sprite.Texture!=before)throw new Exception("Idle texture stability");
            using(var image=full.GetImage())image.SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-paperdoll.png"));
            _selectionPanel.Hide();RefreshUi();
            void LegacyEquipmentScan()
            {
                var owned=_world.Items.Where(i=>i.Owner!=""&&i.Slot!="").ToLookup(i=>i.Owner);
                foreach(var worker in _world.Workers)
                {
                    var key=(worker.Appearance,string.Join(";",owned[worker.Id].OrderBy(i=>i.Slot).Select(i=>i.Slot+":"+i.Item)));
                    if(_equipmentKeys.GetValueOrDefault(worker.Id)!=key)throw new Exception("Legacy equipment scan differs");
                }
            }
            LegacyEquipmentScan();long legacyAllocated=GC.GetAllocatedBytesForCurrentThread();var legacyTimer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<1000;i++)LegacyEquipmentScan();legacyTimer.Stop();legacyAllocated=GC.GetAllocatedBytesForCurrentThread()-legacyAllocated;
            int indexBuilds=_equipmentIndexBuilds;
            long allocated=GC.GetAllocatedBytesForCurrentThread();var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<1000;i++){SyncEquipmentVisuals();}
            timer.Stop();long syncBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            if(_equipmentIndexBuilds!=indexBuilds)throw new Exception("Unrelated world changes rebuilt equipment index");
            var oldAppearance=w.Appearance;w.Appearance=oldAppearance with {HairColor="ff0000"};SyncEquipmentVisuals();
            if(_equipmentKeys[w.Id].Appearance!=w.Appearance||_equipmentIndexBuilds!=indexBuilds)throw new Exception("Appearance changes require no inventory scan");
            w.Appearance=oldAppearance;SyncEquipmentVisuals();
            var unused=_world.SpawnItem("wood",1,w.Tile);unused.Count++;SyncEquipmentVisuals();
            if(_equipmentIndexBuilds!=indexBuilds)throw new Exception("Loose stock change rebuilt equipment index");
            GD.Print($"EQUIPMENT_SYNC_MEASURE: reads=1000 index_rebuilds={_equipmentIndexBuilds-indexBuilds} ms={timer.Elapsed.TotalMilliseconds:F3} bytes={syncBytes} legacy_scan_ms={legacyTimer.Elapsed.TotalMilliseconds:F3} legacy_scan_bytes={legacyAllocated}");
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=GetViewport().GetTexture().GetImage();image.SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-lineup.png"));}
            SelectResident(0);OpenPersonPage(3);RefreshUi();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            if(DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);using var image=GetViewport().GetTexture().GetImage();image.SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-0.5.png"));}
            GD.Print("EQUIPMENT_UI_OK: all gear icons, wearable/held layers, equip/unequip, cache reuse, save/load and roster synchronization");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
