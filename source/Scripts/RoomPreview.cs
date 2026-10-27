using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void CaptureRoomPreview()
    {
        try
        {
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_world.Items.Clear();_world.Stockpiles.Clear();
            _overview.World=_world;_terrainRevision=-1;_clock.Paused=true;_developerMode=false;_creative=false;
            var origin=_world.Camp+new Cell(4,5);RoomEnvironmentChecks.Shell(_world,origin);
            for(int x=1;x<5;x++)for(int z=1;z<5;z++)
            {var c=origin+new Cell(x,z,-1);_world.Terrain.Excavate(c);_world.Buildings[c]=new(c,BuildKind.Planks);}
            var bed=origin+new Cell(1,1);_world.Buildings[bed]=new(bed,BuildKind.Bed);
            var fire=origin+new Cell(1,4);_world.Buildings[fire]=new(fire,BuildKind.Campfire);_world.FireFuel[fire]=300;
            var target=origin+new Cell(2,2);_world.RoomAt(target);
            for(int i=0;i<30*90;i++)_world.Update(1d/30);
            _foundation=new FoundationPlan(origin,origin+new Cell(5,5));_foundation.SetFloorHeight(3);_heightSelected=true;_layerOffset=0;
            _focus=WorldPosition(target);_camera.Size=14;_cameraYaw=0;_topDown=false;_orbitPitch=DefaultCameraPitch;PositionCamera();CreatePeople();
            _buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();SetViewMode(1);SelectTool("select");SyncWorld();
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            async Task Snap(string name)
            {
                await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                if(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://.local/{name}.png"))!=Error.Ok)throw new IOException("Screenshot failed");
            }
            await Frame();
            var screen=_camera.UnprojectPosition(WorldPosition(target));
            foreach(bool down in new[]{true,false})GetViewport().PushInput(new InputEventMouseButton{Position=screen,GlobalPosition=screen,ButtonIndex=MouseButton.Left,Pressed=down},true);
            await Frame();RefreshUi();
            if(!_selectionPanel.Visible||!_selection.Text.Contains("室温")||!_selection.Text.Contains("精致"))throw new InvalidOperationException("Floor click did not expose room quality and indoor temperature: "+_selection.Text);
            await Snap("room-enclosed");
            var roof=target+new Cell(0,0,2);_world.Buildings.Remove(roof);SyncWorld();RefreshUi();
            if(!_selection.Text.Contains("室外")||_selection.Text.Contains("室温"))throw new InvalidOperationException("Open roof did not switch selection to outdoor climate");
            await Snap("room-open");
            if(_selectionPanel.GetGlobalRect().Intersects(_buildTab.GetGlobalRect()))throw new InvalidOperationException("Room information overlaps bottom navigation");
            GD.Print("ROOM_UI_OK: floor selection, quality, room temperature, roof opening, outdoor fallback and panel bounds");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
