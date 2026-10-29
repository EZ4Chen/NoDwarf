namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    // No rigid bodies: inspect only resident columns after support changes/at ticks.
    private void SettleResidents()
    {
        foreach(var worker in Actors)
        {
            if(CarrierOf(worker) is not null)continue;
            var from=worker.Tile;
            if(!Inside(from)||LadderAt(from)||LadderAt(from+new Cell(0,0,-1))||Occupied(from)||Occupied(from+new Cell(0,0,-1)))continue;
            // A valid planned jump has an explicit landing and its own motion.
            if(worker.Path.TryPeek(out var next)&&Connected(from,next))continue;
            var landing=from;
            while(landing.Level>TerrainMap.MinLevel+1&&!Occupied(landing+new Cell(0,0,-1)))landing+=new Cell(0,0,-1);
            int distance=from.Level-landing.Level;if(distance<=0)continue;
            if(worker.Job is {} job)Release(job);
            StopMovement(worker);worker.Level=landing.Level;
            ApplyFallInjury(worker,distance);worker.Status=distance>2?"坠落受伤":"落地";Revision++;
        }
    }
    private void ApplyFallInjury(DemoWorker worker,int distance)
    {
        if(distance<=2||worker.Person.Dead)return;
        double severity=Math.Min(100,(distance-2)*12d);
        foreach(string part in new[]{"left_leg","right_leg"})
        {
            var injury=worker.Person.Injuries.FirstOrDefault(i=>i.Part==part&&i.Kind=="fracture");
            if(injury is not null){injury.Severity=Math.Min(100,injury.Severity+severity);injury.Treated=false;}
            else if(worker.Person.Injuries.Count<40)worker.Person.Injuries.Add(new(){Part=part,Kind="fracture",Severity=severity});
        }
        worker.Person.DecisionVersion++;RefreshWeight(worker);
        worker.Person.Remember("fall",$"从 {distance} 米高处坠落，双腿受伤。",_simulationTime);
        Log($"{worker.Name}从 {distance} 米高处坠落受伤。");
    }
    // Preview removal of one support voxel without cloning or scanning the terrain.
    // Conservative at the work limit: defer the operation rather than risk a resident.
    private readonly Dictionary<Cell,bool> _supportSafety=new();
    private readonly HashSet<Cell> _supportFeet=new(),_supportVisited=new();
    private readonly Queue<Cell> _supportQueue=new();
    private bool RemovalEndangersResident(Cell removed,HashSet<Cell>? residentFeet=null)
    {
        if(Workers.Count==0)return false;
        if(_supportSafety.TryGetValue(removed,out bool cached))return cached;
        using var timing=Probe?.Measure("safety.removal-preview")??default;
        // Assignment already captured these feet for this synchronous decision.
        // Other callers refresh scratch from live residents; never cache occupancy.
        var feet=residentFeet??_supportFeet;
        if(residentFeet is null)
        {
            feet.Clear();
            foreach(var w in Workers){feet.Add(w.Tile);if(w.Path.TryPeek(out var next))feet.Add(next);}
        }
        if((LadderAt(removed)&&feet.Contains(removed))||feet.Contains(removed+new Cell(0,0,1)))return _supportSafety[removed]=true;
        var visited=_supportVisited;visited.Clear();int budget=4096;
        foreach(var direction in SupportDirections)
        {
            var start=removed+direction;
            if(!Buildings.ContainsKey(start)||!visited.Add(start))continue;
            bool anchored=false,occupied=false;var queue=_supportQueue;queue.Clear();queue.Enqueue(start);
            while(queue.TryDequeue(out var c))
            {
                if(--budget<0)return _supportSafety[removed]=true;
                occupied|=(LadderAt(c)&&feet.Contains(c))||feet.Contains(c+new Cell(0,0,1))||(TallFixture(Buildings[c].Kind)&&feet.Contains(c+new Cell(0,0,2)));
                foreach(var d in SupportDirections)
                {
                    var n=c+d;if(n==removed)continue;
                    if(NaturalSolid(n)||NaturalRamp(n))anchored=true;
                    if(Buildings.ContainsKey(n)&&visited.Add(n))queue.Enqueue(n);
                }
            }
            if(!anchored&&occupied)return _supportSafety[removed]=true;
        }
        return _supportSafety[removed]=false;
    }
}
