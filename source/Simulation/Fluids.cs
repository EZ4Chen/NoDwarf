namespace NoDwarf.Simulation;

public sealed record FluidMaterial(string Id,double Density,double SpecificHeat,double FreezeC,double BoilC,
    double FusionHeat,double VaporizationHeat,string SolidId,string GasId);
public static class FluidMaterials
{
    // SI material constants; one volume unit is one litre. Thermal energy uses Kelvin, joules.
    public static readonly FluidMaterial Water=new("nodwarf:water",1000,4184,0,100,334000,2256000,"nodwarf:ice","nodwarf:steam");
}
public sealed record FluidCell(Cell Cell,int Amount,double Heat,string Material="nodwarf:water")
{
    public double Temperature=>Heat/(Amount*FluidMaterials.Water.SpecificHeat)-273.15;
}
public sealed class Aquifer
{
    public Cell Cell {get;set;}
    public int Capacity {get;set;}
    public int Stored {get;set;}
    public int Head {get;set;}
    public int Rate {get;set;}=250;
    public int Recharge {get;set;}
    public double Temperature {get;set;}=12;
    public bool Discovered {get;set;}
    public double CurrentHead=>Cell.Level+(Head-Cell.Level)*(Stored/(double)Capacity);
}
public sealed class FluidArchive
{
    public int Version {get;set;}=2;
    public FluidCell[] Cells {get;set;}=Array.Empty<FluidCell>();
    public Aquifer[] Sources {get;set;}=Array.Empty<Aquifer>();
    public Cell[] OpenGates {get;set;}=Array.Empty<Cell>();
    public Cell[] Active {get;set;}=Array.Empty<Cell>();
    public double Elapsed {get;set;}
    public long Added {get;set;}
    public long Removed {get;set;}
    public long Recharged {get;set;}
    public long Initial {get;set;}
    public long Step {get;set;}
    public long RiverIn {get;set;}
    public long RiverOut {get;set;}
}

