namespace NoDwarf.Simulation;

public sealed class StoryMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
}
public sealed class StoryReceipt
{
    public string CallId { get; set; } = "";
    public string Command { get; set; } = "";
    public string Result { get; set; } = "";
}
public sealed class StoryTurn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Model { get; set; } = "";
    public string Provider { get; set; } = "";
    public int PromptVersion { get; set; } = 1;
    public double ObservedAt { get; set; }
    public string VitalState { get; set; } = "";
    public string Status { get; set; } = "ready";
    public int Requests { get; set; }
    public string AcceptedJson { get; set; } = "";
    public string AcceptedCallId { get; set; } = "";
    public List<StoryMessage> Messages { get; set; } = new();
    public List<StoryReceipt> Receipts { get; set; } = new();
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}
public sealed class StoryEvent
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public double Due { get; set; }
    public int Count { get; set; }
    public string Item { get; set; } = "";
    public string Weather { get; set; } = "";
    public double Duration { get; set; }
    public string Reason { get; set; } = "";
}
public sealed record StoryHistory(string EventId, string Kind, double Time, string Outcome, string Detail);
public sealed class StoryState
{
    public bool Enabled { get; set; }
    public double GraceUntil { get; set; }
    public double NextDecisionAt { get; set; }
    public double RaidAfter { get; set; }
    public double SuppliesAfter { get; set; }
    public double WeatherAfter { get; set; }
    public int PreviousCapable { get; set; } = -1;
    public double RecoveryUntil { get; set; }
    public string ActiveWeather { get; set; } = "";
    public double WeatherUntil { get; set; }
    public string Error { get; set; } = "";
    public StoryTurn? Turn { get; set; }
    public List<StoryEvent> Pending { get; set; } = new();
    public List<StoryHistory> History { get; set; } = new();
}
