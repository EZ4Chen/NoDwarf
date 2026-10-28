namespace NoDwarf.Simulation;

// Shared, single-threaded navigation. Regions are connected components inside an
// 8x8 column of voxels, NOT floor numbers: overlapping floors stay disconnected
// unless real stairs connect them. Optional region routes guide a fine-grid A*
// corridor; exact A* is the default selected by the benchmark.
public sealed class WorldNavigation
{
    public const int ChunkSize = 8;
    private const int Columns = (DemoWorld.Width + ChunkSize - 1) / ChunkSize;
    private const int Rows = (DemoWorld.Depth + ChunkSize - 1) / ChunkSize;
    private readonly DemoWorld _world;
    private readonly Dictionary<Cell, Node> _nodes = new();
    private readonly Dictionary<int, Region> _regions = new();
    private readonly Chunk[,] _chunks = new Chunk[Columns, Rows];
    private readonly PriorityQueue<(Node Node, int Cost), (int Total, int Remaining)> _open = new();

    private readonly Dictionary<int,HashSet<int>> _regionReach=new();
    private readonly HashSet<Chunk> _linksDirty=new();
    private readonly Queue<int> _regionQueue=new();
    private readonly List<int> _finishOrder=new();
    private readonly Stack<(int Id,HashSet<int>.Enumerator Edges)> _regionDfs=new();
    private readonly List<HashSet<int>> _strongEdges=new();
    private int _strongCount;
    private long _search;
    private int _nextRegion;
    private bool _dirty = true;
    public long Version { get; private set; }
    public long Searches { get; private set; }
    public long ExpandedNodes { get; private set; }
    public long RebuiltChunks { get; private set; }
    public long RejectedUnreachable { get; private set; }
    public long HierarchicalSearches { get; private set; }
    public bool UseHierarchy { get; set; }
    public int CachedNodes => _nodes.Count;

    private sealed class Node(Cell cell)
    {
        public readonly Cell Cell = cell;
        public Cell[] Edges = Array.Empty<Cell>();
        public int Region;
        public long Search;
        public int Cost;
        public Node? Previous;
    }
    private sealed class Region(int x, int z)
    {
        public readonly int X = x, Z = z;
        public readonly HashSet<int> Edges = new();
        public readonly HashSet<int> Incoming=new();
        public int Component,StrongComponent;
        public bool Visited;
    }
    private sealed class Chunk
    {
        public bool Dirty = true;
        public long Version;
        public readonly List<Cell> Cells = new();
        public readonly List<int> Regions = new();
        public readonly List<Node> Portals = new();
        public readonly HashSet<Cell> Candidates = new();
    }

    public WorldNavigation(DemoWorld world)
    {
        _world = world;
        for (int x = 0; x < Columns; x++)
            for (int z = 0; z < Rows; z++) _chunks[x, z] = new Chunk();
    }

    public void Invalidate(Cell changed)
    {
        Version++;
        _dirty = true;
        if (DemoWorld.Inside(changed))
        {
            var candidates = _chunks[changed.X / ChunkSize, changed.Z / ChunkSize].Candidates;
            // Excavation exposes a possible standing cell; a placed solid block
            // exposes its top. Retain these sparse candidates through later edits.
            candidates.Add(changed);
            candidates.Add(changed + new Cell(0, 0, 1));
        }
        // Walkable depends on support and headroom in the same column; Connected
        // also depends on the adjacent column. Whole-height chunks cover both.
        for (int x = Math.Max(0, changed.X - 1); x <= Math.Min(DemoWorld.Width - 1, changed.X + 1); x++)
            for (int z = Math.Max(0, changed.Z - 1); z <= Math.Min(DemoWorld.Depth - 1, changed.Z + 1); z++)
            {
                var chunk = _chunks[x / ChunkSize, z / ChunkSize];
                chunk.Dirty = true;
                chunk.Version = Version;
            }
    }

    // Checks live geometry without rebuilding the search graph. A remote edit
    // does not cancel an otherwise valid path; a new shortcut does not interrupt it.
    public bool PathValid(Cell start, Queue<Cell> path, long checkedVersion)
    {
        if (checkedVersion == Version) return true;
        var from = start;
        foreach (var to in path)
        {
            if (!DemoWorld.Inside(from) || !DemoWorld.Inside(to)) return false;
            if (_chunks[from.X / ChunkSize, from.Z / ChunkSize].Version > checkedVersion ||
                _chunks[to.X / ChunkSize, to.Z / ChunkSize].Version > checkedVersion)
                if (!_world.Walkable(from) || !_world.Walkable(to) || (from != to && !_world.Connected(from, to))) return false;
            from = to;
        }
        return true;
    }

