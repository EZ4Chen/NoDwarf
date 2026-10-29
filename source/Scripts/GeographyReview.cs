using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;

public partial class Main
{
    private async void CheckGeographyPreview()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            var settings=new WorldGenerationSettings{Revision=2,Seed=23519,WorldSize=65,Moisture=2};
            var river=(from x in Enumerable.Range(4,57) from y in Enumerable.Range(4,57) where settings.Tile(x,y).RiverWidth>0 select(x,y)).First();
            settings=settings with {SiteX=river.x,SiteY=river.y};
            _world=new(settings.LocalSeed,region:settings.Site.Region,generation:settings);_clock.Paused=true;
            _overview.World=_world;_foundation=null;_terrainRevision=-1;_interiorInputs=null;
            _focus=WorldPosition(new(56,56,settings.Site.Height));_camera.Size=65;
            _layerOffset=0;_heightSelected=false;_cutaway=false;_revealUnknown=false;
            CreatePeople();PositionCamera();_buildMenu.Hide();_inspector.Hide();
            async Task Settle()
            {
                SyncWorld();RefreshUi();var timer=System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    if(timer.Elapsed.TotalSeconds>40)throw new InvalidOperationException("Geography rendering did not settle");
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);PumpInteriorWork();
                }while(InteriorPending||_terrainView.PendingMeshCount>0);
                await ToSignal(GetTree(),SceneTree.SignalName.PhysicsFrame);
            }
            async Task Capture(string name)
            {
                if(DisplayServer.GetName()=="headless")return;
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://.local/"+name+".png"));
            }
            await Settle();RefreshFluidVisuals();_overview.RefreshMap();await Capture("geography-river");
            Check(_waterMesh.Mesh is not null,"Actual river water has visible geometry");
            var water=_world.Terrain.InitialSurfaceWater.First();
            Check(_overview.MapPixel(water.X,water.Z)==new Color("428fba"),"Minimap shows generated river");
            foreach(var c in _world.Fluids.Cells.Keys.Where(c=>c.X==water.X&&c.Z==water.Z).ToArray())_world.Fluids.Drain(c);
            _overview.AdvanceMapRefresh(5);Check(_overview.MapPixel(water.X,water.Z)!=new Color("428fba"),"Minimap updates drained water on existing cadence");
            var floor=_world.Terrain.CavernFloors.First();
            Check(!new WorldView(floor.Level).Known(_world,floor),"Natural cave starts undiscovered");
            var roof=_world.Terrain.Caverns.Where(c=>c.X==floor.X&&c.Z==floor.Z).MaxBy(c=>c.Level)+new Cell(0,0,1);
            _world.Terrain.Excavate(roof);
            _focus=WorldPosition(floor);_camera.Size=38;_layerOffset=floor.Level-_world.Camp.Level;_heightSelected=true;_cutaway=true;_interiorView=false;
            _floorWalls=true;SelectTool("dig");PositionCamera();await Settle();await Capture("geography-cavern");
            Check(CurrentView.Known(_world,floor)&&_world.Walkable(floor),"Discovered natural cavern is viewable and walkable");
            var inside=_world.Terrain.DiscoveredCavernFloors.First(c=>c.Level==floor.Level&&new[]{new Cell(1,0),new Cell(-1,0),new Cell(0,1),new Cell(0,-1)}.All(d=>!_world.NaturalSolid(c+d)));
            var mesh=TerrainMeshBuilder.Build(new TerrainMeshSnapshot(_world.Terrain.CaptureMeshSnapshot(),CurrentView,new()),(inside.X/16*16,inside.Z/16*16));
            var center=WorldPosition(inside);
            Check(mesh.Surfaces.Any(p=>p.Key.Top&&!p.Key.Section&&!p.Key.Rim&&p.Value.Vertices.Any(v=>Math.Abs(v.Y-center.Y)<.01&&Math.Abs(v.X-center.X)<.51&&Math.Abs(v.Z-center.Z)<.51)),"Discovered cave interior has actual floor triangles, not just perimeter walls");
            Check(InteriorOcclusionBuilder.ExcavatedFloors(CaptureInteriorInput(),floor.Level).Cells.Count>100,"Natural cave floors participate in mining cutaway");
            GD.Print("GEOGRAPHY_UI_OK: river mesh, periodic water minimap, hidden/discovered cavern and mining cutaway");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
