using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void ReviewMiningCutaway()
    {
        try
        {
            void Check(bool ok,string why){if(!ok)throw new Exception("MINING_CUTAWAY: "+why);}
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;_placement=0;
            var origin=new Cell(125,125,-1);var floor=Enumerable.Range(0,4).Select(z=>origin+new Cell(0,z)).ToArray();
            foreach(var c in floor)_world.Terrain.Excavate(c);
            // An unsupported opening at the end makes this an open trench, not an enclosed room.
            _world.Terrain.Excavate(origin+new Cell(0,4));_world.Terrain.Excavate(origin+new Cell(0,4,-1));
            _layerOffset=origin.Level-_world.Camp.Level;_heightSelected=true;_cutaway=true;_interiorView=false;
            _floorWalls=false;_revealUnknown=false;_topDown=false;_orbitPitch=Mathf.DegToRad(35);
            _cameraYaw=Mathf.DegToRad(45);_focus=WorldPosition(origin+new Cell(0,1));_camera.Size=8;
            CreatePeople();PositionCamera();_buildMenu.Hide();_inspector.Hide();
            async Task Settle()
            {
                SyncWorld();
                var timer=System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    if(timer.Elapsed.TotalSeconds>30)throw new Exception("Mining view did not settle");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);PumpInteriorWork();
                    if(_revision!=_world.Revision)SyncWorld();
                }while(InteriorPending||_terrainView.PendingMeshCount>0);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            }
            async Task Capture(string name)
            {
                if(DisplayServer.GetName()=="headless")return;
                RefreshUi();RenderingServer.ForceDraw();
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+name+".png"));
                await Task.CompletedTask;
            }
            SelectTool("select");await Settle();await Capture("mining-cutaway-before");
            Check(floor.Any(c=>DigPick(_camera.UnprojectPosition(WorldPosition(c)+Vector3.Up*.01f))!=c+new Cell(0,0,-1)),"fixture did not reproduce blocked floor");
            int terrainRevision=_world.Terrain.Revision;var original=_world.Terrain.Excavated.ToHashSet();
            foreach(bool background in new[]{false,true})
            {
                _backgroundInterior=background;_interiorInputs=null;
                SelectTool("dig");
                foreach(float pitch in new[]{20f,35f,55f,80f})foreach(float yaw in new[]{45f,90f,135f,180f,225f,270f,315f,0f})
                {
                    _orbitPitch=Mathf.DegToRad(pitch);_cameraYaw=Mathf.DegToRad(yaw);PositionCamera();await Settle();
                    Check(_wallCaps is not null,$"mining cut result missing at {yaw}/{pitch}, async={background}");
                    Check(InteriorRooms(origin.Level).Cells.Count==0,"open trench became an enclosed room");
                    foreach(var c in floor)
                    {
                        var actual=DigPick(_camera.UnprojectPosition(WorldPosition(c)+Vector3.Up*.01f));
                        Check(actual==c+new Cell(0,0,-1),$"floor pick blocked at {yaw}/{pitch}, async={background}: {c} -> {actual}");
                    }
                    int stamp=_viewStamp,builds=_interiorOcclusionBuilds;
                    PrepareInteriorWalls();Check(stamp==_viewStamp&&builds==_interiorOcclusionBuilds,"unchanged mining view rebuilt");
                }
                SelectTool("select");await Settle();Check(_wallCaps is null,"leaving mining retained cuts");
            }
            _orbitPitch=Mathf.DegToRad(35);_cameraYaw=Mathf.DegToRad(45);PositionCamera();SelectTool("dig");await Settle();
            var start=floor[0]+new Cell(0,0,-1);var end=floor[^1]+new Cell(0,0,-1);
            CommitDigDrag(start,end);await Settle();
            Check(_world.Jobs.Count(j=>j.Dig)==4&&_world.Jobs.All(j=>j.Cell.Level==origin.Level-1),"mining drag queued the foreground or wrong layer");
            Check(_world.Terrain.Revision==terrainRevision&&original.SetEquals(_world.Terrain.Excavated),"visibility changed real terrain");
            await Capture("mining-cutaway-after");
            SelectTool("digstairs");await Settle();Check(_wallCaps is {Count:>0},"stairs excavation lost cutaway");
            StepLayer(1);await Settle();Check(_wallCaps is {Count:0},"different level retained old pit cuts");
            StepLayer(-1);await Settle();Check(_wallCaps is {Count:>0},"returning to level failed to restore cuts");
            _heightSelected=false;_layerOffset=0;_interiorInputs=null;await Settle();
            Check(_wallCaps is {Count:>0},"default surface mining did not reveal a one-metre pit");
            SetViewMode(0);await Settle();Check(_wallCaps is null,"overview retained mining cutaway");
            GD.Print("MINING_CUTAWAY_OK: reproduced blocked 1x4 open trench, 256 floor picks across eight angles/four pitches/sync/async, exact mining jobs, cache, modes, height/default surface and unchanged terrain");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
