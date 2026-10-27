namespace NoDwarf.Simulation;

public sealed record RoomInfo(bool Enclosed,int Area,int Roofed,double Temperature,double Quality,string Grade,string Use,int FinishedFloor,int Furniture);
public sealed record RoomClimateState(Cell Anchor,double Temperature,double UpdatedAt);

public sealed partial class DemoWorld
{
    private sealed record RoomEnvelope(bool Enclosed,int Roofed,double Quality,string Grade,string Use,int FinishedFloor,int Furniture);
    private sealed class RoomHeat
    {
        public required HashSet<Cell> Cells;
        public required RoomRegion Region;
        public Cell Anchor;
        public double Temperature,UpdatedAt;
        public double ExchangeSeconds,ExchangeWeight,ExchangeDelta;
    }
    private readonly List<RoomHeat> _roomHeat=new();
    public const double RoomTemperatureInterval=5;
    private double _lastRoomTemperatureUpdate;
    private static Cell RoomAnchor(IEnumerable<Cell> cells)=>cells.OrderBy(c=>c.X).ThenBy(c=>c.Z).First();
    public double OutdoorTemperatureAt(Cell c)=>ClimateRules.OutdoorTemperature(Calendar,Terrain[c.X,c.Z].Level,Terrain.Region)+Weather.Temperature;
    private bool RoomBarrier(Cell c)=>Boundary(c)||Buildings.TryGetValue(c+new Cell(0,0,-1),out var below)&&below.Kind==BuildKind.Door;

