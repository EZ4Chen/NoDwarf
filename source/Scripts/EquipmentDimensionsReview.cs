using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewEquipmentDimensions()
    {
        try
        {
            EquipmentDimensionsChecks.Run();
            _world=CombatChecks.Fixture();_overview.World=_world;_clock.Paused=true;_cutaway=false;_interiorView=false;_foundation=null;
            var actor=_world.Workers[0];
            foreach(var name in new[]{"shirt","trousers","boots","sword","shield"})
            {var item=_world.SpawnItem(name,1,actor.Tile);item.Owner=actor.Id;item.Slot=ResidentGear.Get(name)!.Slot;}
            _focus=WorldPosition(actor.Tile);_camera.Size=4;PositionCamera();SyncWorld();_terrainView.FlushMeshes();
            var holder=new Node3D{Position=WorldPosition(actor.Tile)};AddChild(holder);
            // This verifies the retained 3D collision-authoring rig, independently of the live sprite renderer.
            var config=ActorModelView.LoadLegacyDefinition();
            var view=new ActorModelView();holder.AddChild(view);view.Initialize(new(){Scene=config.Scene,Scale=config.Scale,YawDegrees=config.YawDegrees,Animations=config.Animations});view.UpdateEquipment(_world.Inventory(actor));
            var rig=ActorModelView.Descendants(view).OfType<Skeleton3D>().Single();
            var right=ActorModelView.Descendants(rig).OfType<BoneAttachment3D>().Single(b=>b.BoneName=="slot_hand_r");
            var left=ActorModelView.Descendants(rig).OfType<BoneAttachment3D>().Single(b=>b.BoneName=="slot_hand_l");
            static Vector3 G(System.Numerics.Vector3 v)=>new(v.X,v.Y,v.Z);
            static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
            async Task Frame(){for(int i=0;i<2;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            void CheckReferenceBounds(Node3D item,EquipmentDimensions geometry)
            {
                Vector3 min=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=new(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
                foreach(var mesh in ActorModelView.Descendants(item).OfType<MeshInstance3D>())
                {
                    var transform=item.GlobalTransform.AffineInverse()*mesh.GlobalTransform;var bounds=mesh.GetAabb();
                    for(int corner=0;corner<8;corner++){var point=transform*bounds.GetEndpoint(corner);min=min.Min(point);max=max.Max(point);}
                }
                if(min.DistanceTo(V(geometry.ReferenceMin))>.002f||max.DistanceTo(V(geometry.ReferenceMax))>.002f)
                    throw new Exception("Authored mesh bounds changed; recalibrate equipment definition");
            }
            var sword=MeleeProfiles.Weapon("sword");var normal=sword.Geometry;
            CheckReferenceBounds(right.GetChild<Node3D>(0),normal);
            CheckReferenceBounds(left.GetChild<Node3D>(0),EquipmentPhysicalCatalog.Get("shield"));
            float maxError=0;
            foreach(float lengthScale in new[]{1f,1.8f})
            {
                var dimensions=normal with{Size=[normal.Size[0],normal.Size[1],normal.Size[2]*lengthScale]};
                view.ApplyEquipmentDimensions("sword",dimensions);var weapon=sword with{Geometry=dimensions};
                for(int sample=0;sample<=10;sample++)
                {
                    actor.Combat.AttackWeapon="sword";actor.Combat.AttackTime=sample*.1;
                    view.Present(actor,0,Vector3.Back,0);await Frame();
                    var swordNode=right.GetChild<Node3D>(0);
                    var actual=rig.GlobalTransform.AffineInverse()*(swordNode.GlobalTransform*V(normal.ContactEnd));
                    var expected=G(MeleeProfiles.Blade(weapon,actor.Combat.AttackTime).B);
                    float error=actual.DistanceTo(expected);maxError=Math.Max(maxError,error);
                    if(error>.008f)throw new Exception($"Rendered sword and sweep differ: {error}m at {sample}, scale {lengthScale}");
                }
            }
            view.ApplyEquipmentDimensions("sword",normal);
            actor.Combat.AttackWeapon="";actor.Combat.Defense="block";actor.Combat.DefenseTime=.3;
            view.Present(actor,0,Vector3.Back,0);await Frame();
            var shield=EquipmentPhysicalCatalog.Get("shield");var shieldNode=left.GetChild<Node3D>(0);
            foreach(float sizeScale in new[]{1f,1.5f})
            {
                var geometry=shield with{Size=shield.Size.Select(v=>v*sizeScale).ToArray()};view.ApplyEquipmentDimensions("shield",geometry);await Frame();
                for(int mask=0;mask<8;mask++)
                {
                    var corner=new float[3];for(int i=0;i<3;i++)corner[i]=(mask&(1<<i))==0?shield.ReferenceMin[i]:shield.ReferenceMax[i];
                    var actual=rig.GlobalTransform.AffineInverse()*(shieldNode.GlobalTransform*V(corner));
                    var expected=G(CombatGripPoses.Guard(true).Transform(EquipmentDimensions.V(corner)*geometry.Scale));
                    float error=actual.DistanceTo(expected);maxError=Math.Max(maxError,error);
                    if(error>.008f)throw new Exception("Shield rendered bounds differ from collision: "+error);
                }
            }
            view.ApplyEquipmentDimensions("shield",shield);await Frame();
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/equipment-dimensions-guard.png"));
            GD.Print($"EQUIPMENT_DIMENSIONS_UI_OK: 22 animated sword poses at two lengths, 16 shield corners at two sizes; max error {maxError:F6} m");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