// Integer finite-volume demo. No Godot dependency; transfer is serialized and conservative.
public sealed class FluidSimulation
{
    public const int Full=1000,MaxCells=32768,WorkPerStep=2048;
    public static readonly Cell[] Adjacent={new(0,0,-1),new(1,0),new(-1,0),new(0,1),new(0,-1),new(0,0,1)};
    private readonly Func<Cell,int> _capacity;
    private readonly Func<Cell,bool> _passable;
    private readonly Action<Cell> _changed;
    private readonly Action<string> _log;
    private readonly Dictionary<Cell,FluidCell> _cells=new();
    private readonly Queue<Cell> _active=new();
    private readonly HashSet<Cell> _queued=new();
    private readonly Dictionary<Cell,double> _heads=new();
    private readonly Dictionary<Cell,double> _sourceHeads=new();
    public IReadOnlyDictionary<Cell,FluidCell> Cells=>_cells;
    public List<Aquifer> Sources {get;}=new();
    public long Revision {get;private set;}
    public long Added {get;private set;}
    public long Removed {get;private set;}
    public long Recharged {get;private set;}
    public long Initial {get;private set;}
    public long Step {get;private set;}
    public long RiverIn {get;private set;}
    public long RiverOut {get;private set;}
    private RiverBoundary[] _riverBoundaries=Array.Empty<RiverBoundary>();
    private double _riverTemperature=15;
    private HashSet<Cell> _riverInlets=new();
    public void SetRiverBoundaries(IEnumerable<RiverBoundary> boundaries,double temperature)
    {_riverBoundaries=boundaries.ToArray();_riverInlets=_riverBoundaries.Where(b=>b.Inlet).Select(b=>b.Cell).ToHashSet();_riverTemperature=temperature;}
    private void ExchangeRiverWater()
    {
        // Fixed head at each inlet. A blocked or full cell accepts no extra water.
        // Outflow is external removal, not an aquifer with infinite storage.
        foreach(var b in _riverBoundaries)
        {
            if(!_passable(b.Cell))continue;
            if(b.Inlet)RiverIn+=Add(b.Cell,50,_riverTemperature);
            else RiverOut+=Withdraw(b.Cell,50);
        }
    }
    public double Elapsed {get;private set;}
    public int Pending=>_active.Count;
    public bool AtLimit=>_cells.Count>=MaxCells;
    public long Mass=>_cells.Values.Sum(x=>(long)x.Amount)+Sources.Sum(s=>(long)s.Stored);
    public bool Conserved=>Mass==Initial+Added+Recharged-Removed;
    public FluidSimulation(Func<Cell,int> capacity,Func<Cell,bool> passable,Action<Cell> changed,Action<string> log)
    {_capacity=capacity;_passable=passable;_changed=changed;_log=log;}
    public int Amount(Cell c)=>_cells.TryGetValue(c,out var f)?f.Amount:0;
    public double Depth(Cell c)=>Amount(c)/1000d;
    private int SurfaceUnits(Cell c)=>c.Level*1000+1000-_capacity(c)+Amount(c);
    private double Surface(Cell c)=>SurfaceUnits(c)/1000d;
    public void Wake(Cell c)
    {
        if(TerrainMap.InBounds(c)&&_queued.Add(c))_active.Enqueue(c);
    }
    public void GeometryChanged(Cell c){if(Amount(c)>0)Wake(c);foreach(var d in Adjacent)if(Amount(c+d)>0)Wake(c+d);}
    private void Set(Cell c,int amount,double heat)
    {
        int before=Amount(c);
        if(amount==0)_cells.Remove(c);else _cells[c]=new(c,amount,heat);
        if(before!=amount){Revision++;_changed(c);GeometryChanged(c);}
    }
    private int Space(Cell c)=>!TerrainMap.InBounds(c)||!_passable(c)||(!_cells.ContainsKey(c)&&AtLimit)?0:Math.Max(0,_capacity(c)-Amount(c));
    public int Add(Cell c,int amount,double temperature=15,bool external=true)
    {
        if(amount<0||!double.IsFinite(temperature)||temperature< -273.15||temperature>10000)throw new ArgumentException("无效水量／温度。");
        int take=Math.Min(amount,Space(c));if(take==0)return 0;
        Set(c,Amount(c)+take,(_cells.GetValueOrDefault(c)?.Heat??0)+take*(temperature+273.15)*FluidMaterials.Water.SpecificHeat);
        if(external)Added+=take;return take;
    }
    public int Withdraw(Cell c,int requested)
    {if(requested<=0||!_cells.TryGetValue(c,out var f))return 0;int n=Math.Min(requested,f.Amount);Set(c,f.Amount-n,f.Heat*(f.Amount-n)/f.Amount);Removed+=n;return n;}
    public int Drain(Cell c)
    {int amount=Amount(c);if(amount>0){Set(c,0,0);Removed+=amount;}return amount;}
    private int Transfer(Cell from,Cell to,int desired)
    {
        if(!_passable(from)||desired<=0||!_cells.TryGetValue(from,out var f))return 0;
        int n=Math.Min(f.Amount,Math.Min(desired,Space(to)));if(n<=0)return 0;
        double energy=f.Heat*(n/(double)f.Amount);
        Set(to,Amount(to)+n,(_cells.GetValueOrDefault(to)?.Heat??0)+energy);
        Set(from,f.Amount-n,f.Heat-energy);return n;
    }
    public void AddSource(Aquifer source,bool initial=false)
    {
        if(source is null||Sources.Count>=128||!TerrainMap.InBounds(source.Cell)||source.Capacity is <1 or >10000000||source.Stored<0||source.Stored>source.Capacity||source.Head<=source.Cell.Level||source.Head>TerrainMap.MaxLevel+1||source.Rate is <5 or >5000||source.Rate%5!=0||source.Recharge<0||source.Recharge>source.Rate||source.Recharge%5!=0||!double.IsFinite(source.Temperature)||source.Temperature< -273.15||source.Temperature>10000||Sources.Any(s=>s.Cell==source.Cell))throw new ArgumentException("含水层参数无效（流量为 5 的倍数）。");
        Sources.Add(source);if(initial)Initial+=source.Stored;else Added+=source.Stored;
    }
    private void FeedSources()
    {
        _sourceHeads.Clear();
        foreach(var source in Sources)
        {
            int recharge=Math.Min(source.Capacity-source.Stored,source.Recharge/5);source.Stored+=recharge;Recharged+=recharge;
            if(source.Stored==0)continue;
            int budget=Math.Min(source.Stored,source.Rate/5);
            double head=source.CurrentHead;
            foreach(var d in new[]{new Cell(0,0,0)}.Concat(Adjacent))
            {
                var outlet=source.Cell+d;if(!TerrainMap.InBounds(outlet)||!_passable(outlet)||_capacity(outlet)==0||head<=Surface(outlet))continue;
                if(!source.Discovered){source.Discovered=true;Revision++;_log($"地下突水！{outlet.X},{outlet.Z} · Y={outlet.Level}，水头 {head:F1} m。");}
                _sourceHeads[outlet]=Math.Max(_sourceHeads.GetValueOrDefault(outlet),head);
                int amount=Math.Min(budget,Math.Max(0,(int)((head-Surface(outlet))*1000)));
                int emitted=Add(outlet,amount,source.Temperature,false);source.Stored-=emitted;budget-=emitted;
                // Also wake a full outlet: pressure can move existing water even when no new water fits.
                Wake(outlet);
            }
        }
    }
    private void PressureFlow()
    {
        _heads.Clear();var seen=new HashSet<Cell>();int transfers=0;
        bool FullAt(Cell c)=>_passable(c)&&_capacity(c)>0&&Amount(c)>=_capacity(c);
        // Full cells form hydraulic connections. Move volume between their boundaries,
        // leaving the full pipe intact instead of creating artificial air gaps along it.
        foreach(var c in _cells.Keys.ToArray())
        {
            if(!FullAt(c)||!seen.Add(c))continue;
            var component=new List<Cell>{c};var boundary=new HashSet<Cell>();double head=Surface(c);
            for(int i=0;i<component.Count;i++)
            {
                var p=component[i];head=Math.Max(head,Math.Max(Surface(p),_sourceHeads.GetValueOrDefault(p)));
                foreach(var d in Adjacent)
                {
                    var n=p+d;if(!TerrainMap.InBounds(n)||!_passable(n)||_capacity(n)==0)continue;
                    if(FullAt(n)){if(seen.Add(n))component.Add(n);}
                    else {boundary.Add(n);if(Amount(n)>0)head=Math.Max(head,Surface(n));}
                }
            }
            foreach(var p in component)_heads[p]=head;
            if(transfers>=256)continue;
            var targets=boundary.Where(n=>Space(n)>0&&head-Surface(n)>.001)
                .OrderBy(SurfaceUnits).ThenBy(n=>n.X).ThenBy(n=>n.Z).ThenBy(n=>n.Level).ToArray();
            if(targets.Length==0)continue;
            var target=targets[0];
            var donor=component.Concat(boundary.Where(n=>Amount(n)>0)).Where(n=>n!=target)
                // Prefer the connected inlet as donor: pressure transmits through a
                // full channel without waiting for a vacancy to diffuse upstream.
                .OrderByDescending(n=>_riverInlets.Contains(n)).ThenByDescending(SurfaceUnits).ThenBy(n=>n.X).ThenBy(n=>n.Z).ThenBy(n=>n.Level).FirstOrDefault();
            if(Amount(donor)==0)continue;
            // Limit to available head and to half the physical level difference when not externally pressurized.
            double natural=Surface(donor)-Surface(target);
            int desired=Math.Min(100,Math.Max(0,(int)((head-Surface(target))*1000)));
            if(head<=Surface(donor)+.0001)desired=Math.Min(desired,Math.Max(0,(int)Math.Round(natural*500)));
            if(Transfer(donor,target,desired)>0)transfers++;
        }
    }
    public void Advance(double dt)
    {
        if(!double.IsFinite(dt)||dt<=0)return;
        Elapsed+=dt;
        // Preserve backlog instead of silently losing simulation time; at most 8 substeps per call.
        int steps=0;while(Elapsed+1e-9>=.2&&steps++<8){Elapsed=Math.Max(0,Elapsed-.2);Tick();}
    }
    private void Tick()
    {
        Step++;ExchangeRiverWater();FeedSources();if(_active.Count==0)return;
        int count=Math.Min(WorkPerStep,_active.Count);
        for(int i=0;i<count;i++)
        {
            var c=_active.Dequeue();_queued.Remove(c);
            if(Amount(c)==0||!_passable(c))continue;
            Transfer(c,c+Adjacent[0],Full);
            for(int k=0;k<4;k++)
            {
                var n=c+Adjacent[1+(k+(int)(Step%4))%4];if(!TerrainMap.InBounds(n)||Space(n)==0)continue;
                int balance=(SurfaceUnits(c)-SurfaceUnits(n))/2;
                Transfer(c,n,Math.Max(0,balance));
            }

        }
        PressureFlow();
    }
    public FluidArchive Save(IEnumerable<Cell> gates)=>new(){Cells=_cells.Values.ToArray(),Sources=Sources.ToArray(),OpenGates=gates.ToArray(),Active=_active.ToArray(),Elapsed=Elapsed,Added=Added,Removed=Removed,Initial=Initial,Recharged=Recharged,Step=Step,RiverIn=RiverIn,RiverOut=RiverOut};
    public void Restore(FluidArchive s)
    {
        if(s.Version is not (1 or 2)||s.RiverIn<0||s.RiverOut<0||s.RiverIn>s.Added||s.RiverOut>s.Removed||s.Cells is null||s.Sources is null||s.Active is null||s.Cells.Length>MaxCells||s.Sources.Length>128||s.Active.Length>MaxCells*7||!double.IsFinite(s.Elapsed)||s.Elapsed<0||s.Elapsed>86400||s.Added<0||s.Removed<0||s.Initial<0||s.Recharged<0||s.Step<0)throw new InvalidDataException("流体存档无效。");
        _cells.Clear();Sources.Clear();_active.Clear();_queued.Clear();
        foreach(var f in s.Cells)
        {
            if(f is null||!TerrainMap.InBounds(f.Cell)||f.Material!=FluidMaterials.Water.Id||f.Amount<=0||f.Amount>_capacity(f.Cell)||!double.IsFinite(f.Heat)||f.Heat<0||!double.IsFinite(f.Temperature)||f.Temperature>10000||!_cells.TryAdd(f.Cell,f))throw new InvalidDataException("流体格无效。");
        }
        try{foreach(var source in s.Sources)AddSource(source);}catch(ArgumentException e){throw new InvalidDataException("含水层存档无效。",e);}
        foreach(var c in s.Active){if(!TerrainMap.InBounds(c)||_queued.Contains(c))throw new InvalidDataException("流体队列无效。");Wake(c);}
        Added=s.Added;Removed=s.Removed;Initial=s.Initial;Recharged=s.Recharged;Step=s.Step;Elapsed=s.Elapsed;RiverIn=s.RiverIn;RiverOut=s.RiverOut;
        if(!Conserved)throw new InvalidDataException("流体质量账本不平衡。");Revision++;
    }
}