    private void EnsureGraph()
    {
        if (!_dirty) return;
        using var timing = _world.Probe?.Measure("navigation.rebuild-graph") ?? default;
        using var trace=DiagnosticTrace.Span("navigation.rebuild",revision:Version);
        _linksDirty.Clear();
        using(var chunksTiming=_world.Probe?.Measure("navigation.rebuild-chunks")??default)
        for (int x = 0; x < Columns; x++)
            for (int z = 0; z < Rows; z++)
                if (_chunks[x, z].Dirty)
                {
                    Rebuild(x,z);_linksDirty.Add(_chunks[x,z]);
                    // Replacing region IDs also changes links from adjacent chunks.
                    if(x>0)_linksDirty.Add(_chunks[x-1,z]);
                    if(x+1<Columns)_linksDirty.Add(_chunks[x+1,z]);
                    if(z>0)_linksDirty.Add(_chunks[x,z-1]);
                    if(z+1<Rows)_linksDirty.Add(_chunks[x,z+1]);
                }
        _regionReach.Clear();
        using(var linksTiming=_world.Probe?.Measure("navigation.relink-regions")??default)
        foreach(var chunk in _linksDirty)
        {
            foreach(int id in chunk.Regions)
            {
                var region=_regions[id];
                foreach(int next in region.Edges)_regions[next].Incoming.Remove(id);
                region.Edges.Clear();
            }
            foreach(var node in chunk.Portals)
                foreach(var cell in node.Edges)
                    if(_nodes.TryGetValue(cell,out var next)&&next.Region!=node.Region&&_regions[node.Region].Edges.Add(next.Region))
                        _regions[next.Region].Incoming.Add(node.Region);
        }
        _world.Probe?.Count("navigation.chunks-relinked",_linksDirty.Count);
        BuildConnectivity();
        _dirty = false;
    }

    private void BuildConnectivity()
    {
        using var timing=_world.Probe?.Measure("navigation.label-components")??default;
        foreach(var r in _regions.Values){r.Component=0;r.StrongComponent=-1;r.Visited=false;}
        // Weak components remain a coarse rejection filter for existing callers.
        var queue=_regionQueue;queue.Clear();int component=0;
        foreach(var (id,region) in _regions)
        {
            if(region.Component!=0)continue;
            region.Component=++component;queue.Enqueue(id);
            while(queue.TryDequeue(out int current))
            {
                var r=_regions[current];
                foreach(int next in r.Edges)if(_regions[next].Component==0){_regions[next].Component=component;queue.Enqueue(next);}
                foreach(int next in r.Incoming)if(_regions[next].Component==0){_regions[next].Component=component;queue.Enqueue(next);}
            }
        }
        // Iterative Kosaraju: normal bidirectional terrain collapses to very few
        // strongly connected groups; directed two-metre drops stay directed.
        _finishOrder.Clear();_regionDfs.Clear();
        foreach(var (id,region) in _regions)
        {
            if(region.Visited)continue;
            region.Visited=true;_regionDfs.Push((id,region.Edges.GetEnumerator()));
            while(_regionDfs.TryPop(out var frame))
            {
                if(frame.Edges.MoveNext())
                {
                    int next=frame.Edges.Current;_regionDfs.Push(frame);
                    var r=_regions[next];
                    if(!r.Visited){r.Visited=true;_regionDfs.Push((next,r.Edges.GetEnumerator()));}
                }
                else _finishOrder.Add(frame.Id);
            }
        }
        _strongCount=0;
        for(int i=_finishOrder.Count-1;i>=0;i--)
        {
            int id=_finishOrder[i];if(_regions[id].StrongComponent>=0)continue;
            int strong=_strongCount++;
            if(strong==_strongEdges.Count)_strongEdges.Add(new());else _strongEdges[strong].Clear();
            _regions[id].StrongComponent=strong;queue.Enqueue(id);
            while(queue.TryDequeue(out int current))
                foreach(int next in _regions[current].Incoming)
                    if(_regions[next].StrongComponent<0){_regions[next].StrongComponent=strong;queue.Enqueue(next);}
        }
        foreach(var r in _regions.Values)foreach(int next in r.Edges)
        {
            int target=_regions[next].StrongComponent;
            if(target!=r.StrongComponent)_strongEdges[r.StrongComponent].Add(target);
        }
        _world.Probe?.Count("navigation.strong-components",_strongCount);
    }

