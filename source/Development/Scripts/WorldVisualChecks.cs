using Godot;
using NoDwarf.Simulation;
namespace NoDwarf;
public partial class Main
{
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
