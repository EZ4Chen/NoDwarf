namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    public FluidSimulation Fluids {get;private set;}=null!;
    private readonly HashSet<Cell> _openFloodgates=new();
    private readonly HashSet<Cell> _deepWater=new();
    public bool GateOpen(Cell c)=>_openFloodgates.Contains(c);
    public bool FluidPassable(Cell c)=>Inside(c)&&!Solid(c)&&!(Buildings.TryGetValue(c,out var b)&&b.Kind==BuildKind.Floodgate&&!GateOpen(c));
    public int FluidCapacity(Cell c)=>!Inside(c)||Solid(c)?0:NaturalRamp(c)||(Buildings.TryGetValue(c,out var b)&&BlockCatalog.IsStairs(b.Kind))?500:1000;
    private void InitializeFluids(bool seed)
    {
        Fluids=new FluidSimulation(FluidCapacity,FluidPassable,c=>
        {
            bool deep=Fluids.Amount(c)>=500;
            if(deep?_deepWater.Add(c):_deepWater.Remove(c))Navigation.Invalidate(c);
        },Log);
        Fluids.SetRiverBoundaries(Terrain.RiverBoundaries,Math.Clamp(Terrain.Region?.Mean??15,1,30));
        if(!seed||Terrain.Flat||Terrain.Region is null)return;
        foreach(var c in Terrain.InitialSurfaceWater)Fluids.Add(c,1000,Math.Clamp(Terrain.Region.Mean,1,30));
        var random=new Random(unchecked(Terrain.Seed^0x51f01d));
        int aquifers=Terrain.Generation is {Revision:2} g?g.Aquifers*16:16;
        for(int i=0;i<aquifers;i++)
        {
            int x=16+random.Next(Width-32),z=16+random.Next(Depth-32);
            if(Math.Abs(x-Camp.X)<18&&Math.Abs(z-Camp.Z)<18)continue;
            int y=Math.Max(2,Terrain[x,z].Level-12-random.Next(24));
            if(!Terrain.Solid(new(x,z,y)))continue;
            int capacity=(12000+random.Next(24000))*(Terrain.Generation is {Revision:2,Aquifers:2}?2:1);
            Fluids.AddSource(new Aquifer{Cell=new(x,z,y),Capacity=capacity,Stored=capacity,Head=Math.Min(TerrainMap.MaxLevel,Terrain[x,z].Level-3),Rate=500,Recharge=Terrain.Generation is {Revision:2} modern?(modern.Site.Rainfall>.65?10:0):Terrain.Region.Humidity>.65?10:0,Temperature=12},true);
        }
    }
    private void FluidGeometryChanged(Cell c)
    {
        if(!Buildings.TryGetValue(c,out var b)||b.Kind!=BuildKind.Floodgate)_openFloodgates.Remove(c);
        Fluids?.GeometryChanged(c);
    }
    public bool ToggleFloodgate(Cell c)
    {
        if(!Buildings.TryGetValue(c,out var b)||b.Kind!=BuildKind.Floodgate)return false;
        if(!_openFloodgates.Remove(c))_openFloodgates.Add(c);
        Navigation.Invalidate(c);InvalidateRooms(c);Fluids.GeometryChanged(c);Revision++;
        Log(GateOpen(c)?"闸门已打开。":"闸门已关闭，格中水量保留。");return true;
    }
    public void RestoreFluids(FluidArchive? archive)
    {
        InitializeFluids(false);_openFloodgates.Clear();_deepWater.Clear();
        if(archive is null)return;
        if(archive.OpenGates is null||archive.OpenGates.Length>Buildings.Count)throw new InvalidDataException("闸门存档无效。");
        foreach(var c in archive.OpenGates)if(!Buildings.TryGetValue(c,out var b)||b.Kind!=BuildKind.Floodgate||!_openFloodgates.Add(c))throw new InvalidDataException("闸门存档无效。");
        Fluids.Restore(archive);foreach(var f in Fluids.Cells.Values)if(f.Amount>=500)_deepWater.Add(f.Cell);
    }
    public string FluidDescription(Cell c)
    {
        string text="";
        if(Fluids.Cells.TryGetValue(c,out var f))text=$"水 {f.Amount} L · 本格水深 {Fluids.Depth(c):F2} m · {f.Temperature:F1}°C";
        var source=Fluids.Sources.FirstOrDefault(s=>s.Discovered&&(s.Cell==c||FluidSimulation.Adjacent.Any(d=>s.Cell+d==c)));
        if(source is not null)text+=(text==""?"":"\n")+$"含水层 {source.Stored}/{source.Capacity} L · 水头 {source.CurrentHead:F1} m";
        if(Buildings.TryGetValue(c,out var b)&&b.Kind==BuildKind.Floodgate)text+=(text==""?"":"\n")+(GateOpen(c)?"闸门：开启":"闸门：关闭");
        return text;
    }
}