    private void Rebuild(int x, int z)
    {
        var chunk = _chunks[x, z];
        foreach (var cell in chunk.Cells) _nodes.Remove(cell);
        foreach(var id in chunk.Regions)
        {
            var old=_regions[id];
            foreach(int next in old.Edges)_regions[next].Incoming.Remove(id);
            foreach(int previous in old.Incoming)_regions[previous].Edges.Remove(id);
            _regions.Remove(id);
        }
        chunk.Cells.Clear(); chunk.Regions.Clear(); chunk.Portals.Clear();
        int xEnd = Math.Min(DemoWorld.Width, (x + 1) * ChunkSize);
        int zEnd = Math.Min(DemoWorld.Depth, (z + 1) * ChunkSize);
        for (int cx = x * ChunkSize; cx < xEnd; cx++)
            for (int cz = z * ChunkSize; cz < zEnd; cz++) chunk.Candidates.Add(_world.Surface(cx, cz));
        // No full-depth scan: deep untouched rock and unsupported air cannot be
        // navigation nodes. Walkable remains the authority for every candidate.
        foreach (var cell in chunk.Candidates)
        {
            if (!_world.Walkable(cell)) continue;
            var node = new Node(cell);
            node.Edges = _world.Neighbors(cell).Where(next => _world.Connected(cell, next)).ToArray();
            _nodes.Add(cell, node); chunk.Cells.Add(cell);
            if (node.Edges.Any(next => next.X / ChunkSize != x || next.Z / ChunkSize != z || cell.Level-next.Level==2)) chunk.Portals.Add(node);
        }
        var queue = new Queue<Node>();
        foreach (var cell in chunk.Cells)
        {
            var node = _nodes[cell];
            if (node.Region != 0) continue;
            int id = ++_nextRegion;
            _regions.Add(id, new Region(x, z)); chunk.Regions.Add(id);
            node.Region = id; queue.Enqueue(node);
            while (queue.TryDequeue(out var current))
                foreach (var nextCell in current.Edges)
                    if (nextCell.X / ChunkSize == x && nextCell.Z / ChunkSize == z &&
                        _nodes.TryGetValue(nextCell, out var next) && next.Region == 0 && current.Cell.Level-nextCell.Level!=2)
                    { next.Region = id; queue.Enqueue(next); }
        }
        chunk.Dirty = false; RebuiltChunks++;
    }

