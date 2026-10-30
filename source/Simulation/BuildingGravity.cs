namespace NoDwarf.Simulation;

public sealed record BuildingFall(Building[] Buildings,int Distance,int[] ItemIds);

public sealed partial class DemoWorld
{
    public double FallBreakChance {get;set;}=.25;
    public Queue<BuildingFall> VisualFalls {get;}=new();
    private bool BreakOnLanding(Building b,int distance)
    {
        uint h=unchecked((uint)(Terrain.Seed^b.Cell.X*73856093^b.Cell.Z*19349663^b.Cell.Level*83492791^(int)b.Kind*397^distance));
        h^=h>>16;h*=0x7feb352d;h^=h>>15;h*=0x846ca68b;h^=h>>16;
        return h/(double)uint.MaxValue<Math.Clamp(FallBreakChance,0,1);
    }
    private static readonly Cell[] SupportDirections={new(1,0),new(-1,0),new(0,1),new(0,-1),new(0,0,1),new(0,0,-1)};
    private readonly HashSet<Cell> _supportDirty=new();
    private IEnumerator<int>? _supportWork;
    private long _supportEpoch,_supportWorkEpoch;
    private readonly HashSet<Cell> _supportWaiting=new();
    private int _supportRetry;
    public long SupportVisits {get;private set;}
    public int FallenBuildings {get;private set;}
    public bool SupportPending=>_supportWork is not null||_supportDirty.Count>0;
    private void SupportChanged(Cell c)
    {
        _supportSafety.Clear();
        bool touches=_supportDirty.Contains(c);
        if(Buildings.ContainsKey(c)){_supportDirty.Add(c);touches=true;}
        foreach(var d in SupportDirections)if(Buildings.ContainsKey(c+d)){_supportDirty.Add(c+d);touches=true;}
        foreach(int height in new[]{-2,2})
        {var above=c+new Cell(0,0,height);if(Buildings.ContainsKey(above)){_supportDirty.Add(above);touches=true;}}
        // Unrelated mining/tree changes must not repeatedly restart a large search.
        if(touches)_supportEpoch++;
    }
    // Event-driven, resumable graph traversal: idle worlds do no support scanning.
    // A commit moves one connected group atomically so rendering never sees half a move.
    public void AdvanceSupport(int budget=512)
    {
        using var timing=Probe?.Measure("physics.support")??default;
        _supportSafety.Clear();SettleResidents();
        if(_supportWaiting.Count>0&&++_supportRetry>=30)
        { _supportRetry=0;foreach(var c in _supportWaiting)_supportDirty.Add(c);_supportWaiting.Clear(); }
        while(budget-->0)
        {
            if(_supportWork is not null&&_supportWorkEpoch!=_supportEpoch){_supportWork.Dispose();_supportWork=null;}
            if(_supportWork is null)
            {
                if(_supportDirty.Count==0)return;
                var c=_supportDirty.First();
                if(!Buildings.ContainsKey(c)){_supportDirty.Remove(c);continue;}
                _supportWorkEpoch=_supportEpoch;_supportWork=CheckSupport(c).GetEnumerator();
            }
            SupportVisits++;
            if(!_supportWork.MoveNext()){_supportWork.Dispose();_supportWork=null;}
        }
    }
    private IEnumerable<int> CheckSupport(Cell start)
    {
        var cells=new HashSet<Cell>{start};var queue=new Queue<Cell>();queue.Enqueue(start);bool anchored=false;
        while(queue.TryDequeue(out var c))
        {
            foreach(var d in SupportDirections)
            {
                var n=c+d;
                if(NaturalSolid(n)||NaturalRamp(n))anchored=true;
                if(Buildings.ContainsKey(n)&&cells.Add(n))queue.Enqueue(n);
            }
            yield return 0;
        }
        if(anchored){foreach(var c in cells)_supportDirty.Remove(c);yield break;}
        var buildings=cells.Select(c=>Buildings[c]).ToArray();
        var volume=new HashSet<Cell>(cells);
        foreach(var b in buildings)if(TallFixture(b.Kind))volume.Add(b.Cell+new Cell(0,0,1));
        // Carry residents standing on this group; other residents stop it safely.
        var columns=cells.Select(c=>(c.X,c.Z)).ToHashSet();
        bool Relevant(DemoWorker w)=>columns.Contains((w.Tile.X,w.Tile.Z))||(w.Path.TryPeek(out var next)&&columns.Contains((next.X,next.Z)));
        var people=Actors.Where(Relevant).Select(w=>(w,w.X,w.Z,w.Level,w.TravelFrom)).ToArray();
        var riders=people.Select(p=>p.w).Where(w=>cells.Contains(w.Tile+new Cell(0,0,-1))||(cells.Contains(w.Tile)&&LadderAt(w.Tile))).ToArray();
        var obstacles=people.Select(p=>p.w).Except(riders).ToArray();
        int distance=0;bool residentBlocked=false;
        while(true)
        {
            bool blocked=false;
            foreach(var b in buildings)
            {
                var target=b.Cell+new Cell(0,0,-distance-1);
                for(int h=0;h<(TallFixture(b.Kind)?2:1);h++)
                {
                    var p=target+new Cell(0,0,h);
                    if(!Inside(p)||NaturalSolid(p)||NaturalRamp(p)||(!volume.Contains(p)&&Occupied(p)))blocked=true;
                    if(obstacles.Any(w=>IntersectsWorker(p,BuildKind.Wall,w)))
                    {blocked=true;residentBlocked=true;}
                }
                yield return 0;
            }
            if(blocked)break;
            distance++;
        }
        if(people.Length!=Actors.Count(Relevant)||people.Any(p=>!Actors.Contains(p.w)||p.X!=p.w.X||p.Z!=p.w.Z||p.Level!=p.w.Level||p.TravelFrom!=p.w.TravelFrom))yield break;
        foreach(var c in cells)_supportDirty.Remove(c);
        if(residentBlocked)_supportWaiting.Add(start+new Cell(0,0,-distance));
        if(distance==0)yield break;
        var delta=new Cell(0,0,-distance);
        var damage=cells.Where(_blockDamage.ContainsKey).Select(c=>_blockDamage[c]).ToArray();
        var fires=cells.Where(FireFuel.ContainsKey).Select(c=>(Cell:c,Fuel:FireFuel[c])).ToArray();
        var gates=cells.Where(_openFloodgates.Contains).ToArray();
        foreach(var c in cells){Buildings.Remove(c);FireFuel.Remove(c);_openFloodgates.Remove(c);}
        foreach(var b in buildings){var target=b.Cell+delta;Buildings[target]=b with{Cell=target};}
        foreach(var d in damage)_blockDamage[d.Cell+delta]=d with{Cell=d.Cell+delta};
        foreach(var f in fires)FireFuel[f.Cell+delta]=f.Fuel;
        foreach(var g in gates){_openFloodgates.Add(g+delta);FluidGeometryChanged(g+delta);}
        foreach(var w in riders){StopMovement(w);w.Level-=distance;ApplyFallInjury(w,distance);}
        foreach(var job in Jobs.Where(j=>(j.Production||j.Refuel)&&cells.Contains(j.Cell)))
        {Release(job);job.Cell+=delta;}
        // O(buildings + items), not a nested item-by-building scan.
        var destination=volume.Select(c=>c+delta).ToHashSet();
        var tops=destination.GroupBy(c=>(c.X,c.Z)).ToDictionary(g=>g.Key,g=>g.Max(c=>c.Level)+1);
        foreach(var item in Items.Where(i=>i.Owner==""))
            if(destination.Contains(item.Cell))item.Cell=item.Cell with{Level=tops[(item.Cell.X,item.Cell.Z)]};
        int broken=0;var debrisIds=new HashSet<int>();
        if(!residentBlocked)foreach(var b in buildings)
        {
            var target=b.Cell+delta;
            // Do not remove the floor from under a rider until character falling exists.
            if(riders.Any(w=>w.Tile==target+new Cell(0,0,1)||(b.Kind==BuildKind.Ladder&&w.Tile==target))||!BreakOnLanding(b,distance))continue;
            foreach(var job in Jobs.Where(j=>(j.Production||j.Refuel)&&j.Cell==target).ToArray())CancelJob(job);
            Buildings.Remove(target);FireFuel.Remove(target);_openFloodgates.Remove(target);
            foreach(var (item,count) in BuildingDefinition(b).Cost)debrisIds.Add(SpawnItem(item,count,target).Id);
            broken++;
        }
        if(VisualFalls.Count>=16)VisualFalls.Dequeue();
        // Bounded presentation history; simulations without a renderer cannot accumulate indefinitely.
        VisualFalls.Enqueue(new(buildings.Take(2048).ToArray(),distance,debrisIds.ToArray()));
        SettleItems();Revision++;FallenBuildings+=buildings.Length;ValidateWorkshop();
        Log($"失去支撑：{buildings.Length} 个建筑构件下落 {distance} 米，{broken} 个变为原料。");
    }
}
