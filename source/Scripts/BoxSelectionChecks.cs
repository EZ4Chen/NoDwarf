using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void CheckBoxSelection()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            void Motion(Vector2 p)=>GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p,ButtonMask=MouseButtonMask.Left},true);
            void Mouse(MouseButton button,bool down,Vector2 p,bool shift=false)=>GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=button,Pressed=down,Position=p,GlobalPosition=p,ShiftPressed=shift},true);
            void Drag(Vector2 a,Vector2 b,bool shift=false){Motion(a);Mouse(MouseButton.Left,true,a,shift);Motion(b);Mouse(MouseButton.Left,false,b,shift);}
            void Click(Vector2 p,bool shift=false){Motion(p);Mouse(MouseButton.Left,true,p,shift);Mouse(MouseButton.Left,false,p,shift);}
            void DraftKey(bool echo=false,bool control=false)
            {
                foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventKey{Keycode=Key.R,PhysicalKeycode=Key.R,Pressed=down,Echo=down&&echo,CtrlPressed=control},true);
            }
            _clock.Paused=true;_world=new DemoWorld(flat:true);_world.Stockpiles.Clear();_world.Jobs.Clear();_world.DeveloperResidents(true);
            _overview.World=_world;_terrainRevision=-1;_foundation=null;_heightSelected=false;_layerOffset=0;_cutaway=false;_topDown=false;_orbitPitch=DefaultCameraPitch;_cameraYaw=0;
            var origin=new Cell(60,60);for(int i=0;i<_world.Workers.Count;i++){_world.Workers[i].X=origin.X+i*2;_world.Workers[i].Z=origin.Z;_world.Workers[i].Level=0;}
            _focus=WorldPosition(origin+new Cell(2,1));_camera.Size=18;PositionCamera();CreatePeople();SelectTool("select");ClearResidentSelection();
            _buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();_combatPanel.Hide();SyncWorld();_terrainView.FlushMeshes();await Frame();
            var points=_people.Select(p=>_camera.UnprojectPosition(p.Position+Vector3.Up*.7f)).ToArray();
            var min=new Vector2(points.Min(p=>p.X)-18,points.Min(p=>p.Y)-18);var max=new Vector2(points.Max(p=>p.X)+18,points.Max(p=>p.Y)+18);
            Motion(max);Mouse(MouseButton.Left,true,max);Motion(min);
            Check(_marquee?.Visible==true&&SelectedResidents().Length==0,"Box selection must preview before mouse release");
            await Frame();
            if(DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/resident-marquee.png"));}
            Mouse(MouseButton.Left,false,min);Check(SelectedResidents().Length==3&&_selection.Text.Contains("3 位"),"Reverse drag must select three residents");
            Click(points[1],true);Check(SelectedResidents().Length==2&&!IsResidentSelected(1),"Shift click must remove a member");
            Drag(points[1]-Vector2.One*8,points[1]+Vector2.One*8,true);Check(SelectedResidents().Length==3,"Shift drag must append residents");
            Click(points[0]);Check(SelectedResidents().SequenceEqual(new[]{0}),"Single click must replace group selection");
            _people[2].Hide();Drag(min,max);Check(SelectedResidents().Length==2,"Hidden residents must not be box selected");_people[2].Show();
            Drag(min,max);int facingBefore=_facing;
            DraftKey(control:true);Check(_world.Workers.All(w=>!w.Combat.Drafted)&&_facing==facingBefore,"Modified R must not draft or rotate");
            ToggleConsole();DraftKey();Check(_world.Workers.All(w=>!w.Combat.Drafted),"Typing R in console drafted residents");ToggleConsole();
            _world.DraftResident(_world.Workers[0].Id,true);
            DraftKey();Check(_world.Workers.All(w=>w.Combat.Drafted)&&_facing==facingBefore,"R must draft the mixed group without rotating");
            DraftKey(echo:true);Check(_world.Workers.All(w=>w.Combat.Drafted),"Held R repeatedly toggled draft");
            Check(_world.StartTestRaid(1).Ok,"Group attack fixture");var enemy=_world.Enemies.Single();enemy.X=origin.X+2;enemy.Z=origin.Z+3;enemy.Level=0;AnimateCombat();await Frame();
            var enemyPoint=_camera.UnprojectPosition(WorldPosition(enemy.Tile)+Vector3.Up*.7f);Motion(enemyPoint);Mouse(MouseButton.Right,true,enemyPoint);Mouse(MouseButton.Right,false,enemyPoint);
            Check(_world.Workers.All(w=>w.Combat.Target==enemy.Id),"Group attack must target the clicked enemy");
            var destination=_camera.UnprojectPosition(WorldPosition(origin+new Cell(3,4)));Motion(destination);Mouse(MouseButton.Right,true,destination);Mouse(MouseButton.Right,false,destination);
            Check(_world.Workers.All(w=>w.Combat.Destination is not null)&&_world.Workers.Select(w=>w.Combat.Destination).Distinct().Count()==3,"Group move must assign reachable distinct destinations");
            _combatStop.EmitSignal(Godot.Button.SignalName.Pressed);Check(_world.Workers.All(w=>w.Combat.Destination is null),"Group stop failed");
            DraftKey();Check(_world.Workers.All(w=>!w.Combat.Drafted),"R must undraft the selected group");
            Motion(min);Mouse(MouseButton.Left,true,min);Motion(max);Mouse(MouseButton.Right,true,max);Mouse(MouseButton.Right,false,max);Mouse(MouseButton.Left,false,max);
            Check(SelectedResidents().Length==3&&_marquee?.Visible==false,"Cancel must preserve selection");
            var ui=_buildTab.GetGlobalRect().GetCenter();Motion(ui);Mouse(MouseButton.Left,true,ui);Motion(max);Mouse(MouseButton.Left,false,max);
            Check(_leftStart is null&&SelectedResidents().Length==3,"UI drag must not begin a map selection");_buildMenu.Hide();
            await Frame();
            if(DisplayServer.GetName()!="headless")
            {await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/resident-group.png"));}
            // Flat debug terrain is stone, unsuitable for surface crops. Use a
            // real, level soil patch for the zoning half of this input test.
            _world=new DemoWorld();_world.Stockpiles.Clear();_world.DeveloperResidents(false);_overview.World=_world;_terrainRevision=-1;
            var a=FarmingChecks.SurfacePlot(_world);var b=a+new Cell(2,1);
            ClearResidentSelection();_heightSelected=false;_layerOffset=0;_cutaway=false;_focus=WorldPosition(a+new Cell(1,1));PositionCamera();CreatePeople();SyncWorld();_terrainView.FlushMeshes();await Frame();
            Vector2 Screen(Cell c)=>_camera.UnprojectPosition(WorldPosition(c));
            SelectTool("growingzone");Drag(Screen(a),Screen(b));Check(_world.Farms.Count==6,"Growing zone drag must create six cells");
            SelectTool("unfarm");Drag(Screen(b),Screen(a));Check(_removeGrowingDialog?.Visible==true&&_world.Farms.Count==6,"Farm removal drag must still require confirmation");
            _removeGrowingDialog!.Hide();_removeGrowingDialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);Check(_world.Farms.Count==6,"Cancel deletion lost crops");
            _world.SetFarm(a,b,"grain",true);SyncWorld();
            SelectTool("growingzone");Click(Screen(a));Check(_world.Farms.Count==0&&_farmStart==a,"First corner click must not commit");Click(Screen(b));Check(_world.Farms.Count==6,"Two-click zone workflow regressed");
            _world.SetFarm(a,b,"grain",true);SyncWorld();
            SelectTool("stockpile");Drag(Screen(a),Screen(b));Check(_world.Stockpiles.Count==6,"Storage drag failed");_storagePanel?.QueueFree();_storagePanel=null;await Frame();
            SelectTool("unstockpile");Drag(Screen(b),Screen(a));Check(_world.Stockpiles.Count==0,"Storage removal drag failed");
            SelectTool("growingzone");Motion(Screen(a));Mouse(MouseButton.Left,true,Screen(a));Motion(Screen(b));
            GetViewport().PushInput(new InputEventKey{Keycode=Key.Escape,Pressed=true},true);Mouse(MouseButton.Left,false,Screen(b));Check(_world.Farms.Count==0,"Escape committed a zone");
            SelectTool("foundation");Drag(Screen(a),Screen(b));Check(_foundation?.Min==a&&_foundation.Max==b,"Foundation drag failed");
            GD.Print("BOX_SELECTION_OK: reverse drag, preview, Shift toggle/append, single selection, hidden residents, UI isolation, cancellation, R draft/undraft/input guards, right-click attack/move/stop and drag/two-click zones");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
