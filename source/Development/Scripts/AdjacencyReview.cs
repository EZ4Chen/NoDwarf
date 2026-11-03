using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
    private async void ReviewAdjacency()
    {
        try
        {
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException("ADJACENCY: "+message);}
            double Area(Node root)
            {
                double area=0;
                foreach(var mesh in root.GetChildren().OfType<MeshInstance3D>())
                {var vertices=mesh.Mesh.GetFaces();for(int i=0;i<vertices.Length;i+=3)area+=(vertices[i+1]-vertices[i]).Cross(vertices[i+2]-vertices[i]).Length()/2;}
                return area;
            }
            _world=new DemoWorld(flat:true);_world.DeveloperResidents(false);_world.Jobs.Clear();_world.Items.Clear();_world.Stockpiles.Clear();_world.StorageZones.Clear();
            _world.SpawnItem("cobblestone",150,new(44,44));_world.SpawnItem("wood",100,new(44,44));
            _overview.World=_world;_clock.Paused=true;_developerMode=false;_creative=false;_foundation=null;_heightSelected=false;_cutaway=false;_interiorView=false;_floorWalls=false;_wallCaps=null;
            _focus=WorldPosition(new Cell(42,42));_camera.Size=17;_cameraYaw=0;_topDown=false;PositionCamera();CreatePeople();SelectTool("select");_buildMenu.Hide();_inspector.Hide();_worldMenu.Hide();SyncWorld();_terrainView.FlushMeshes();
            for(int facing=0;facing<4;facing++)
            {
                var shape=new Node3D();AddChild(shape);DrawBuilding(shape,new(40,40),BuildKind.PlankStairs,facing:facing,fullHeight:true);
                Check(Math.Abs(Area(shape)-6.5)<.0001,"original two-step fixture");
                BoxSurfaceCulling.Apply(shape);Check(Math.Abs(Area(shape)-5.5)<.0001,"only buried stair faces removed; exposed riser preserved for all orientations");
                Check(shape.GetChildren().OfType<MeshInstance3D>().All(m=>m.HasMeta("voxel_bounds")),"original stair picking bounds preserved");shape.Free();
            }
            var chair=new Node3D();AddChild(chair);DrawBuilding(chair,new(40,40),BuildKind.Chair,fullHeight:true);
            double chairBefore=Area(chair);BoxSurfaceCulling.Apply(chair);Check(Area(chair)<chairBefore&&Area(chair)>0,"intersecting chair parts remove hidden surfaces");chair.Free();
            var overlap=new Node3D();AddChild(overlap);Box(overlap,Vector3.Zero,Vector3.One,Mat("ffffff"));Box(overlap,new(.5f,0,0),Vector3.One,Mat("ffffff"));
            BoxSurfaceCulling.Apply(overlap);Check(Math.Abs(Area(overlap)-8)<.0001,"overlapping boxes form correct union including coplanar ownership");overlap.Free();
            var previewCell=new Cell(40,40);Check(_world.QueueBuild(previewCell,BuildKind.StoneBricks,false),"existing plan");SyncWorld();
            var job=_world.Jobs.Single();int available=_world.Available("cobblestone");long nav=_world.Navigation.Version;
            _world.Blueprint.Clear();_world.Blueprint.Add(new(new(0,0),BuildKind.StoneBricks));_world.Blueprint.Add(new(new(1,0),BuildKind.StoneBricks));
            DrawBlueprintPreview(previewCell+new Cell(1,0));
            Check((_planVisuals[job.Id].Faces&BlockFaces.East)==0,"existing plan hides its side against temporary blueprint");
            Check(Math.Abs(_hover.GetChildren().Sum(n=>Area(n))-7)<.0001,"blueprint omits its own shared face and existing plan contact");
            Check(_world.Jobs.Count==1&&_world.Available("cobblestone")==available&&_world.Navigation.Version==nav,"preview never reserves or mutates navigation");
            ClearHover();Check((_planVisuals[job.Id].Faces&BlockFaces.East)!=0,"closing preview restores existing face");
            // Same-zone edges disappear, different zones keep a boundary, removal restores a hole.
            Check(_world.SetStockpile(new(39,43),new(41,45)),"storage rectangle");SyncWorld();
            Check(_stockNodes[new(40,44)].GetMeta("outline_mask").AsInt32()==0,"storage interior no grid frame");
            Check(_world.SetStockpile(new(42,43),new(42,45)),"separate adjacent zone");SyncWorld();
            Check((_stockNodes[new(41,44)].GetMeta("outline_mask").AsInt32()&2)!=0,"different storage zone boundary retained");
            Check(_world.SetStockpile(new(40,44),new(40,44),true),"remove central storage cell");SyncWorld();
            Check((_stockNodes[new(39,44)].GetMeta("outline_mask").AsInt32()&2)!=0,"storage hole boundary restored");
            var digs=new[]{new Cell(43,40,-1),new Cell(44,40,-1),new Cell(45,40,-1)};
            foreach(var cell in digs){Check(_world.QueueDig(cell,announce:false),"dig marker");var dig=_world.Jobs.Single(j=>j.Cell==cell);dig.DigRegion="11111111111111111111111111111111";dig.DigRegionMin=digs[0];dig.DigRegionMax=digs[2];}
            SyncWorld();var middle=_world.Jobs.Single(j=>j.Cell==digs[1]);
            Check(_planVisuals[middle.Id].OutlineMask==12,"dig group removes inner edges");
            var last=_world.Jobs.Single(j=>j.Cell==digs[2]);last.DigRegion="22222222222222222222222222222222";last.DigRegionMin=last.DigRegionMax=last.Cell;SyncWorld();
            Check(_planVisuals[middle.Id].OutlineMask==14&&_planVisuals[last.Id].OutlineMask==15,"distinct dig groups retain boundary");
            _world.CancelJob(middle);SyncWorld();Check(_planVisuals[_world.Jobs.Single(j=>j.Cell==digs[0]).Id].OutlineMask==15,"cancel exposes dig perimeter");
            var saved=DemoWorld.LoadJson(_world.SaveJson());Check(saved.StorageZones.Count==_world.StorageZones.Count&&saved.Jobs.Count==_world.Jobs.Count,"current save/load unaffected");
            // A real wall can hide a terrain face; a plan cannot. Removing the wall restores it.
            var terrainCell=new Cell(60,60,-1);var above=terrainCell+new Cell(0,0,1);
            int TopVertices(HashSet<Cell> walls,WorldView view)=>TerrainMeshBuilder.Build(new(_world.Terrain.CaptureMeshSnapshot(),view,walls),(48,48)).Surfaces.Values.Sum(s=>Enumerable.Range(0,s.Vertices.Length/3).Count(i=>Enumerable.Range(0,3).All(k=>{
                var p=s.Vertices[i*3+k];return Math.Abs(p.Y)<.00001&&Math.Abs(p.X-TerrainView.Point(60,0,60).X)<=.50001&&Math.Abs(p.Z-TerrainView.Point(60,0,60).Z)<=.50001; })))*3;
            Check(TopVertices(new(),new(null))==6&&TopVertices(new(){above},new(null))==0,"terrain top occluded only by completed wall");
            Check(TopVertices(new(){above},new(-1))==6,"slice restores terrain below hidden wall");
            _world.Buildings[above]=new(above,BuildKind.StoneBricks);SyncWorld();_terrainView.FlushMeshes();_world.Buildings.Remove(above);SyncWorld();_terrainView.FlushMeshes();_terrainView.CheckAppliedState(_world,CurrentView);
            // Opaque geometry at ghost contacts remains intact, and partial caps keep the exposed side.
            var real=previewCell+new Cell(-1,0);_world.Buildings[real]=new(real,BuildKind.StoneBricks);SyncWorld();
            Check((BlockExposure.BuildingFaces(_world,CurrentView,real)&BlockFaces.East)!=0,"ghost does not erase real wall face");
            var caps=new Dictionary<Cell,float>{{real,.2f}};Check((BlockExposure.PlanFaces(_world,new(null,WallCaps:caps),previewCell,SolidPlanCells())&BlockFaces.West)!=0,"partial contact does not erase an entire ghost side");
            var forestWorld=new DemoWorld(flat:true);forestWorld.Trees.Clear();var treeRoot=new Cell(80,80);
            forestWorld.Trees[treeRoot]=new TreeState{Cell=treeRoot,MaximumHeight=4};
            int LogFaces(TerrainView.ForestData data)=>data.Batches.Where(b=>!b.Leaf&&!b.Cap).Sum(b=>b.Heights.Length);
            var originalForest=TerrainView.PrepareForest(forestWorld);int originalFaces=LogFaces(originalForest);
            forestWorld.Terrain.Excavate(treeRoot+new Cell(0,0,-1));
            Check(LogFaces(TerrainView.PrepareForest(forestWorld))==originalFaces+1,"excavation exposes trunk underside");
            var treeWall=treeRoot+new Cell(1,0);forestWorld.Buildings[treeWall]=new(treeWall,BuildKind.StoneBricks);
            Check(LogFaces(TerrainView.PrepareForest(forestWorld))==originalFaces,"opaque wall hides only trunk contact");
            var treeCut=new WorldView(null,WallCaps:new Dictionary<Cell,float>{{treeWall,.2f}},Stamp:1);
            Check(LogFaces(TerrainView.PrepareForest(forestWorld,treeCut))==originalFaces+1,"partial wall cut exposes trunk face");
            var forestView=new TerrainView{SynchronousMeshing=true};AddChild(forestView);forestView.Generate(forestWorld,new(null));int forestBuilds=forestView.TreeBuildCount;
            forestWorld.Terrain.Excavate(new(5,5,-1));forestView.Generate(forestWorld,new(null));Check(forestView.TreeBuildCount==forestBuilds,"remote excavation does not rebuild forest");
            forestWorld.Buildings.Remove(treeWall);forestView.Generate(forestWorld,new(null));Check(forestView.TreeBuildCount==forestBuilds+1,"removed tree contact refreshes forest");forestView.Free();
            Check(TerrainView.PrepareForest(forestWorld).Batches.Where(b=>b.Leaf).Sum(b=>b.Heights.Length)==originalForest.Batches.Where(b=>b.Leaf).Sum(b=>b.Heights.Length),"leaf cutouts preserved");
            // Visual evidence: blueprint row, merged storage zones, grouped dig markers and stairs.
            _world.QueueBuild(new(43,42),BuildKind.PlankStairs,false);_world.QueueBuild(new(45,42),BuildKind.Chair,false);SyncWorld();_terrainView.FlushMeshes();
            ClearHover();DrawBlueprintPreview(previewCell+new Cell(1,0));RefreshUi();
            if(DisplayServer.GetName()!="headless")
            {
                SetProcess(false);await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                Check(GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/adjacency-preview.png"))==Error.Ok,"capture");
            }
            GD.Print("ADJACENCY_OK: blueprint/transient restoration, box union area, stair risers, furniture, storage/dig group boundaries, terrain contact/cut/removal, unchanged solid/ghost policy and save");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
