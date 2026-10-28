using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewDwarfIntegration()
    {
        try
        {
            _world=CombatChecks.Fixture();_overview.World=_world;_clock.Paused=true;_creative=false;_foundation=null;_cutaway=false;_interiorView=false;
            for(int i=0;i<_world.Workers.Count;i++)
            {
                var w=_world.Workers[i];w.X=60+i*2;w.Z=60;
                foreach(var name in new[]{"shirt","trousers","boots"}.Concat(i==0?new[]{"sword","shield"}:i==1?new[]{"pickaxe"}:Array.Empty<string>()))
                {
                    var item=_world.SpawnItem(name,1,w.Tile);item.Owner=w.Id;
                    if(!_world.ActResident(new(w.Id,"equip",ItemId:item.Id)).Ok)throw new Exception("Equip "+name);
                }
            }
            // Reconstruct from a real save so model creation must work for existing games too.
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;
            _focus=WorldPosition(new(62,60));_camera.Size=7;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();AnimatePeople();
            foreach(var node in _people)
            {
                var view=node.GetNode<ActorModelView>("ModelView");
                if(!view.HasModel||node.GetChild<Sprite3D>(0).Visible)throw new Exception("Model/fallback visibility");
                if(ActorModelView.Descendants(view).OfType<Skeleton3D>().Count()!=1)throw new Exception("Equipment duplicated skeleton");
            }
            var guard=_world.Workers[0];var guardView=_people[0].GetNode<ActorModelView>("ModelView");
            var rig=ActorModelView.Descendants(guardView).OfType<Skeleton3D>().Single();
            var left=ActorModelView.Descendants(rig).OfType<BoneAttachment3D>().Single(n=>n.BoneName=="slot_hand_l");
            var right=ActorModelView.Descendants(rig).OfType<BoneAttachment3D>().Single(n=>n.BoneName=="slot_hand_r");
            if(left.GetChildCount()!=1||right.GetChildCount()!=1)throw new Exception("Sword/shield missing");
            var sword=_world.Inventory(guard).Single(i=>i.Item=="sword");
            _world.ActResident(new(guard.Id,"unequip",ItemId:sword.Id));SyncEquipmentVisuals();
            if(right.GetChildCount()!=0)throw new Exception("Unequipped sword retained");
            _world.ActResident(new(guard.Id,"equip",ItemId:sword.Id));SyncEquipmentVisuals();
            if(right.GetChildCount()!=1)throw new Exception("Sword not restored");
            // Female uses the same wrapper and skeleton contract; no invented sex assignment to residents.
            var female=new ActorModelView();AddChild(female);female.Initialize(new(){Scene="res://Scenes/Characters/DwarfFemale.tscn",YawDegrees=180,Animations=new(){{"idle","Idle"},{"attack","Attack"}}});
            female.UpdateEquipment(_world.Inventory(guard));female.Present(guard,0,Vector3.Back,0);
            if(!female.HasModel||ActorModelView.Descendants(female).OfType<Skeleton3D>().Count()!=1)throw new Exception("Female rig");
            female.QueueFree();
            var player=ActorModelView.Descendants(guardView).OfType<AnimationPlayer>().Single();
            if(!player.HasAnimation("Attack")||!player.HasAnimation("Walk"))throw new Exception("Imported clips");
            guardView.Present(guard,0,Vector3.Back,0);guard.Combat.AttackWeapon="sword";guard.Combat.AttackTime=0;guardView.Present(guard,0,Vector3.Back,0);
            guard.Combat.AttackTime=.5;guardView.Present(guard,.5,Vector3.Back,0);
            if(guardView.CurrentState!="attack"||Math.Abs(player.CurrentAnimationPosition-.5)>.01)throw new Exception("Attack sample");
            var pose=rig.GetBonePoseRotation(rig.FindBone("forearm.R"));guardView.Present(guard,.5,null,0);
            if(!rig.GetBonePoseRotation(rig.FindBone("forearm.R")).IsEqualApprox(pose))throw new Exception("Paused animation");
            AnimatePeople();_selectionPanel.Hide();RefreshUi();
            for(int frame=0;frame<4;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/dwarf-in-game.png"));
            guard.Combat.AttackWeapon="";guardView.Present(guard,3,null,0); // Finish the synthetic attack sample before the live movement check.
            double previousX=guard.X,previousZ=guard.Z;
            _world.DraftResident(guard.Id,true);
            var order=_world.MilitaryMove(guard.Id,new Cell(60,64));
            if(!order.Ok)throw new Exception("Walk order: "+order.Message);
            _clock.Paused=false;
            await ToSignal(GetTree().CreateTimer(.6),SceneTreeTimer.SignalName.Timeout);
            if(Math.Abs(guard.X-previousX)+Math.Abs(guard.Z-previousZ)<.01||guardView.CurrentState!="walk")throw new Exception("Actual walking/model state");
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/dwarf-in-game-walk.png"));
            _clock.Paused=true;
            GD.Print("DWARF_INTEGRATION_OK: actual male/female rigs, single shared skeleton, sword/shield equip/unequip, loaded residents, attack timing, pause, fallback visibility");
            if(!OS.GetCmdlineUserArgs().Contains("--keep-dwarf-preview"))GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