    private static int Distance(Cell a, Cell b) =>
        Math.Max(Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z), Math.Abs(a.Level - b.Level));

    // Reuses component labels; no path search or voxel flood for each material stack.
    public bool CanReach(Cell start, Cell end)
    {
        EnsureGraph();
        return _nodes.TryGetValue(start,out var a) && _nodes.TryGetValue(end,out var b)
            && RegionsReachable(a.Region,b.Region);
    }

    internal int TravelRegionAt(Cell cell){EnsureGraph();return _nodes.TryGetValue(cell,out var n)?n.Region:0;}
    private bool RegionsReachable(int source,int target)
    {
        var a=_regions[source];var b=_regions[target];
        if(a.Component!=b.Component)return false;
        if(a.StrongComponent==b.StrongComponent)return true;
        return ReachableComponents(a.StrongComponent).Contains(b.StrongComponent);
    }
    private HashSet<int> ReachableComponents(int source)
    {
        if(_regionReach.TryGetValue(source,out var found))return found;
        using var timing=_world.Probe?.Measure("navigation.region-reachability")??default;
        _world.Probe?.Count("navigation.reachability-builds");
        var result=new HashSet<int>{source};var queue=_regionQueue;queue.Clear();queue.Enqueue(source);
        while(queue.TryDequeue(out int current))foreach(int next in _strongEdges[current])if(result.Add(next))queue.Enqueue(next);
        if(_regionReach.Count>=128){_world.Probe?.Count("navigation.reachability-cache-clears");_regionReach.Clear();}
        _regionReach[source]=result;return result;
    }

    // Component IDs are valid only until the next geometry change. Callers may
    // share them within one synchronous scheduling batch, never persist them.
    internal int ComponentAt(Cell cell)
    {
        EnsureGraph();
        return _nodes.TryGetValue(cell, out var node) ? _regions[node.Region].Component : 0;
    }

    public Queue<Cell>? FindPath(Cell start, Cell end) => FindPathCore(start, end, UseHierarchy);
    internal Queue<Cell>? FindExactPath(Cell start, Cell end) => FindPathCore(start, end, false);
    internal Queue<Cell>? FindHierarchicalPath(Cell start, Cell end) => FindPathCore(start, end, true);

    private Queue<Cell>? FindPathCore(Cell start, Cell end, bool hierarchical)
    {
        using var timing = _world.Probe?.Measure("navigation.point-search") ?? default;
        Searches++;
        EnsureGraph();
        if (!_nodes.TryGetValue(start, out var source) || !_nodes.TryGetValue(end, out var goal)) return null;
        if (!RegionsReachable(source.Region,goal.Region))
        { RejectedUnreachable++; return null; }
        HashSet<int>? corridor = null;
        if (hierarchical && source.Region != goal.Region && Distance(start, end) >= ChunkSize * 3)
        {
            corridor = Corridor(source.Region, goal.Region);
            HierarchicalSearches++;
        }
        return Search(source, goal, corridor);
    }

    private HashSet<int> Corridor(int start, int end)
    {
        var open = new PriorityQueue<(int Id, int Cost), (int Total, int Remaining)>();
        var costs = new Dictionary<int, int> { [start] = 0 };
        var previous = new Dictionary<int, int> { [start] = start };
        int Heuristic(int id) => Math.Abs(_regions[id].X - _regions[end].X) + Math.Abs(_regions[id].Z - _regions[end].Z);
        open.Enqueue((start, 0), (Heuristic(start), Heuristic(start)));
        while (open.TryDequeue(out var item, out _))
        {
            if (costs[item.Id] != item.Cost) continue;
            if (item.Id == end) break;
            foreach (int next in _regions[item.Id].Edges)
            {
                int cost = item.Cost + 1;
                if (costs.TryGetValue(next, out int old) && old <= cost) continue;
                costs[next] = cost; previous[next] = item.Id;
                open.Enqueue((next, cost), (cost + Heuristic(next), Heuristic(next)));
            }
        }
        var result = new HashSet<int> { end };
        for (int id = end; id != start;) { id = previous[id]; result.Add(id); }
        // One neighboring region gives fine search room to choose a better crossing
        // and avoids forcing units to the center of a portal. This remains approximate.
        foreach (int id in result.ToArray()) result.UnionWith(_regions[id].Edges);
        return result;
    }

    private Queue<Cell>? Search(Node source, Node goal, HashSet<int>? corridor)
    {
        _search++; _open.Clear();
        source.Search = _search; source.Cost = 0; source.Previous = source;
        int initial = Distance(source.Cell, goal.Cell);
        _open.Enqueue((source, 0), (initial, initial));
        while (_open.TryDequeue(out var item, out _))
        {
            var current = item.Node;
            if (current.Cost != item.Cost) continue;
            ExpandedNodes++;
            if (current == goal) return Trace(current);
            foreach (var cell in current.Edges)
            {
                var next = _nodes[cell];
                if (corridor is not null && !corridor.Contains(next.Region)) continue;
                int cost = current.Cost + _world.TraversalCost(current.Cell,cell);
                if (next.Search == _search && next.Cost <= cost) continue;
                next.Search = _search; next.Cost = cost; next.Previous = current;
                int h = Distance(cell, goal.Cell);
                _open.Enqueue((next, cost), (cost + h, h));
            }
        }
        return null;
    }

    // Task assignment keeps exact nearest-reachable semantics. All workers reuse
    // adjacency and component labels; impossible targets never cause a voxel flood.
    public Queue<Cell>? FindNearest(Cell start, IEnumerable<Cell> targets, out Cell endpoint)
    {
        using var timing = _world.Probe?.Measure("navigation.nearest-search") ?? default;
        Searches++; endpoint = default;
        EnsureGraph();
        if (!_nodes.TryGetValue(start, out var source)) return null;
        var reachable = new HashSet<Cell>();
        foreach (var target in targets)
            if (_nodes.TryGetValue(target, out var node) && RegionsReachable(source.Region,node.Region)) reachable.Add(target);
        if (reachable.Count == 0) { RejectedUnreachable++; return null; }
        _search++; _open.Clear();
        source.Search = _search; source.Cost=0; source.Previous = source; _open.Enqueue((source,0),(0,0));
        while (_open.TryDequeue(out var entry,out _))
        {
            var current=entry.Node;if(current.Cost!=entry.Cost)continue;
            ExpandedNodes++;
            if (reachable.Contains(current.Cell)) { endpoint = current.Cell; return Trace(current); }
            foreach (var cell in current.Edges)
            {
                var next = _nodes[cell];
                int cost=current.Cost+_world.TraversalCost(current.Cell,cell);
                if (next.Search == _search&&next.Cost<=cost) continue;
                next.Search = _search; next.Cost=cost; next.Previous = current; _open.Enqueue((next,cost),(cost,0));
            }
        }
        return null;
    }

    private static Queue<Cell> Trace(Node end)
    {
        var path = new List<Cell>();
        while (end.Previous != end) { path.Add(end.Cell); end = end.Previous!; }
        path.Reverse(); return new Queue<Cell>(path);
    }
}
