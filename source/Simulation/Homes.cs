namespace NoDwarf.Simulation;
public sealed record FireState(Cell Cell,double Fuel);
public sealed partial class DemoWorld
{
    public Dictionary<Cell,double> FireFuel {get;}=new();
    public bool FurnishedRoom(Cell c)
    {
        var room=FindRoomRegion(c);return !room.Outside&&room.Cells.Count>=4&&
            room.Cells.Any(p=>Directions.Any(d=>Buildings.TryGetValue(p+d,out var b)&&b.Kind==BuildKind.Door));
    }
    public bool KitchenReady(Cell c)=>Buildings.TryGetValue(c,out var b)&&b.Kind==BuildKind.Stove&&FurnishedRoom(c);
    public string RoomDescription(Cell c)=>Buildings.TryGetValue(c,out var b)?b.Kind switch
    {
        BuildKind.Stove=>KitchenReady(c)?"厨房就绪":"厨房需要围墙、门及至少 4 格内部空间",
        BuildKind.Bed=>RoomAt(c).Enclosed?"室内床 · 加快休息恢复":"露天床 · 仍可休息",
        BuildKind.Campfire=>$"燃料剩余 {FireFuel.GetValueOrDefault(c):0} 秒 · 自动搬入木料",_=>""
    }:"";
    private void ScheduleHomeJobs()
    {
        foreach(var b in Buildings.Values.Where(b=>b.Kind==BuildKind.Campfire))
        {
            if(FireFuel.GetValueOrDefault(b.Cell)>30||Jobs.Any(j=>j.Refuel&&j.Cell==b.Cell))continue;
            var j=new DemoJob{Id=_nextJob+1,Cell=b.Cell,Refuel=true};
            if(!Reserve(j,new(){{"wood",1}}))continue;
            _nextJob++;Jobs.Add(j);Revision++;
        }
    }
    private void UpdateHomes(double dt)
    {
        foreach(var c in FireFuel.Keys.ToArray())
        {
            double remaining=FireFuel[c]-dt;
            if(remaining<=0||!Buildings.TryGetValue(c,out var b)||b.Kind!=BuildKind.Campfire){FireFuel.Remove(c);Revision++;}
            else FireFuel[c]=remaining;
        }
    }
    public double HeatingAt(Cell c)
    {
        var room=EnvironmentRegion(c);
        return room is not null&&Envelope(room).Enclosed?RoomHeating(room):0;
    }
    private static string BedKey(Cell c)=>$"bed:{c.X}:{c.Z}:{c.Level}";
    private double RestRate(DemoWorker w)
    {
        var bed=Buildings.Values.FirstOrDefault(b=>b.Kind==BuildKind.Bed&&BedKey(b.Cell)==w.Person.Activity?.Target&&
            Math.Abs(b.Cell.X-w.Tile.X)+Math.Abs(b.Cell.Z-w.Tile.Z)<=1&&b.Cell.Level==w.Level);
        return bed is null?.9:RoomAt(bed.Cell).Enclosed?1.8:1.35;
    }
}
