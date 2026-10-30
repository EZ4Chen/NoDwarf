using Godot;
using NoDwarf.Simulation;

namespace NoDwarf;

public partial class Main
{
    private readonly Dictionary<Cell, (Building Data, Node3D Root, int Faces, float Height, bool GateOpen)> _buildingVisuals = new();
    private readonly Dictionary<int, (Cell Cell, BuildKind? Build, bool Dig, int Height, int Facing, BlockFaces Faces, float Top, int OutlineMask, Node3D Root)> _planVisuals = new();
    private int _faceTerrainRevision=-1;

    private void SyncStructuresAndPlans()
    {
        bool reset = _visualWorld != _world;
        BeginFallVisuals(reset);
        var view=CurrentView;
        bool wholeSliceChanged=reset||_visualView.Top!=view.Top||_visualView.RevealAll!=view.RevealAll;
        var dirtyFaces=new HashSet<Cell>();
        void Dirty(Cell c){dirtyFaces.Add(c);foreach(var (_,d) in BlockExposure.Neighbors)dirtyFaces.Add(c+d);}
        if(!reset)
        {
            foreach(var old in _buildingVisuals)
                if(!_world.Buildings.TryGetValue(old.Key,out var current)||current!=old.Value.Data)Dirty(old.Key);
            foreach(var current in _world.Buildings)
                if(!_buildingVisuals.ContainsKey(current.Key))Dirty(current.Key);
            foreach(var c in _world.Terrain.ChangesSince(_faceTerrainRevision))Dirty(c);
            // A view stamp alone does not change geometry. Local cuts affect only
            // their own shapes and neighboring exposed faces.
            if(!wholeSliceChanged&&!ReferenceEquals(_visualView.WallCaps,view.WallCaps))
            {
                if(_visualView.WallCaps is not null)foreach(var c in _visualView.WallCaps.Keys)
                    if(_visualView.CellTop(c)!=view.CellTop(c))Dirty(c);
                if(view.WallCaps is not null)foreach(var c in view.WallCaps.Keys)
                    if(_visualView.CellTop(c)!=view.CellTop(c))Dirty(c);
            }
        }
        _faceTerrainRevision=_world.Terrain.Revision;
        if (reset)
        {
            Clear(_structures); Clear(_ghosts);
            _buildingVisuals.Clear(); _planVisuals.Clear();
            _constructionVisuals.Clear();
            ResetConstructionOutlines();
        }
        using (var timing = _world.Probe?.Measure("view.sync-buildings") ?? default)
        {
            foreach (var cell in _buildingVisuals.Keys.ToArray())
                if (!_world.Buildings.ContainsKey(cell) || !VisibleAt(cell))
                { RemoveVisual(_buildingVisuals[cell].Root); _buildingVisuals.Remove(cell); }
            foreach (var building in _world.Buildings.Values)
            {
                if (!VisibleAt(building.Cell)) continue;
                bool wall=BlockCatalog.IsWall(building.Kind),gateOpen=_world.GateOpen(building.Cell);
                float height=wall?Math.Clamp(view.CellTop(building.Cell)-building.Cell.Level,0,1):1;
                int faces=!wholeSliceChanged&&!dirtyFaces.Contains(building.Cell)&&_buildingVisuals.TryGetValue(building.Cell,out var cached)
                    ?cached.Faces:wall?(int)BlockExposure.BuildingFaces(_world,view,building.Cell):(int)BlockFaces.All;
                if (_buildingVisuals.TryGetValue(building.Cell, out var old))
                {
                    if (old.Data == building && old.Faces==faces && old.Height==height && (building.Kind!=BuildKind.Floodgate||old.GateOpen==gateOpen))
                    {
                        if(building.Kind==BuildKind.Campfire&&old.Root.FindChild("Flame",true,false) is Node3D flame)flame.Visible=_world.FireFuel.GetValueOrDefault(building.Cell)>0;
                        continue;
                    }
                    RemoveVisual(old.Root);
                }
                var root = new Node3D(); _structures.AddChild(root);
                root.SetMeta("gate_open",gateOpen);
                AddBuildingVisual(root, building.Cell, building.Kind, facing: building.Facing,definitionId:building.DefinitionId);
                _buildingVisuals[building.Cell] = (building, root, faces, height, gateOpen);
                _world.Probe?.Count("view.buildings-created");
            }
        }
        using (var timing = _world.Probe?.Measure("view.sync-plans") ?? default)
        {
            var plans = _world.Jobs.Where(j => j.Construction).ToDictionary(j => j.Id);
            var solidPlans=SolidPlanCells();
            bool PlanVisible(DemoJob job)=>job.Dig&&CurrentView.Level is not null ? job.Cell.Level==ActiveLevel : VisibleAt(job.Cell);
            var digGroups=plans.Values.Where(j=>j.Dig&&PlanVisible(j)).ToDictionary(j=>j.Cell,j=>j.DigRegion.Length==0?"job:"+j.Id:"region:"+j.DigRegion);
            foreach (int id in _planVisuals.Keys.ToArray())
                if (!plans.TryGetValue(id, out var job) || !PlanVisible(job))
                { RemoveVisual(_planVisuals[id].Root); _planVisuals.Remove(id); RemoveConstructionVisual(id); }
            foreach (var job in plans.Values)
            {
                if (!PlanVisible(job)) continue;
                var faces=job.BuildingWork&&_world.BuildBlock(job).Shape==BlockShape.Solid?BlockExposure.PlanFaces(_world,view,job.Cell,solidPlans):BlockFaces.All;
                float top=view.CellTop(job.Cell);
                int outlineMask=job.Dig?CellBoundary.Mask(job.Cell,n=>digGroups.TryGetValue(n,out var group)&&group==digGroups[job.Cell]&&view.CellTop(n)==top):15;
                if (_planVisuals.TryGetValue(job.Id, out var old))
                {
                    if (old.Cell == job.Cell && old.Build == job.Build && old.Dig == job.Dig && old.Height == job.DigHeight && old.Facing == job.Facing&&old.Faces==faces&&old.Top==top&&old.OutlineMask==outlineMask) continue;
                    RemoveVisual(old.Root);
                    RemoveConstructionVisual(job.Id);
                }
                var root = new Node3D(); _ghosts.AddChild(root);
                if (job.Dig) DrawDig(root, job.Cell, job.DigHeight == 3, job.Facing,outlineMask);
                else AddConstructionVisual(root, job,faces);
                _planVisuals[job.Id] = (job.Cell, job.Build, job.Dig, job.DigHeight, job.Facing, faces, top, outlineMask, root);
                _world.Probe?.Count("view.plans-created");
            }
        }
        SyncConstructionOutlines();
        _visualWorld = _world; _visualView = CurrentView;
    }

