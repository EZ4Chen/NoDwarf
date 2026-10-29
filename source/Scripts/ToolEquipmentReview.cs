using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void ReviewToolEquipment()
    {
        try
        {
            _clock.Paused=true;
            ToolEquipmentChecks.Run();
            ProductionOrderChecks.Run();
            ResidentChecks.Run();
            MeleeChecks.Run();
            _world=CombatChecks.Fixture();_overview.World=_world;
            var worker=_world.Workers[0];
            foreach(var sex in new[]{"Dwarf","DwarfFemale"})
            {
                var view=new ActorModelView();AddChild(view);
                view.Initialize(new(){Scene=$"res://Scenes/Characters/{sex}.tscn",Animations=new(){{"idle","Idle"},{"attack","Attack"}}});
                var rig=ActorModelView.Descendants(view).OfType<Skeleton3D>().Single();
                var socket=ActorModelView.Descendants(rig).OfType<BoneAttachment3D>().Single(n=>n.BoneName=="slot_hand_r");
                foreach(string id in ToolEquipmentChecks.Items.Prepend("sword").Append("pickaxe"))
                {
                    var stack=new ItemStack{Item=id,Slot="main",Count=1};
                    view.UpdateEquipment([stack]);
                    if(socket.GetChildCount()!=1)throw new Exception("Missing/duplicate hand model: "+sex+id);
                    var held=socket.GetChild<Node3D>(0);
                    view.UpdateEquipment([stack]);
                    if(socket.GetChild<Node3D>(0)!=held)throw new Exception("Unchanged gear rebuilt");
                    if(EquipmentArt.Icon(id) is null)throw new Exception("Missing icon: "+id);
                    if(ToolEquipmentChecks.Items.Contains(id))
                    {
                        var mesh=held.GetNode<MeshInstance3D>("ItemMesh");var bounds=mesh.GetAabb();
                        var geometry=EquipmentPhysicalCatalog.Get(id);
                        static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
                        if(bounds.Position.DistanceTo(V(geometry.ReferenceMin))>.001||bounds.End.DistanceTo(V(geometry.ReferenceMax))>.001)
                            throw new Exception("Physical dimensions differ from mesh: "+id);
                        worker.Combat.AttackWeapon=id;
                        foreach(float time in new[]{0f,.48f,.65f,.78f,1.3f})
                        {
                            worker.Combat.AttackTime=time;view.Present(worker,time,Vector3.Back,0);
                            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                            var rendered=rig.GlobalTransform.AffineInverse()*(held.GlobalTransform*V(geometry.ContactEnd));
                            var target=MeleeProfiles.Blade(MeleeProfiles.Weapon(id),time).B;
                            if(rendered.DistanceTo(new(target.X,target.Y,target.Z))>.01f)throw new Exception("Tool sweep differs from animation: "+sex+id);
                        }
                    }
                }
                worker.Combat.AttackWeapon="";
                view.UpdateEquipment([]);
                if(socket.GetChildCount()!=0)throw new Exception("Unequipped tool ghost");
                view.QueueFree();
            }
            foreach(var entry in EquipmentModels.Catalog)
            {
                var first=EquipmentModels.Create(entry.Key,true)!;var second=EquipmentModels.Create(entry.Key,true)!;
                if(first.GetNode<MeshInstance3D>("ItemMesh").Mesh!=second.GetNode<MeshInstance3D>("ItemMesh").Mesh)
                    throw new Exception("Meshes not shared: "+entry.Key);
                first.Free();second.Free();
            }
            if(DisplayServer.GetName()!="headless")
            {
                foreach(bool onCharacters in new[]{false,true})
                {
                    var gallery=BuildEquipmentGallery(onCharacters);
                    for(int i=0;i<5;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(onCharacters?"res://.local/equipment-held.png":"res://.local/equipment-catalog.png"));
                    gallery.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                }
            }
            GD.Print("TOOL_MODELS_OK: male/female grips, swap/removal, shared meshes, physical bounds, 50 animated sweep samples, reserved assets");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
    private CanvasLayer BuildEquipmentGallery(bool onCharacters)
    {
        var layer=new CanvasLayer{Layer=100};AddChild(layer);
        var panel=new PanelContainer();layer.AddChild(panel);panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("15232b"),ContentMarginLeft=16,ContentMarginRight=16,ContentMarginTop=12,ContentMarginBottom=12});
        var column=new VBoxContainer();panel.AddChild(column);
        column.AddChild(Text(onCharacters?"武器与工具 · 人物握持":"武器与工具 · 3D 素材",22,Accent));
        var grid=new GridContainer{Columns=3,SizeFlagsVertical=Control.SizeFlags.ExpandFill};column.AddChild(grid);
        foreach(var pair in EquipmentModels.Catalog)
        {
            var cell=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};grid.AddChild(cell);
            cell.AddChild(Text(pair.Value.Name+(pair.Value.Playable?"":" · 素材预留"),16,Ink));
            var container=new SubViewportContainer{Stretch=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};cell.AddChild(container);
            var viewport=new SubViewport{Size=new(300,180),OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};container.AddChild(viewport);
            var env=new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("22343e"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.65f}};viewport.AddChild(env);
            var light=new DirectionalLight3D{RotationDegrees=new(-30,-30,0),LightEnergy=1.1f};viewport.AddChild(light);
            if(onCharacters&&pair.Value.Playable)
            {
                var holder=new Node3D{Position=new(0,-.7f,0)};viewport.AddChild(holder);
                var actor=new ActorModelView();holder.AddChild(actor);actor.Initialize(ActorModelView.LoadLegacyDefinition());
                var worn=new[]{"shirt","trousers","boots",pair.Key}.Select(id=>new ItemStack{Item=id,Slot=ResidentGear.Get(id)!.Slot,Count=1});
                actor.UpdateEquipment(worn);actor.Present(new DemoWorker{Name="展示居民"},0,Vector3.Back,0);
            }
            else
            {
                var model=EquipmentModels.Create(pair.Key,true)!;viewport.AddChild(model);
                var mesh=model.GetNode<MeshInstance3D>("ItemMesh");
                model.RotationDegrees=new(0,0,-30);model.Position=-(model.Basis*mesh.GetAabb().GetCenter());
            }
            var camera=new Camera3D{Projection=Camera3D.ProjectionType.Orthogonal,Size=onCharacters?2.3f:1.65f,Position=onCharacters?new(2,1,3):new(3,.2f,1.2f),Current=true};viewport.AddChild(camera);camera.LookAt(Vector3.Zero);
        }
        return layer;
    }
}
