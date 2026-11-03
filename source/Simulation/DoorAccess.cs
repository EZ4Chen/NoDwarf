namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    public bool DoorLocked(Cell cell)=>Buildings.TryGetValue(cell,out var b)&&b.Kind==BuildKind.Door&&b.Locked;
    private bool LockedDoorBody(Cell cell)=>DoorLocked(cell)||DoorLocked(cell+new Cell(0,0,-1));

    public ResidentResult SetDoorLocked(Cell cell,bool locked)
    {
        if(!Buildings.TryGetValue(cell,out var door)||door.Kind!=BuildKind.Door)return new(false,"请选择普通门");
        if(door.Locked==locked)return new(true,locked?"门已锁闭":"门已允许通行");
        bool Touches(Cell p)=>p.X==cell.X&&p.Z==cell.Z&&p.Level>=cell.Level&&p.Level<cell.Level+2;
        if(locked&&(Actors.Any(a=>Touches(a.Tile)||a.TravelFrom is not null&&a.Path.TryPeek(out var next)&&Touches(next))||
            _creatures is not null&&_creatures.Save().Creatures.Any(c=>Touches(c.Cell)||c.Destination is Cell target&&Touches(target))))
            return new(false,"门口有人或生物正在通过，无法锁闭");

        Buildings.SetDoorLocked(cell,locked);Navigation.Invalidate(cell);Revision++;
        int budget=Workers.Count+Enemies.Count;
        foreach(var actor in Actors)
        {
            if(actor.Path.Count>0&&!EnsureWorkerRoute(actor,ref budget)&&actor.Path.Count==0)
            {CancelResidentActivity(actor,"门禁变化，路线不可达");actor.Status="门禁变化，路线不可达";}
            ValidateHomeBed(actor);
            actor.NextJobSearch=0;actor.NextCareCheck=0;actor.NextRescueCheck=0;
        }
        string message=locked?"门已锁闭，普通门不挡水":"门已允许通行";
        if(locked)
        {
            var trapped=Workers.Where(w=>!w.Person.Dead&&RoomHasNoExit(w)).Select(w=>w.Name).ToArray();
            if(trapped.Length>0)message+="；"+string.Join("、",trapped)+"所在房间无可达出口，请解锁门或打开其他通路";
        }
        Log(message);return new(true,message);
    }

    private bool RoomHasNoExit(DemoWorker worker)
    {
        var room=FindRoomRegion(worker.Tile);
        return !room.Outside&&!room.Cells.SelectMany(Neighbors).Any(c=>!room.Cells.Contains(c)&&Navigation.CanReach(worker.Tile,c));
    }
}
