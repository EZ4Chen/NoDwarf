using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void ReviewDesignations()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("DESIGNATION: "+why);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            void Motion(Vector2 p)=>GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p,ButtonMask=MouseButtonMask.Left},true);
            void Mouse(MouseButton button,bool down,Vector2 p)=>GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=button,Pressed=down,Position=p,GlobalPosition=p},true);
            void Drag(Vector2 a,Vector2 b){Motion(a);Mouse(MouseButton.Left,true,a);Motion(b);Mouse(MouseButton.Left,false,b);}
            void Click(Vector2 p){Motion(p);Mouse(MouseButton.Left,true,p);Mouse(MouseButton.Left,false,p);}
            _clock.Paused=true;_world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_world.Jobs.Clear();_world.Stockpiles.Clear();
            _overview.World=_world;_foundation=null;_heightSelected=false;_layerOffset=0;_cutaway=false;_topDown=false;
            _developerMode=false;_creative=false;_cameraYaw=0;_orbitPitch=DefaultCameraPitch;
            var roots=new[]{new Cell(61,61),new Cell(65,61),new Cell(61,65),new Cell(69,64)};
            foreach(var c in roots){_world.Trees[c]=new(){Cell=c,Age=600,MaximumHeight=6};_world.Terrain.SetTree(c,true);}
            _focus=WorldPosition(new(64,64))+Vector3.Up*2;_camera.Size=22;PositionCamera();CreatePeople();
            _buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();_combatPanel.Hide();
            SelectTool("chop");SyncWorld();_terrainView.FlushMeshes();await Frame();
            Vector2 Screen(Cell c)=>_camera.UnprojectPosition(WorldPosition(c));
            var a=Screen(new(60,60));var b=Screen(new(66,66));
            Motion(b);Mouse(MouseButton.Left,true,b);Motion(a);await Frame();
            Check(_world.Jobs.Count==0&&_leftMoved&&_hint.Text.Contains("3 棵树"),"drag must preview three trees before committing");
            Mouse(MouseButton.Left,false,a);SyncWorld();await Frame();
            Check(_world.Jobs.Count(j=>j.Chop)==3&&roots.Take(3).All(c=>_world.Jobs.Any(j=>j.Chop&&j.Cell==c)),"reverse drag tree range");
            Check(_designationMarkers.Count==3&&_designationMarkers.Values.All(m=>m.Chop&&m.Root.Visible),"felling badges missing");
            var ids=_designationMarkers.ToDictionary(p=>p.Key,p=>p.Value.Root.GetInstanceId());
            Drag(a,b);SyncWorld();Check(_world.Jobs.Count==3&&ids.All(p=>_designationMarkers[p.Key].Root.GetInstanceId()==p.Value),"repeat drag duplicated jobs or recreated badges");
            var outside=Screen(roots[3]+new Cell(1,1));
            foreach(bool escape in new[]{true,false})
            {
                Motion(a);Mouse(MouseButton.Left,true,a);Motion(outside);
                if(escape){GetViewport().PushInput(new InputEventKey{Pressed=true,Keycode=Key.Escape},true);GetViewport().PushInput(new InputEventKey{Pressed=false,Keycode=Key.Escape},true);}
                else{Mouse(MouseButton.Right,true,outside);Mouse(MouseButton.Right,false,outside);}
                Mouse(MouseButton.Left,false,outside);Check(_world.Jobs.Count==3,"cancel committed tree orders");
            }
            Click(_camera.UnprojectPosition(WorldPosition(roots[3])+Vector3.Up*.4f));SyncWorld();await Frame();
            Check(_world.Jobs.Count==4&&_designationMarkers.Count==4,"single-tree click regressed");
            var mine=new Cell(65,67,-1);Check(_world.QueueDig(mine,false,0,false),"mining marker fixture");
            Check(_world.QueueDig(mine+new Cell(1,0),false,0,false),"second mining marker fixture");SyncWorld();await Frame();
            Check(_designationMarkers.Values.Count(m=>!m.Chop)==2,"mining badges missing");
            foreach(var pair in _designationMarkers)
            {
                var marker=pair.Value;var job=_world.Jobs.Single(j=>j.Id==pair.Key);
                Check(marker.Icon.Texture==UiIcons.Get(job.Chop?"mark_chop":"mark_dig"),"wrong designation symbol");
                if(job.Chop)Check(marker.Root.Position.Y>job.Cell.Level+_world.TreeHeight(job.Cell),"axe badge buried in foliage");
            }
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;SyncWorld();_terrainView.FlushMeshes();await Frame();
            Check(_designationMarkers.Count==6,"designation badges lost on load");
            if(DisplayServer.GetName()!="headless")
            {
                RefreshUi();RenderingServer.ForceDraw();
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/forestry-mining-designations.png"));
            }
            _heightSelected=true;_cutaway=true;_layerOffset=0;SyncWorld();
            Check(_designationMarkers.Where(p=>!p.Value.Chop).All(p=>!p.Value.Root.Visible&&p.Value.Body.CollisionLayer==0),"lower-floor mining badges stayed visible");
            _layerOffset=-1;SyncWorld();
            Check(_designationMarkers.Where(p=>!p.Value.Chop).All(p=>p.Value.Root.Visible&&_planVisuals.ContainsKey(p.Key)),"mining badge and outline layers disagree");
            _heightSelected=false;_cutaway=false;_layerOffset=0;SyncWorld();await Frame();
            var last=_world.Jobs.Single(j=>j.Chop&&j.Cell==roots[3]);
            var iconPoint=_camera.UnprojectPosition(_designationMarkers[last.Id].Root.Position);
            Check(PickTarget(iconPoint)==last.Cell,"badge cannot be picked for cancellation");
            SelectTool("cancelplan");Click(iconPoint);SyncWorld();await Frame();
            Check(!_world.Jobs.Any(j=>j.Id==last.Id)&&!_designationMarkers.ContainsKey(last.Id),"cancel did not remove tree badge");
            _world.FellTree(roots[0]);SyncWorld();Check(_designationMarkers.Values.Count(m=>m.Chop)==2,"felled tree retained badge");
            _world.DeveloperFinishPlans();SyncWorld();Check(_designationMarkers.Values.All(m=>m.Chop),"finished mining retained badge");
            _heightSelected=true;_layerOffset=-2;_cutaway=true;SyncWorld();
            Check(_designationMarkers.Values.All(m=>!m.Root.Visible&&m.Body.CollisionLayer==0),"hidden-floor badges intercepted picking");
            _heightSelected=false;_layerOffset=0;_cutaway=false;SelectTool("chop");SyncWorld();await Frame();
            int count=_world.Jobs.Count;var ui=_buildTab.GetGlobalRect().GetCenter();
            Motion(ui);Mouse(MouseButton.Left,true,ui);Motion(b);Mouse(MouseButton.Left,false,b);
            Check(_world.Jobs.Count==count&&_leftStart is null,"UI-origin drag issued felling orders");
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;SyncWorld();
            Check(_designationMarkers.Count==0,"world replacement retained markers");
            GD.Print("DESIGNATION_OK: real reverse drag/single click, preview, duplicate prevention, Esc/right cancel, UI isolation, axe/pickaxe badges, save/load, cancel/completion, floor hiding and resource reuse");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
