using Godot;
using NoDwarf.Simulation;
using System.Xml.Linq;
namespace NoDwarf;

public partial class Main
{
    private async void ReviewConstructionLine()
    {
        try
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("CONSTRUCTION_LINE: "+why);}
            BlockExposureChecks.Run();
            var from=new Cell(40,40);
            Check(ConstructionLine.Cells(from,new(43,41,8)).SequenceEqual(new[]{from,new Cell(41,40),new Cell(42,40),new Cell(43,40)}),"dominant axis and starting height");
            Check(ConstructionLine.Cells(from,new(39,37)).SequenceEqual(new[]{from,new Cell(40,39),new Cell(40,38),new Cell(40,37)}),"reverse Z line");
            Check(ConstructionLine.Cells(from,from).Length==1&&ConstructionLine.Cells(from,new(int.MaxValue,40)).Length==DemoWorld.Width-40,"inclusive single cell and bounded extent");
            var pack=new ContentPack("linecheck","1.0",1,[],[new("line.xml","defs",XDocument.Parse("<Defs><Block id='linecheck:block' name='试验砖' shape='Solid' color='b87333'/><Building id='linecheck:wall' parent='nodwarf:stone_bricks' name='试验墙' block='linecheck:block'><Cost item='wood' count='2'/></Building></Defs>"))]);
            var catalog=ContentCatalog.Load([pack]);
            _world=new DemoWorld(flat:true,content:catalog);_world.DeveloperResidents(false);_world.Jobs.Clear();_world.Items.Clear();_world.Stockpiles.Clear();
            _world.SpawnItem("cobblestone",80,new(48,48));_world.SpawnItem("wood",20,new(48,48));_world.SpawnItem("sand",20,new(48,48));
            _overview.World=_world;_clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;_layerOffset=0;
            _heightSelected=false;_cutaway=false;_interiorView=false;_floorWalls=false;
            _topDown=false;_orbitPitch=DefaultCameraPitch;_cameraYaw=0;CreatePeople();
            _focus=WorldPosition(from+new Cell(2,1));_camera.Size=17;PositionCamera();SelectTool("StoneBricks");_buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();SyncWorld();_terrainView.FlushMeshes();RefreshUi();
            async Task Frame(){await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
            Vector2 Screen(Cell c)=>_camera.UnprojectPosition(WorldPosition(c)+new Vector3(.07f,0,.03f));
            void ButtonAt(Vector2 p,bool down,MouseButton button=MouseButton.Left)=>GetViewport().PushInput(new InputEventMouseButton{Position=p,GlobalPosition=p,ButtonIndex=button,Pressed=down},true);
            void Move(Vector2 p)=>GetViewport().PushInput(new InputEventMouseMotion{Position=p,GlobalPosition=p,ButtonMask=MouseButtonMask.Left},true);
            await Frame();await Frame();
            Check(PickForTool(Screen(from))==from,"start ray picks ground cell");
            var routeBefore=_world.FindPath(from+new Cell(-2,0),from+new Cell(6,0))!.ToArray();
            int available=_world.Available("cobblestone");
            Move(Screen(from));ButtonAt(Screen(from),true);Move(Screen(from+new Cell(4,1)));await Frame();
            Check(_leftMoved&&_world.Jobs.Count==0&&_world.Available("cobblestone")==available,"drag previews without reserving material");
            Check(_hover.GetChildCount()==5,"five cells previewed");
            ButtonAt(Screen(from+new Cell(4,1)),false);await Frame();
            Check(_world.Jobs.Count==5&&_world.Jobs.Select(j=>j.Cell).SequenceEqual(ConstructionLine.Cells(from,from+new Cell(4,0))),"real mouse release queues exactly one line");
            Check(_world.FindPath(from+new Cell(-2,0),from+new Cell(6,0))!.SequenceEqual(routeBefore),"plans do not alter path");
            Check(!_world.Solid(from)&&_world.Walkable(from)&&!_world.Walkable(from+new Cell(0,0,1)),"no collision or physical support before completion");
            var middle=_world.Jobs.Single(j=>j.Cell==from+new Cell(2,0));
            Check(_planVisuals[middle.Id].Faces==(BlockFaces.Top|BlockFaces.North|BlockFaces.South),"middle cube has only three exterior faces");
            IEnumerable<Node> Nodes(Node n){yield return n;foreach(var ch in n.GetChildren())foreach(var d in Nodes(ch))yield return d;}
            var meshes=Nodes(_planVisuals[middle.Id].Root).OfType<MeshInstance3D>().ToArray();
            Check(meshes.Sum(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Sum(i=>((ArrayMesh)m.Mesh).SurfaceGetArrayLen(i)))==18,"internal triangles actually removed");
            Check(!Nodes(_ghosts).OfType<PhysicsBody3D>().Any()&&Nodes(_ghosts).OfType<Area3D>().Count()==5,"only non-physical selection areas");
            var hit=PickObject(_camera.UnprojectPosition(WorldPosition(middle.Cell)+Vector3.Up*.8f),8);
            Check(hit.Count>0&&hit["collider"].AsGodotObject() is Area3D,"selection ray still picks plan area");
            int paid=available-_world.Available("cobblestone");Check(paid==5*catalog.Building("nodwarf:stone_bricks").Cost["cobblestone"],"exact per-cell reservations");
            CheckConstructionOutlines();
            Check(_constructionVisuals[middle.Id].OutlineMask==12&&_constructionVisuals.Values.Sum(v=>System.Numerics.BitOperations.PopCount((uint)v.OutlineMask))==12,"line has twelve exterior unit edges and no dividers");
            _selectedCell=middle.Cell;_selectedWorker=-1;UpdateConstructionVisuals();CheckConstructionOutlines();
            Check(_constructionVisuals[middle.Id].OutlineMask==15,"selected cell regains a complete highlight");
            _selectedCell=null;UpdateConstructionVisuals();CheckConstructionOutlines();
            Check(_constructionVisuals[middle.Id].OutlineMask==12,"deselection removes internal highlight edges");
            if(DisplayServer.GetName()!="headless")
            {
                SelectTool("select");await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/construction-line-ghosts.png"))==Error.Ok,"save preview capture");
                SelectTool("StoneBricks");
            }
            var old=_planVisuals[middle.Id].Root.GetInstanceId();middle.Progress=DemoJob.BuildWorkRequired*.5;SyncWorld();
            Check(_planVisuals[middle.Id].Root.GetInstanceId()==old,"progress retains geometry");
            var removed=_world.Jobs.Single(j=>j.Cell==from+new Cell(1,0));_world.CancelJob(removed);SyncWorld();
            Check((_planVisuals[middle.Id].Faces&BlockFaces.West)!=0,"cancellation restores exposed face");
            _world.Buildings[removed.Cell]=new(removed.Cell,BuildKind.StoneBricks);SyncWorld();
            Check((_planVisuals[middle.Id].Faces&BlockFaces.West)==0,"real neighboring block hides ghost face");
            Check((BlockExposure.BuildingFaces(_world,CurrentView,removed.Cell)&BlockFaces.East)!=0,"ghost never hides real geometry");
            var saved=DemoWorld.LoadJson(_world.SaveJson(),content:catalog);Check(saved.Jobs.Count==4&&saved.Jobs.All(j=>j.Build==BuildKind.StoneBricks),"current plan save/load");
            _heightSelected=true;_placement=-1;
            Check(LineEndPick(Screen(from+new Cell(4,0)),new Cell(from.X,from.Z,-1))==new Cell(44,40,-1),"floor drag uses upper selection plane without lateral shift");
            _heightSelected=false;_placement=0;
            var bridgeStart=new Cell(60,60,1);_world.Buildings[bridgeStart+new Cell(0,0,-1)]=new(bridgeStart+new Cell(0,0,-1),BuildKind.StoneBricks);
            var previewSolids=new HashSet<Cell>();
            foreach(var c in ConstructionLine.Cells(bridgeStart,bridgeStart+new Cell(3,0)))
            {Check(_world.CanPlace(c,BuildKind.StoneBricks,previewSolids:previewSolids),"line preview accepts preceding planned support");previewSolids.Add(c);}
            Check(!_world.Walkable(bridgeStart+new Cell(1,0,1)),"prospective support never leaks into navigation");
            int jobs=_world.Jobs.Count;available=_world.Available("cobblestone");var cancel=from+new Cell(0,3);
            Move(Screen(cancel));ButtonAt(Screen(cancel),true);Move(Screen(cancel+new Cell(3,0)));ButtonAt(Screen(cancel+new Cell(3,0)),true,MouseButton.Right);ButtonAt(Screen(cancel+new Cell(3,0)),false);
            Check(_world.Jobs.Count==jobs&&_world.Available("cobblestone")==available&&_leftStart is null,"right click cancels entire drag");
            Move(Screen(cancel));ButtonAt(Screen(cancel),true);Move(Screen(cancel+new Cell(3,0)));GetViewport().PushInput(new InputEventKey{Pressed=true,Keycode=Key.Escape},true);ButtonAt(Screen(cancel+new Cell(3,0)),false);
            Check(_world.Jobs.Count==jobs&&_leftStart is null,"escape cancels drag");
            SelectTool("linecheck:wall");Check(LineBuildTool(_tool)&&!LineBuildTool("Chair"),"data-defined solid buildings share line tool; furniture stays single");
            CommitBuildLine(new(42,45),new(42,43));
            Check(_world.Jobs.Count(j=>j.BuildDefinitionId=="linecheck:wall")==3&&_world.Available("wood")==14,"reverse Mod line retains ID and custom costs");
            foreach(var j in _world.Jobs.ToArray())_world.CancelJob(j);
            _world.Items.Clear();_world.SpawnItem("cobblestone",4,new(48,48));SelectTool("StoneBricks");CommitBuildLine(new(44,43),new(48,43));
            Check(_world.Jobs.Count==2&&_world.Available("cobblestone")==0,"insufficient materials stop without over-reserving");
            _world.DeveloperFinishPlans();Check(_world.Solid(new(44,43))&&!_world.Walkable(new(44,43)),"completion creates actual obstacle");
            _world.SpawnItem("sand",2,new(48,48));BeginBlockPlacement(catalog.Placement("sand","nodwarf:sand"),0);CommitPlacementArea(new(46,46),new(47,46));
            var sand=_world.Jobs.Single(j=>j.Cell==new Cell(46,46));Check((_planVisuals[sand.Id].Faces&(BlockFaces.East|BlockFaces.Bottom))==0,"generic placement uses same ghost face policy");
            if(DisplayServer.GetName()!="headless")
            {
                _focus=WorldPosition(new Cell(43,42));_camera.Size=13;PositionCamera();SelectTool("select");
                await Frame();await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/construction-line.png"));
            }
            // A 3x3 patch crosses both chunk boundaries; only its exterior is outlined.
            _world.SpawnItem("cobblestone",100,new(18,18));
            for(int x=15;x<=17;x++)for(int z=15;z<=17;z++)Check(_world.QueueBuild(new(x,z),BuildKind.StoneBricks,false),"outline patch plan");
            SyncWorld();CheckConstructionOutlines();
            var center=_world.Jobs.Single(j=>j.Cell==new Cell(16,16));
            Check(_constructionVisuals[center.Id].OutlineMask==0,"patch interior has no outline instance");
            Check(_constructionVisuals[_world.Jobs.Single(j=>j.Cell==new Cell(15,15)).Id].OutlineMask==5,"chunk boundaries do not introduce shared edges");
            _selectedCell=center.Cell;UpdateConstructionVisuals();CheckConstructionOutlines();Check(_constructionVisuals[center.Id].OutlineMask==15,"interior cell remains individually selectable");
            _selectedCell=null;UpdateConstructionVisuals();_world.CancelJob(center);SyncWorld();CheckConstructionOutlines();
            var rim=_constructionVisuals.Values.Where(v=>v.Job.Cell.X is >=15 and <=17&&v.Job.Cell.Z is >=15 and <=17).ToArray();
            Check(rim.Sum(v=>System.Numerics.BitOperations.PopCount((uint)v.OutlineMask))==16,"cancelled interior exposes four hole edges");
            Check(_world.QueueBuild(new(15,15,1),BuildKind.StoneBricks,false),"upper layer plan");SyncWorld();CheckConstructionOutlines();
            Check(_constructionVisuals[_world.Jobs.Single(j=>j.Cell==new Cell(15,15,1)).Id].OutlineMask==15,"different heights retain independent contours");
            GD.Print("CONSTRUCTION_OUTLINE_OK: shared edges, patch interiors, chunk boundaries, cancellation holes, independent heights and selected-cell highlight");
            GD.Print("CONSTRUCTION_LINE_OK: mouse drag/release/cancel, axis lock, costs, Mod IDs, shared-face geometry, selection-only areas, navigation, save and completion");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
