namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    private sealed record RoomHeatLink(RoomHeat A,RoomHeat B,double Conductance);
    private readonly List<RoomHeatLink> _roomHeatLinks=new();
    private bool _roomHeatLinksDirty=true;
    private const double WallHeatConductance=.1,DoorHeatConductance=.4;

    private void EnsureRoomHeatLinks()
    {
        if(!_roomHeatLinksDirty)return;
        using var timing=Probe?.Measure("rooms.heat-network-build")??default;
        _roomHeatLinks.Clear();
        var links=new Dictionary<(RoomHeat A,RoomHeat B),double>();
        static Cell Anchor(RoomHeat heat)=>heat.Anchor;
        bool Before(RoomHeat a,RoomHeat b)
        {
            var x=Anchor(a);var y=Anchor(b);
            return x.Level!=y.Level?x.Level<y.Level:x.X!=y.X?x.X<y.X:x.Z<y.Z;
        }
        // Discover connected neighbours, even if the player has never selected them.
        // This runs only when structure or the set of tracked rooms changes.
        for(int index=0;index<_roomHeat.Count;index++)
        {
            var source=_roomHeat[index];
            foreach(var cell in source.Cells)
            foreach(var direction in Directions)
            {
                if(source.Cells.Contains(cell+direction))continue;
                // A shared one-cell wall or door gives one fixed connection per room pair.
                // No wall-thickness search, material resistance or shared-area calculation.
                var barrier=cell+direction;
                var next=barrier+direction;
                if(!Inside(next)||!RoomBarrier(barrier)||RoomBarrier(next))continue;
                var region=EnvironmentRegion(next);
                if(region is not null&&region!=source.Region&&Envelope(region).Enclosed)
                {
                    var target=Heat(region);
                    if(Before(source,target))
                    {
                        bool door=Buildings.TryGetValue(barrier,out var b)&&b.Kind is BuildKind.Door or BuildKind.Floodgate;
                        var pair=(source,target);
                        links[pair]=Math.Max(links.GetValueOrDefault(pair),door?DoorHeatConductance:WallHeatConductance);
                    }
                }
            }
        }
        foreach(var (pair,conductance) in links.OrderBy(p=>Anchor(p.Key.A).Level).ThenBy(p=>Anchor(p.Key.A).X).ThenBy(p=>Anchor(p.Key.A).Z)
            .ThenBy(p=>Anchor(p.Key.B).X).ThenBy(p=>Anchor(p.Key.B).Z))
            _roomHeatLinks.Add(new(pair.A,pair.B,conductance));
        _roomHeatLinksDirty=false;
        Probe?.Count("rooms.heat-network-builds");
        Probe?.Count("rooms.heat-network-links",_roomHeatLinks.Count);
    }

    private void ExchangeRoomHeat()
    {
        using var timing=Probe?.Measure("rooms.heat-exchange")??default;
        foreach(var link in _roomHeatLinks)
        {
            double weight=link.Conductance*Math.Min(link.A.ExchangeSeconds,link.B.ExchangeSeconds);
            link.A.ExchangeWeight+=weight/link.A.Cells.Count;
            link.B.ExchangeWeight+=weight/link.B.Cells.Count;
        }
        // Simultaneous, equal-and-opposite energy transfers. Each link uses both ends'
        // local exchange weights to cap mixing at 50%, without coupling unrelated rooms.
        foreach(var link in _roomHeatLinks)
        {
            double weight=Math.Max(link.A.ExchangeWeight,link.B.ExchangeWeight);
            double scale=weight<1e-8?1:(1-Math.Exp(-2*weight))/(2*weight);
            double energy=(link.B.Temperature-link.A.Temperature)*link.Conductance*
                Math.Min(link.A.ExchangeSeconds,link.B.ExchangeSeconds)*scale;
            link.A.ExchangeDelta+=energy/link.A.Cells.Count;
            link.B.ExchangeDelta-=energy/link.B.Cells.Count;
        }
        foreach(var heat in _roomHeat)heat.Temperature+=heat.ExchangeDelta;
    }
}
