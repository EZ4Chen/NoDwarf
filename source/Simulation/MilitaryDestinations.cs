namespace NoDwarf.Simulation;
public sealed partial class DemoWorld
{
    // Reserve endpoints, not entire corridors. Friendly walkers may pass each other;
    // hard per-voxel occupancy would deadlock one-wide stairs and ladders.
    private bool MilitaryStandReserved(DemoWorker actor,Cell cell)
    {
        foreach(var other in Actors)
        {
            if(other==actor)continue;
            if(other.Combat.Destination==cell)return true;
            if(other.Path.Count>0&&other.Path.Last()==cell)return true;
            if(other.Path.Count==0&&other.Tile==cell)return true;
        }
        return false;
    }
    private Cell? AvailableMilitaryDestination(DemoWorker actor,Cell requested)
    {
        // Never silently place a soldier on another storey just because X/Z match.
        for(int radius=0;radius<=3;radius++)
            for(int dx=-radius;dx<=radius;dx++)for(int dz=-radius;dz<=radius;dz++)
            {
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                var c=requested+new Cell(dx,dz);
                if(Inside(c)&&Walkable(c)&&!MilitaryStandReserved(actor,c)&&Navigation.CanReach(actor.Tile,c))return c;
            }
        return null;
    }
}