    private static void RemoveVisual(Node node)
    {
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    private void CheckIncrementalVisuals()
    {
        _world = new DemoWorld(flat: true); _world.DeveloperResidents(false); _overview.World = _world;
        _clock.Paused = true; _selectedWorker = -1; CreatePeople();
        _heightSelected = true; _placement = 0; _revealUnknown = false;
        _foundation = null; _layerOffset = 0; _cutaway = true; _interiorView = false; _floorWalls = false;
        var first = new Cell(20,20); var second = new Cell(21,20);
        _world.Buildings[first] = new(first, BuildKind.Wall); _world.Buildings[second] = new(second, BuildKind.Wall);
        if (!_world.QueueBuild(new Cell(22,22), BuildKind.Wall, announce:false)) throw new InvalidOperationException("Visual test plan");
        SyncWorld();
        CheckConstructionOutlines();
        ulong firstId = _buildingVisuals[first].Root.GetInstanceId();
        ulong secondId = _buildingVisuals[second].Root.GetInstanceId();
        int jobId = _world.Jobs.Single().Id; ulong planId = _planVisuals[jobId].Root.GetInstanceId();
        _world.Terrain.Excavate(new Cell(60,60,-3)); SyncWorld();
        if (_buildingVisuals[first].Root.GetInstanceId() != firstId || _planVisuals[jobId].Root.GetInstanceId() != planId)
            throw new InvalidOperationException("Remote excavation rebuilt unchanged visuals");
        _world.Buildings[second] = new(second, BuildKind.Planks);
        _world.QueueBuild(new Cell(24,22), BuildKind.Wall, announce:false); SyncWorld();
        if (_buildingVisuals[first].Root.GetInstanceId() != firstId || _buildingVisuals[second].Root.GetInstanceId() == secondId || _planVisuals[jobId].Root.GetInstanceId() != planId)
            throw new InvalidOperationException("Incremental replacement changed unrelated visuals");
        var buried = new Cell(30,30,-3); _world.Buildings[buried] = new(buried, BuildKind.Wall); SyncWorld();
        if (_buildingVisuals.ContainsKey(buried)) throw new InvalidOperationException("Undiscovered block leaked into view");
        _world.Terrain.Excavate(buried + new Cell(1,0)); SyncWorld();
        if (!_buildingVisuals.ContainsKey(buried)) throw new InvalidOperationException("Excavation did not reveal adjacent block");
        _world.Buildings.Remove(second); _world.CancelJob(_world.Jobs.Single(j=>j.Id==jobId)); SyncWorld();
        if (_buildingVisuals.ContainsKey(second) || _planVisuals.ContainsKey(jobId)) throw new InvalidOperationException("Removed visuals retained");
        _layerOffset = -3; SyncWorld();
        CheckConstructionOutlines();
        if (_buildingVisuals.ContainsKey(first) || !_buildingVisuals.ContainsKey(buried)) throw new InvalidOperationException("Floor change failed to update cached visuals");
        GD.Print("VISUAL_CACHE_OK: retained nodes, changed materials, new/deleted plans, terrain discovery and floor changes");
    }
}
