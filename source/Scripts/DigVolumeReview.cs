using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewDigVolume()
    {
        try
        {
            void Check(bool value,string reason){if(!value)throw new Exception(reason);}
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;
            _consolePanel.Hide();_characterEditor.Hide();_buildMenu.Hide();_inspector.Hide();
            SelectTool("dig");
            var start=_world.Camp+new Cell(2,2,-2);var end=start+new Cell(2,1);
            _heightSelected=true;_layerOffset=start.Level-_world.Camp.Level;
            _digStart=start;_digEnd=end;
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            Check(_world.Jobs.Count==0&&_digStart==start,"release committed selection");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Minus,PhysicalKeycode=Key.Minus});
            Check(_digStart==start&&_digEnd==end+new Cell(0,0,-1),"height change lost anchor or endpoint");
            DrawDigDrag(start,_digEnd!.Value);Check(_hover.GetChildCount()==16,"cuboid edges/layer rings missing");
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});
            Check(_world.Jobs.Count(j=>j.Dig)==12&&_world.Jobs.Where(j=>j.Dig).All(j=>j.DigHeight==1),"two-layer cuboid mismatch");
            Check(_digStart is null,"confirmation retained anchor");
            int count=_world.Jobs.Count;CommitDigDrag(end+new Cell(0,0,-1),start);
            Check(_world.Jobs.Count==count,"duplicate jobs");
            Check(_planVisuals.Count==6,"current-layer excavation markers missing");
            StepLayer(1);SyncWorld();Check(_planVisuals.Count==6&&_planVisuals.Values.All(v=>v.Cell.Level==start.Level),"layer switch failed to update markers");
            _digStart=start;_digEnd=end;HandleMapGesture(new InputEventKey{Pressed=true,Keycode=Key.Escape});
            Check(_digStart is null&&_world.Jobs.Count==count,"escape changed jobs");
            _digStart=start;_digEnd=end;HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true});
            Check(_digStart is null&&_world.Jobs.Count==count,"right cancel changed jobs");
            CommitDigDrag(start,start+new Cell(100,100,-10));Check(_world.Jobs.Count==count,"oversize not rejected");
            _developerMode=true;_creative=true;var creative=start+new Cell(5,0);
            CommitDigDrag(creative,creative+new Cell(1,0,-1));
            foreach(var c in new[]{creative,creative+new Cell(1,0),creative+new Cell(0,0,-1),creative+new Cell(1,0,-1)})Check(!_world.NaturalSolid(c),"creative cuboid not excavated");
            // Cancel only excavation plans, including reverse corners and multi-level tasks.
            _developerMode=false;_creative=false;SelectTool("canceldig");_buildMenu.Hide();
            int planned=_world.Jobs.Count;var outside=start+new Cell(8,0);
            Check(_world.QueueDig(outside),"outside fixture");
            var unrelated=new DemoJob{Id=-100,Cell=start};_world.Jobs.Add(unrelated);
            var buildings=_world.Buildings.ToDictionary(p=>p.Key,p=>p.Value);
            var excavated=_world.Terrain.Excavated.ToHashSet();
            _digStart=start;_digEnd=end;
            HandleMapGesture(new InputEventKey{Pressed=true,Keycode=Key.Escape});
            Check(_world.Jobs.Count==planned+2,"canceling cancel-selection changed jobs");
            _digStart=start;_digEnd=end;
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true});
            Check(_digStart is null&&_world.Jobs.Count==planned+2,"right click canceled tasks");
            _digStart=end;_digEnd=start;
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Minus,PhysicalKeycode=Key.Minus});
            Check(_digEnd==start+new Cell(0,0,-1),"cancel selection height lost");
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
            Check(_world.Jobs.Count==planned+2,"cancel selection committed on release");
            DrawDigDrag(_digStart!.Value,_digEnd!.Value);
            Check(_hint.Text.Contains("取消挖掘"),"cancel preview mislabeled");
            HandleMapGesture(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});
            Check(_world.Jobs.Count==2&&_world.Jobs.Contains(unrelated)&&_world.Jobs.Any(j=>j.Dig&&j.Cell==outside),"cancel affected unrelated or outside jobs");
            Check(_planVisuals.Values.All(v=>v.Cell==outside),"canceled markers retained");
            Check(_world.Terrain.Excavated.ToHashSet().SetEquals(excavated)&&_world.Buildings.SequenceEqual(buildings),"cancel changed physical world");
            CommitCancelDig(start,end);Check(_world.Jobs.Count==2,"empty cancellation changed jobs");
            var tall=outside+new Cell(3,0,-2);Check(_world.QueueDig(tall,height:3),"multi-level fixture");
            CommitCancelDig(tall+new Cell(0,0,2),tall+new Cell(0,0,2));
            Check(!_world.Jobs.Any(j=>j.Dig&&j.Cell==tall),"intersection missed multi-level job");
            GD.Print("CANCEL_DIG_VOLUME_OK: two clicks, layer change, reverse corners, release, Esc/right abort, outside/other jobs preserved, markers cleared, terrain preserved, tall-task intersection");
            // A new batch retains its original cuboid even after partial completion/save/load.
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_overview.World=_world;
            _creative=false;_developerMode=false;SelectTool("dig");
            start=_world.Camp+new Cell(2,2,-2);end=start+new Cell(2,1,-1);
            CommitDigDrag(start,end);var first=_world.Jobs.Where(j=>j.Dig).ToArray();
            Check(first.Length==12&&first.Select(j=>j.DigRegion).Distinct().Count()==1&&first[0].DigRegion!="","batch identity missing");
            CommitDigDrag(start+new Cell(3,0),end+new Cell(3,0));
            Check(_world.Jobs.Where(j=>j.Dig).Select(j=>j.DigRegion).Distinct().Count()==2,"adjacent batches merged");
            _world.CancelJob(first.Single(j=>j.Cell==start));
            _world=DemoWorld.LoadJson(_world.SaveJson());_overview.World=_world;
            _layerOffset=start.Level-_world.Camp.Level;_heightSelected=true;_cutaway=true;
            _focus=WorldPosition(start);_camera.Size=12;PositionCamera();SelectTool("select");
            var click=_camera.UnprojectPosition(WorldPosition(start)+Vector3.Up);
            Check(PickTarget(click)==start,"original region no longer pickable at finished cell");
            Act(start);
            Check(_cancelDigRegionAction.Visible&&_selection.Text.Contains("剩余 11"),"region selection button/count missing");
            Check(_digRegionHighlight is {Visible:true}&&_digRegionHighlight.GetChildCount()==2,"selected volume missing");
            var oldBoundary=_digRegionHighlight!.GetChild(0);
            RefreshUi();Check(_digRegionHighlight.GetChild(0)==oldBoundary,"unchanged selection rebuilt geometry");
            var firstPosition=_digRegionHighlight.Position;
            Act(start+new Cell(3,0));Check(_digRegionHighlight.Visible&&_digRegionHighlight.Position!=firstPosition,"adjacent region retained old highlight");
            Act(start);Check(_digRegionHighlight.Position==firstPosition,"original region highlight bounds lost");
            StepLayer(-1);Check(_digRegionHighlight.GetNode<MeshInstance3D>("SelectedLayer").Visible,"lower selected layer lost fill");
            StepLayer(-1);Check(!_digRegionHighlight.GetNode<MeshInstance3D>("SelectedLayer").Visible&&_digRegionHighlight.Visible,"outside layer fill misleading or volume hidden");
            StepLayer(1);StepLayer(1);
            _selectionPanel.Hide();HideInactiveDigHighlight();Check(!_digRegionHighlight.Visible,"closed panel retained highlight");
            Act(start);SelectTool("dig");HideInactiveDigHighlight();Check(!_digRegionHighlight.Visible,"changed tool retained highlight");
            SelectTool("select");Act(start);
            if(DisplayServer.GetName()!="headless")
            {
                _developerMode=true;_revealUnknown=true;SyncWorld();_terrainView.FlushMeshes();
                for(int frame=0;frame<8;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                RefreshUi();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/dig-region-highlight.png"));
                OrbitCamera(90);
                for(int frame=0;frame<3;frame++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/dig-region-highlight-rotated.png"));
            }
            _cancelDigRegionAction.EmitSignal(Godot.Button.SignalName.Pressed);
            Check(_world.Jobs.Count(j=>j.Dig)==12&&_world.Jobs.All(j=>j.DigRegion!=first[0].DigRegion),"region cancel crossed batch boundary");
            Check(!_cancelDigRegionAction.Visible,"region cancel button retained after completion");
            Check(_digRegionHighlight is {Visible:false},"canceled region highlight retained");
            Act(start+new Cell(3,0));
            foreach(var job in _world.Jobs.Where(j=>j.Dig).ToArray())_world.CancelJob(job,false);
            RefreshUi();Check(_digRegionHighlight is {Visible:false},"finished region highlight retained");
            GD.Print("DIG_REGION_HIGHLIGHT_OK: full bounds, adjacent switch, cached geometry, layer fill, panel/tool cleanup, canceled/finished cleanup");
            GD.Print("DIG_REGION_SELECTION_OK: original bounds, neighboring batches, partial task removal, save/load, click selection, cancel button, remaining count");
            GD.Print("DIG_VOLUME_OK: release/second click, height anchor, 12 voxels, outline, pending markers, duplicate/reverse, cancel, size limit, creative excavation");
            GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
