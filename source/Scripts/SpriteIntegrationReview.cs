using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewSpriteIntegration()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            Check(ActorModelView.LoadDefinition().Renderer=="sprite4","Default renderer must use current 2D sprites");
            FlatPawnAppearanceChecks.Run();
            ToolEquipmentChecks.Run();
            ProductionOrderChecks.Run();
            ResidentChecks.Run();
            MeleeChecks.Run();
            _world=CombatChecks.Fixture();_overview.World=_world;_clock.Paused=true;_creative=false;_foundation=null;_cutaway=false;_interiorView=false;
            for(int i=0;i<_world.Workers.Count;i++)
            {
                var actor=_world.Workers[i];actor.X=60+i*2;actor.Z=60;actor.Appearance=actor.Appearance with{BodySex=i==1?"female":"male",Body=i==0?"broad":i==1?"regular":"round",Face=i==1?"round":"square",Hair=i==1?"braid":"sweep",Beard=i==0?"full":"none",Outfit="tunic"};
                foreach(var name in (i==2?Array.Empty<string>():new[]{"shirt","trousers","boots"}.Concat(i==0?new[]{"sword","shield"}:new[]{"pickaxe"})))
                {
                    var item=_world.SpawnItem(name,1,actor.Tile);item.Owner=actor.Id;
                    Check(_world.ActResident(new(actor.Id,"equip",ItemId:item.Id)).Ok,"Equip "+name);
                }
            }
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;
            Check(_world.Workers[1].Appearance.BodySex=="female","Female save roundtrip");
            Check(System.Text.Json.JsonSerializer.Deserialize<CharacterAppearance>("{}")!.BodySex=="male","Old appearance compatibility");
            _focus=WorldPosition(new(62,60));_camera.Size=7;PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();AnimatePeople();
            foreach(var node in _people)
            {
                var view=node.GetNode<ActorModelView>("ModelView");
                Check(view.HasModel&&!node.GetChild<Sprite3D>(0).Visible,"New renderer visibility");
                Check(!ActorModelView.Descendants(view).Any(n=>n is Skeleton3D or AnimationPlayer or MeshInstance3D),"3D actor resource instantiated");
            }
            var guard=_world.Workers[0];var guardView=_people[0].GetNode<ActorModelView>("ModelView");var sprite=guardView.GetNode<DirectionalDwarfView>("DwarfSprite");
            // Current hand art and icon fallback, independent of retained 3D rigs.
            foreach(var id in ToolEquipmentChecks.Items.Prepend("sword").Append("pickaxe").Append("shield"))
            {
                string slot=id=="shield"?"off":"main";
                sprite.SetEquipment([new ItemStack{Item=id,Slot=slot,Count=1}]);
                Check(sprite.GetNode<Sprite3D>("Paper/"+slot+"/Held").Texture is not null,"Missing current held art: "+id);
                Check(EquipmentArt.Icon(id) is not null,"Missing inventory icon: "+id);
            }
            sprite.SetEquipment(_world.Inventory(guard));
            Check(sprite.Wears("sword")&&sprite.Wears("shield"),"Main/off equipment");
            var sword=_world.Inventory(guard).Single(i=>i.Item=="sword");
            _world.ActResident(new(guard.Id,"unequip",ItemId:sword.Id));SyncEquipmentVisuals();Check(!sprite.Wears("sword")&&sprite.Wears("shield"),"Unequip");
            _world.ActResident(new(guard.Id,"equip",ItemId:sword.Id));SyncEquipmentVisuals();Check(sprite.Wears("sword"),"Reequip");
            foreach(float angle in new[]{0f,.6f,Mathf.Pi/2,Mathf.Pi})
            {
                var basis=new Basis(Vector3.Up,angle);
                foreach(var (v,d) in new[]{(Vector3.Back,0),(Vector3.Right,1),(Vector3.Forward,2),(Vector3.Left,3)})
                    Check(DirectionalDwarfView.SelectDirection(basis*v,basis)==d,"Camera-relative facing");
            }
            guard.Combat.AttackWeapon="sword";guard.Combat.AttackTime=.25;guardView.Present(guard,.25,Vector3.Back,0);
            var paper=sprite.GetNode<Node3D>("Paper");var pose=paper.Transform;var hand=paper.GetNode<Node3D>("main").Transform;
            guardView.Present(guard,.25,null,0);Check(paper.Transform==pose&&paper.GetNode<Node3D>("main").Transform==hand,"Pause pose drift");
            guard.Combat.AttackTime=.5;guardView.Present(guard,.5,null,0);Check(paper.GetNode<Node3D>("main").Transform!=hand,"Attack static");
            // Normal movement is a deterministic body bob; hands only appear during actions.
            var front=_camera.GlobalBasis.Z;front.Y=0;front=front.Normalized();
            sprite.Sample(.1,"walk",front);
            var bodyNode=paper.GetNode<Sprite3D>("body");
            var bodyPose=bodyNode.Transform;
            Check(!paper.GetNode<Node3D>("main").Visible&&!paper.GetNode<Node3D>("off").Visible,"Walking must hide hands");
            Check(!paper.HasNode("mainFoot")&&!paper.HasNode("offFoot"),"Flat pawns must not render feet");
            sprite.Sample(.1,"walk",front);
            Check(bodyPose==bodyNode.Transform,"Paused body drift");
            sprite.Sample(.3,"walk",front);
            Check(bodyPose!=bodyNode.Transform,"Body walking animation is static");
            sprite.SetEquipment([new ItemStack{Item="pickaxe",Slot="main",Count=1}]);
            foreach(double t in new[]{.1,.55,.95,1.2})
            {
                sprite.Sample(t,"work",front);
                Check(paper.GetNode<Node3D>("main").Visible&&paper.GetNode<Node3D>("off").Visible,"Mining must show both hands");
                var main=paper.GetNode<Node3D>("main");var off=paper.GetNode<Node3D>("off");
                var upper=main.GetNode<Sprite3D>("Palm");var lower=off.GetNode<Sprite3D>("Palm");
                Check(Mathf.Abs(upper.Position.Y-104*DirectionalDwarfView.Pixel)<.0001f,"Upper pick grip moved");
                Check(main.Basis.IsEqualApprox(off.Basis),"Mining palms rotate independently of handle");
                Check(new Vector2(main.Position.X,main.Position.Y).DistanceTo(new(lower.GetParent<Node3D>().Position.X,lower.GetParent<Node3D>().Position.Y))<.0001f,"Lower pick grip moved");
            }
            sprite.SetEquipment(_world.Inventory(guard));
            Check(new CharacterAppearance{Body="slim"}.Normalize().Body=="thin","Legacy thin body migration");
            Check(new CharacterAppearance{Body="broad"}.Normalize().Body=="hulk","Legacy broad body migration");
            Check(new CharacterAppearance{Body="round"}.Normalize().Body=="fat","Legacy round body migration");
            Check(new CharacterAppearance{Body="regular",BodySex="female"}.Normalize().Body=="female","Legacy female body migration");
            foreach(string body in CharacterAppearance.Bodies)
                Check(System.Text.Json.JsonSerializer.Deserialize<CharacterAppearance>(System.Text.Json.JsonSerializer.Serialize(new CharacterAppearance{Body=body}))!.Normalize().Body==body,"Five-body roundtrip: "+body);
            guard.Combat.AttackWeapon="";AnimatePeople();_selectionPanel.Hide();_inspector.Hide();RefreshUi();
            for(int frame=0;frame<4;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            // The dummy headless renderer does not emit a drawable frame. Keep
            // logic/scene regression checks usable without opening a window.
            if(DisplayServer.GetName()!="headless")
            {
                RenderingServer.ForceDraw();
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/sprites-in-game.png"));
            }
            double x=guard.X,z=guard.Z;_world.DraftResident(guard.Id,true);Check(_world.MilitaryMove(guard.Id,new Cell(60,64)).Ok,"Walk order");_clock.Paused=false;
            bool walked=false;
            // Observe a moving frame instead of assuming a fixed wall-clock delay
            // lands between path start and arrival on both headless and GUI runs.
            for(int frame=0;frame<180&&!walked;frame++)
            {
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                walked=Math.Abs(guard.X-x)+Math.Abs(guard.Z-z)>.01&&guardView.CurrentState=="walk";
            }
            _clock.Paused=true;
            Check(walked,$"Real walking/state: position {guard.X:F2},{guard.Z:F2}; state {guardView.CurrentState}; path {guard.Path.Count}");
            GD.Print("SPRITE_INTEGRATION_OK: five body types, legacy migration, no 3D rigs/feet, equip/unequip, camera-relative directions, attack/pause, action-only hands, mining grips, actual movement");
            if(!OS.GetCmdlineUserArgs().Contains("--keep-dwarf-preview"))GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