    private RoomEnvelope Envelope(RoomRegion room)
    {
        if(room.Envelope is {} cached)return cached;
        if(room.Outside)return room.Envelope=new(false,0,0,"无","室外",0,0);
        int roofed=0,finished=0,furniture=0,beds=0,stoves=0,workshops=0;
        bool sealedWalls=true;
        foreach(var c in room.Cells)
        {
            int roof=c.Level+1;
            while(roof<=TerrainMap.MaxLevel&&!Solid(new Cell(c.X,c.Z,roof)))roof++;
            if(roof<=TerrainMap.MaxLevel)
            {
                roofed++;
                // A single low wall beneath a higher roof leaves an outdoor opening.
                foreach(var d in Directions)
                    if(!room.Cells.Contains(c+d))
                        for(int y=c.Level;y<roof;y++)
                            if(!RoomBarrier(new Cell(c.X+d.X,c.Z+d.Z,y))){sealedWalls=false;break;}
            }
            if(Buildings.TryGetValue(c+new Cell(0,0,-1),out var floor)&&floor.Kind is BuildKind.Planks or BuildKind.StoneBricks)finished++;
            if(Buildings.TryGetValue(c,out var b))
            {
                if(b.Kind==BuildKind.Bed)beds++;
                if(b.Kind==BuildKind.Stove)stoves++;
                if(b.Kind is BuildKind.Anvil or BuildKind.Furnace or BuildKind.Mill or BuildKind.Loom)workshops++;
                if(b.Kind is BuildKind.Bed or BuildKind.Stove or BuildKind.Anvil or BuildKind.Furnace or BuildKind.Mill or BuildKind.Loom)furniture++;
            }
        }
        bool enclosed=sealedWalls&&roofed==room.Cells.Count;
        double quality=enclosed?Math.Min(40,room.Cells.Count*2)+40d*finished/room.Cells.Count+Math.Min(20,furniture*5):0;
        string use=(beds>0?1:0)+(stoves>0?1:0)+(workshops>0?1:0)>1?"多用途房间":beds>0?"卧室":stoves>0?"厨房":workshops>0?"工坊":"空房间";
        return room.Envelope=new(enclosed,roofed,quality,!enclosed?"无":quality<25?"简陋":quality<50?"普通":quality<75?"舒适":"精致",use,finished,furniture);
    }
    private double RoomBaseTemperature(Cell c)
    {
        double outside=OutdoorTemperatureAt(c),mean=Terrain.Region?.Mean??15;
        int depth=Math.Max(0,Terrain[c.X,c.Z].Level-c.Level);
        return mean+(outside-mean)*Math.Exp(-depth/6d);
    }
    private RoomHeat Heat(RoomRegion region)
    {
        if(region.Heat is {} cached)return cached;
        double sum=0;int inherited=0;
        // Preserve existing warmth when rooms split or merge, weighted by shared floor area.
        foreach(var old in _roomHeat.ToArray())
        {
            int overlap=old.Cells.Count(region.Cells.Contains);
            if(overlap==0)continue;
            sum+=old.Temperature*overlap;inherited+=overlap;
            old.Cells.ExceptWith(region.Cells);
            if(old.Cells.Count==0)_roomHeat.Remove(old);
        }
        var anchor=RoomAnchor(region.Cells);
        var heat=new RoomHeat{Region=region,Cells=new(region.Cells),Anchor=anchor,UpdatedAt=_simulationTime,
            Temperature=inherited>0?sum/inherited:RoomBaseTemperature(anchor)};
        _roomHeat.Add(heat);region.Heat=heat;_roomHeatLinksDirty=true;return heat;
    }
    private RoomRegion? EnvironmentRegion(Cell cell)
    {
        // Match the headroom rule used while traversing rooms: a low water channel is not a room entry.
        if(!Inside(cell)||Boundary(cell)||NaturalSolid(cell+new Cell(0,0,1))||!Solid(cell+new Cell(0,0,-1)))return null;
        return FindRoomRegion(cell);
    }
    public RoomInfo RoomAt(Cell cell)
    {
        if(!Inside(cell))throw new ArgumentOutOfRangeException(nameof(cell));
        var region=EnvironmentRegion(cell);
        if(region is null)return new(false,0,0,OutdoorTemperatureAt(cell),0,"无","室外",0,0);
        var envelope=Envelope(region);
        if(!envelope.Enclosed)return new(false,region.Outside?0:region.Cells.Count,envelope.Roofed,OutdoorTemperatureAt(cell),0,"无",region.Outside?"室外":"开放空间",envelope.FinishedFloor,envelope.Furniture);
        return new(true,region.Cells.Count,envelope.Roofed,Heat(region).Temperature,envelope.Quality,envelope.Grade,envelope.Use,envelope.FinishedFloor,envelope.Furniture);
    }
    public double TemperatureAt(Cell cell)=>RoomAt(cell).Temperature;
    private double RoomHeating(RoomRegion region)=>Math.Min(20,FireFuel.Count(f=>f.Value>0&&region.Cells.Contains(f.Key)&&Buildings.TryGetValue(f.Key,out var b)&&b.Kind==BuildKind.Campfire)*10);
    private void RefreshRoomHeatLayouts()
    {
        // Only tracked enclosed rooms are revisited after a structural edit.
        while(_roomHeat.FirstOrDefault(h=>!_roomRegions.Contains(h.Region)) is {} old)
        {
            var cells=old.Cells.Where(c=>!Boundary(c)&&Solid(c+new Cell(0,0,-1))).ToArray();
            if(cells.Length==0){_roomHeat.Remove(old);continue;}
            var region=FindRoomRegion(RoomAnchor(cells));
            if(!Envelope(region).Enclosed){_roomHeat.Remove(old);continue;}
            Heat(region);
        }
    }
    private void UpdateRoomTemperatures()
    {
        if(_simulationTime-_lastRoomTemperatureUpdate<RoomTemperatureInterval)return;
        using var timing=Probe?.Measure("rooms.temperature-update")??default;
        _lastRoomTemperatureUpdate=_simulationTime;RefreshRoomHeatLayouts();
        foreach(var fire in FireFuel.Keys)
        {
            var region=EnvironmentRegion(fire);
            if(region is not null&&Envelope(region).Enclosed)Heat(region);
        }
        EnsureRoomHeatLinks();
        foreach(var heat in _roomHeat)
        {
            double dt=Math.Max(0,_simulationTime-heat.UpdatedAt);heat.UpdatedAt=_simulationTime;
            heat.ExchangeSeconds=dt;heat.ExchangeWeight=0;heat.ExchangeDelta=0;
            double target=RoomBaseTemperature(heat.Anchor)+RoomHeating(heat.Region);
            double inertia=Math.Min(300,30+heat.Cells.Count*2);
            heat.Temperature+=(target-heat.Temperature)*(1-Math.Exp(-dt/inertia));
        }
        ExchangeRoomHeat();
    }
    private RoomClimateState[] SaveRoomClimates()
    {
        RefreshRoomHeatLayouts();
        return _roomHeat.Select(h=>new RoomClimateState(h.Anchor,h.Temperature,h.UpdatedAt)).ToArray();
    }
    private void RestoreRoomClimates(RoomClimateState[] states,double updatedAt)
    {
        if(states is null||states.Length>10000||!double.IsFinite(updatedAt)||updatedAt<0||updatedAt>_simulationTime)throw new InvalidDataException("房间温度存档无效。");
        var seen=new HashSet<RoomRegion>();
        foreach(var state in states)
        {
            if(!Inside(state.Anchor)||!double.IsFinite(state.Temperature)||state.Temperature is <-200 or >200||!double.IsFinite(state.UpdatedAt)||state.UpdatedAt<0||state.UpdatedAt>_simulationTime)throw new InvalidDataException("房间温度越界。");
            var region=EnvironmentRegion(state.Anchor);
            if(region is null||!Envelope(region).Enclosed||!seen.Add(region))throw new InvalidDataException("房间温度与建筑围合不一致。");
            Heat(region).Temperature=state.Temperature;
            Heat(region).UpdatedAt=state.UpdatedAt;
        }
        _lastRoomTemperatureUpdate=updatedAt;
    }
    public string EnvironmentDescription(Cell cell)
    {
        if(Solid(cell)&&Inside(cell+new Cell(0,0,1)))cell+=new Cell(0,0,1);
        var room=RoomAt(cell);
        return room.Enclosed?$"{room.Use} · {room.Area} 格 · {room.Grade} {room.Quality:0}/100\n室温 {room.Temperature:F1}°C · 已铺地 {room.FinishedFloor}/{room.Area} · 家具 {room.Furniture}"
            :$"{room.Use} · 室外 {room.Temperature:F1}°C"+(room.Area>0?$" · 屋顶 {room.Roofed}/{room.Area}":"");
    }
}
