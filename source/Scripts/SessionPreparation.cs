using NoDwarf.Simulation;
namespace NoDwarf;

public sealed record PreparedSession(SessionState State,DemoWorld World,SimulationClock Clock,FoundationPlan? Foundation,TerrainView.ForestData Forest,WorldOverview.MapData Map);

// Prepare presentation data after the engine-independent session has been validated.
public static class SessionPreparation
{
    public static PreparedSession Prepare(string path)
    {
        var session=SessionPersistence.Read(path);
        return new(session.State,session.World,session.Clock,session.Foundation,
            TerrainView.PrepareForest(session.World),WorldOverview.PrepareMap(session.World));
    }
}
