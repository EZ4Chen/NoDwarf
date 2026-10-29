using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoDwarf.Simulation;

public sealed partial class DemoWorld
{
    public const double StoryHour = GameCalendar.SecondsPerDay / 24;
    public const int MaxStoryRaid = 12;
    public StoryState Story { get; private set; } = new();
    private double _storyHealthCheck;
    public string StoryVitalState() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('|', Workers.Select(w => $"{w.Id}:{w.Person.Dead}:{w.Person.Incapacitated}")) + "/" +
        string.Join('|', Enemies.Select(w => $"{w.Id}:{w.Person.Dead}:{w.Person.Incapacitated}")))));
    public double StoryThreatBudget(ColonyValue? value = null)
    {
        var v = value ?? Values(true);
        if (SimulationTime < Story.GraceUntil || v.Capable == 0 || Enemies.Any(Fighting)) return 0;
        double wealth = 70 + Math.Sqrt(v.Total) * 4;
        double readiness = Math.Min(v.Capable * 100, v.CombatPower * 2);
        double recovery = SimulationTime < Story.RecoveryUntil ? .5 : 1;
        return Math.Floor(Math.Clamp(Math.Min(wealth, readiness) * recovery, 0, MaxStoryRaid * 100));
    }
    public double StoryGiftBudget(ColonyValue? value = null) => Math.Clamp((value ?? Values(true)).Total * .03, 60, 300);

    public void EnableStory(bool enabled)
    {
        if (Story.Enabled == enabled) return;
        Story.Enabled = enabled; Story.Error = ""; Story.Turn = null;
        if (enabled)
        {
            Story.GraceUntil = Math.Max(Story.GraceUntil, SimulationTime + GameCalendar.SecondsPerDay);
            Story.NextDecisionAt = SimulationTime;
            Story.PreviousCapable = Values(true).Capable;
            Log("LLM 叙事者已启用；会发送营地摘要并使用 AI 请求额度。首日不安排袭击。");
        }
        else
        {
            foreach (var e in Story.Pending) StoryRecord(e, "cancelled", "玩家关闭叙事者，待发生事件取消。");
            Story.Pending.Clear();
            Log("叙事者已停止；待发生事件取消，已发生后果保留。");
        }
        Revision++;
    }
    public void RetryStory()
    {
        if (!Story.Enabled) return;
        // Only unaccepted network work is replaced. A received command must be settled first.
        if (Story.Turn is { AcceptedJson.Length: > 0 }) return;
        Story.Turn = null; Story.Error = ""; Story.NextDecisionAt = SimulationTime; Revision++;
    }
    public ResidentResult CancelStoryEvent(string id, string reason)
    {
        var e = Story.Pending.FirstOrDefault(e => e.Id == id);
        if (e is null) return new(false, "事件已发生、已取消或不存在。");
        Story.Pending.Remove(e); StoryRecord(e, "cancelled", reason); Revision++;
        return new(true, "待发生事件已取消。");
    }
    public ResidentResult ScheduleStoryEvent(StoryEvent e)
    {
        ValidateStoryEvent(e);
        if (!Story.Enabled) return new(false, "叙事者已关闭。");
        if (Story.Pending.Any(x => x.Id == e.Id) || Story.History.Any(x => x.EventId == e.Id)) return new(false, "事件 ID 已使用。");
        if (Story.Pending.Count >= 8) return new(false, "待发生事件已满。");
        if (e.Due < SimulationTime + .1 * StoryHour || e.Due > SimulationTime + 24 * StoryHour) return new(false, "事件需要安排在 0.1—24 游戏小时后。");
        var value = Values(true);
        string issue = StoryEventIssue(e, value, false);
        if (issue != "") return new(false, issue);
        Story.Pending.Add(e);
        if (e.Kind == "raid") Story.RaidAfter = e.Due + 2 * GameCalendar.SecondsPerDay;
        if (e.Kind == "supplies") Story.SuppliesAfter = e.Due + 6 * StoryHour;
        if (e.Kind == "weather") Story.WeatherAfter = e.Due + e.Duration + 6 * StoryHour;
        Log($"叙事者安排了{StoryEventName(e.Kind)}：约 {(e.Due - SimulationTime) / StoryHour:0.#} 游戏小时后。{e.Reason}");
        Revision++; return new(true, "事件已排队，发生前会再次检查条件。");
    }
    public static string StoryEventName(string kind) => kind switch { "raid" => "袭击", "supplies" => "物资发现", "weather" => "天气变化", "decision" => "叙事决定", "loss" => "居民损失", _ => kind };
    private string StoryEventIssue(StoryEvent e, ColonyValue value, bool executing)
    {
        if (value.Living == 0) return "营地已无存活居民。";
        if (e.Kind == "raid")
        {
            if (Story.Pending.Any(x => x != e && x.Kind == "raid")) return "已有袭击占用威胁预算。";
            if (!executing && (e.Due < Story.RaidAfter || e.Due < Story.GraceUntil || e.Due < SimulationTime + 2 * StoryHour)) return "袭击仍在宽限或冷却期，且需要至少两小时预警。";
            // Reserve based on current ability, even if the scheduled date is after grace.
            if (e.Count * 100 > StoryThreatBudget(value)) return "袭击超过当前财富、战力或恢复期允许的预算。";
        }
        if (e.Kind == "supplies")
        {
            if (Story.Pending.Any(x => x != e && x.Kind == "supplies") || !executing && e.Due < Story.SuppliesAfter) return "补给仍在冷却期或已有补给排队。";
            if (e.Count * ValueCatalog.Base(e.Item) > StoryGiftBudget(value)) return "补给超过当前机会预算。";
            if (value.Capable == 0) return "没有可接收补给的居民。";
        }
        if (e.Kind == "weather")
        {
            if (WeatherOverride is not null) return "开发者固定天气期间不安排天气事件。";
            if (Story.Pending.Any(x => x != e && x.Kind == "weather") || Story.WeatherUntil > SimulationTime || !executing && e.Due < Story.WeatherAfter) return "天气事件仍在持续、冷却或已排队。";
            if (e.Weather == "rain" && OutdoorTemperature <= 0) return "当前气温不适合降雨。";
        }
        return "";
    }
    private void StoryRecord(StoryEvent e, string outcome, string detail)
    {
        Story.History.Add(new(e.Id, e.Kind, SimulationTime, outcome, detail));
        if (Story.History.Count > 64) Story.History.RemoveAt(0);
    }
    private void AdvanceStoryEvents()
    {
        if (Story.ActiveWeather != "" && SimulationTime >= Story.WeatherUntil)
        { Story.ActiveWeather = ""; Revision++; Log("叙事天气结束，恢复自然天气。"); }
        if (!Story.Enabled) return;
        if (SimulationTime >= _storyHealthCheck)
        {
            _storyHealthCheck = SimulationTime + 2;
            int capable = 0; foreach (var w in Workers) if (Fighting(w)) capable++;
            if (Story.PreviousCapable >= 0 && capable < Story.PreviousCapable)
            {
                Story.RecoveryUntil = SimulationTime + 2 * GameCalendar.SecondsPerDay;
                Story.NextDecisionAt = Math.Min(Story.NextDecisionAt, SimulationTime);
                StoryRecord(new() { Id = "loss-" + SimulationTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Kind = "loss" }, "observed", "可行动居民减少，进入恢复期。");
            }
            Story.PreviousCapable = capable;
        }
        // Bounded queue. Evaluate only when due; no navigation work on ordinary ticks.
        for (int i = Story.Pending.Count - 1; i >= 0; i--)
        {
            var e = Story.Pending[i]; if (e.Due > SimulationTime) continue;
            string issue = StoryEventIssue(e, Values(true), true);
            if (issue == "" && e.Kind == "raid")
            { var result = StartRaid(e.Count); if (!result.Ok) issue = result.Message; }
            else if (issue == "" && e.Kind == "supplies")
            {
                var receiver = Workers.FirstOrDefault(w => Fighting(w) && Walkable(w.Tile));
                if (receiver is null) issue = "没有安全的接收位置。";
                else SpawnItem(e.Item, e.Count, receiver.Tile);
            }
            else if (issue == "" && e.Kind == "weather")
            { Story.ActiveWeather = e.Weather; Story.WeatherUntil = SimulationTime + e.Duration; }
            Story.Pending.RemoveAt(i);
            StoryRecord(e, issue == "" ? "executed" : "cancelled", issue == "" ? e.Reason : issue);
            Log(issue == "" ? $"叙事事件发生：{StoryEventName(e.Kind)}。{e.Reason}" : $"叙事事件取消：{StoryEventName(e.Kind)}。{issue}");
            _valueCache = null; Revision++;
        }
    }
    private static bool StoryText(string? s, int limit) => s is not null && s.Length <= limit;
    private static bool StoryTime(double time) => double.IsFinite(time) && time >= 0 && time <= 1e12;
    private static void ValidateStoryEvent(StoryEvent e)
    {
        if (e is null || !StoryText(e.Id, 100) || e.Id.Length == 0 || !StoryText(e.Reason, 240) ||
            !StoryTime(e.Due) || !StoryTime(e.Duration) || e.Kind is not ("raid" or "supplies" or "weather") ||
            !StoryText(e.Item, 40) || !StoryText(e.Weather, 20)) throw new InvalidDataException("叙事事件无效。");
        if (e.Kind == "raid" && (e.Count is < 1 or > MaxStoryRaid || e.Item != "" || e.Weather != "" || e.Duration != 0) ||
            e.Kind == "supplies" && (e.Count is < 1 or > 100 || e.Item is not ("meal" or "wood" or "bandage" or "grain_seed") || e.Weather != "" || e.Duration != 0) ||
            e.Kind == "weather" && (e.Count != 0 || e.Item != "" || e.Weather is not ("clear" or "cloudy" or "rain" or "fog") || e.Duration < StoryHour || e.Duration > 12 * StoryHour))
            throw new InvalidDataException("叙事事件参数越界。");
    }
    private void RestoreStory(StoryState? state)
    {
        if (state is null) { Story = new(); return; }
        if (state.Pending is null || state.Pending.Count > 8 || state.History is null || state.History.Count > 64 ||
            !StoryText(state.Error, 512) || state.PreviousCapable is < -1 or > 1000 ||
            !new[] { state.GraceUntil, state.NextDecisionAt, state.RaidAfter, state.SuppliesAfter, state.WeatherAfter, state.RecoveryUntil, state.WeatherUntil }.All(StoryTime) ||
            state.ActiveWeather is not ("" or "clear" or "cloudy" or "rain" or "fog")) throw new InvalidDataException("叙事存档无效。");
        foreach (var e in state.Pending) ValidateStoryEvent(e);
        if (!state.Enabled && state.Pending.Count > 0 || state.Pending.Select(e => e.Id).Distinct().Count() != state.Pending.Count || state.Pending.GroupBy(e => e.Kind).Any(g => g.Count() > 1) ||
            state.Pending.Any(e => e.Due > SimulationTime + 24 * StoryHour) ||
            state.History.Any(h => h is null || !StoryText(h.EventId, 100) || !StoryText(h.Kind, 32) || !StoryText(h.Outcome, 32) || !StoryText(h.Detail, 512) || !StoryTime(h.Time) || h.Time > SimulationTime) ||
            state.Pending.Any(e => state.History.Any(h => h.EventId == e.Id))) throw new InvalidDataException("叙事队列或历史记录无效。");
        if (state.Turn is {} turn)
        {
            if (!Guid.TryParseExact(turn.Id, "N", out _) || !StoryText(turn.Model, 128) || !StoryText(turn.Provider, 32) || turn.PromptVersion != 1 ||
                !StoryTime(turn.ObservedAt) || turn.ObservedAt > SimulationTime || !StoryText(turn.VitalState, 64) ||
                turn.Status is not ("ready" or "requesting" or "received" or "waiting" or "completed") || turn.Requests is < 0 or > 4 ||
                !StoryText(turn.AcceptedJson, 32768) || !StoryText(turn.AcceptedCallId, 100) || turn.Messages is null || turn.Messages.Count > 12 ||
                turn.Messages.Any(m => m is null || m.Role is not ("system" or "user" or "assistant") || !StoryText(m.Content, 32768)) ||
                turn.Messages.Sum(m => (long)m.Content.Length) > 48000 || turn.Receipts is null || turn.Receipts.Count > 4 || turn.InputTokens < 0 || turn.OutputTokens < 0)
                throw new InvalidDataException("叙事 Agent 存档无效。");
            if (turn.Receipts.Select(r => r?.CallId).Distinct().Count() != turn.Receipts.Count) throw new InvalidDataException("叙事回执重复。");
            for (int i = 0; i < turn.Receipts.Count; i++)
            {
                var r = turn.Receipts[i];
                if (r is null || r.CallId != turn.Id + ":" + i || !StoryText(r.Command, 32768) || !StoryText(r.Result, 4096)) throw new InvalidDataException("叙事回执无效。");
            }
            if (turn.AcceptedJson != "")
            {
                if (turn.AcceptedCallId != turn.Id + ":" + turn.Receipts.Count && !turn.Receipts.Any(r => r.CallId == turn.AcceptedCallId && r.Command == turn.AcceptedJson)) throw new InvalidDataException("待处理调用 ID 无效。");
                try { using var doc = JsonDocument.Parse(turn.AcceptedJson, new JsonDocumentOptions { MaxDepth = 16 }); if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
                catch (JsonException) { throw new InvalidDataException("待处理的叙事 JSON 无效。"); }
                turn.Status = "received";
            }
            else if (turn.AcceptedCallId != "" || turn.Status == "received") throw new InvalidDataException("叙事调用缺少 JSON。");
            else if (turn.Status is "requesting" or "ready")
            { turn.Status = "waiting"; state.Error = "读档中断了未完成的 AI 请求；已有事件保留，可点击重试。"; }
        }
        Story = state;
    }
}
