using System.Text.Json;
using NoDwarf.Simulation;

namespace NoDwarf.Services.Llm;

public static class StorytellerTools
{
    private static object Number(double min, double max, string type = "number") => new { type, minimum = min, maximum = max };
    private static object Text(int max) => new { type = "string", maxLength = max };
    private static object Choice(params string[] values) => new Dictionary<string, object> { ["type"] = "string", ["enum"] = values };
    private static object Tool(string name, string description, params (string Name, object Schema)[] fields) => new
    {
        name, description, parameters = new { type = "object", properties = fields.ToDictionary(f => f.Name, f => f.Schema), required = fields.Select(f => f.Name).ToArray(), additionalProperties = false }
    };
    public static readonly string Catalog = JsonSerializer.Serialize(new[]
    {
        Tool("schedule_raid", "安排有限规模的徒手袭击。每人占 100 威胁点；受宽限、恢复、冷却和当前战力限制。", ("count",Number(1,DemoWorld.MaxStoryRaid,"integer")),("delay_hours",Number(2,24)),("reason",Text(240))),
        Tool("schedule_supplies", "在可行动居民所在安全位置发现物资；必须使用机会预算，并遵守冷却。",("item",Choice("meal","wood","bandage","grain_seed")),("count",Number(1,100,"integer")),("delay_hours",Number(.1,24)),("reason",Text(240))),
        Tool("schedule_weather", "短期天气变化；结束自动恢复自然天气。",("weather",Choice("clear","cloudy","rain","fog")),("duration_hours",Number(1,12)),("delay_hours",Number(.1,24)),("reason",Text(240))),
        Tool("cancel_event", "取消尚未发生的事件，不能撤销已发生的后果。",("event_id",Text(100)),("reason",Text(240))),
        Tool("wait", "让营地休养，安排下次决策；不会取消已排队事件。",("hours",Number(1,24)),("reason",Text(240)))
    });

    public static string Apply(DemoWorld world, StoryTurn turn)
    {
        string call = turn.AcceptedCallId, command = turn.AcceptedJson;
        var existing = turn.Receipts.SingleOrDefault(r => r.CallId == call);
        if (existing is not null)
        {
            if (existing.Command != command) throw new LlmException("调用 ID 与已保存决定冲突。");
            using var prior = AgentJson.Parse(command);
            if (prior.RootElement.GetProperty("tool").GetString() is "finish" or "wait") turn.Status = "completed";
            return existing.Result;
        }
        if (call != turn.Id + ":" + turn.Receipts.Count || turn.Receipts.Count >= 4) throw new LlmException("事件调用序号无效。");
        if (!world.Story.Enabled || world.Story.Turn != turn) throw new LlmException("叙事会话已失效。");
        if (world.SimulationTime - turn.ObservedAt > 6 * DemoWorld.StoryHour || world.StoryVitalState() != turn.VitalState)
            throw new LlmException("营地人员或战况已改变，旧决定未执行，请重新观察。");
        using var parsed = AgentJson.Parse(command); var root = parsed.RootElement;
        if (turn.Messages.Count > 9 || turn.Messages.Sum(m => (long)m.Content.Length) + command.Length + 4096 > 48000)
            throw new LlmException("叙事上下文已达上限，本次工具未执行，请重新决策。");
        AgentJson.Fields(root, "tool", "arguments");
        string name = AgentJson.Text(root.GetProperty("tool"), 80); var args = root.GetProperty("arguments");
        double Num(string field, double min, double max, bool integer = false)
        {
            if (!args.TryGetProperty(field, out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double n) ||
                !double.IsFinite(n) || n < min || n > max || integer && n != Math.Floor(n)) throw new LlmException("事件数值参数越界：" + field);
            return n;
        }
        string Str(string field, int max = 240) => AgentJson.Text(args.GetProperty(field), max);
        ResidentResult result;
        if (name == "finish")
        {
            AgentJson.Fields(args, "summary"); string summary = Str("summary", 512);
            result = new(true, summary); turn.Status = "completed";
        }
        else if (name == "wait")
        {
            AgentJson.Fields(args, "hours", "reason"); double hours = Num("hours", 1, 24); string reason = Str("reason");
            world.Story.NextDecisionAt = world.SimulationTime + hours * DemoWorld.StoryHour;
            result = new(true, "休养安排已生效：" + reason); turn.Status = "completed";
        }
        else if (name == "cancel_event")
        {
            AgentJson.Fields(args, "event_id", "reason"); result = world.CancelStoryEvent(Str("event_id", 100), Str("reason"));
        }
        else
        {
            var e = new StoryEvent { Id = call };
            switch (name)
            {
                case "schedule_raid":
                    AgentJson.Fields(args, "count", "delay_hours", "reason");
                    e.Kind = "raid"; e.Count = (int)Num("count", 1, DemoWorld.MaxStoryRaid, true); break;
                case "schedule_supplies":
                    AgentJson.Fields(args, "item", "count", "delay_hours", "reason");
                    e.Kind = "supplies"; e.Item = Str("item", 40); e.Count = (int)Num("count", 1, 100, true);
                    if (e.Item is not ("meal" or "wood" or "bandage" or "grain_seed")) throw new LlmException("不允许的补给物品。");
                    break;
                case "schedule_weather":
                    AgentJson.Fields(args, "weather", "duration_hours", "delay_hours", "reason");
                    e.Kind = "weather"; e.Weather = Str("weather", 20); e.Duration = Num("duration_hours", 1, 12) * DemoWorld.StoryHour;
                    if (e.Weather is not ("clear" or "cloudy" or "rain" or "fog")) throw new LlmException("不允许的天气。");
                    break;
                default: throw new LlmException("未注册的叙事工具。");
            }
            e.Due = world.SimulationTime + Num("delay_hours", e.Kind == "raid" ? 2 : .1, 24) * DemoWorld.StoryHour;
            e.Reason = Str("reason"); result = world.ScheduleStoryEvent(e);
        }
        string json = JsonSerializer.Serialize(new { ok = result.Ok, message = result.Message, event_id = name.StartsWith("schedule_", StringComparison.Ordinal) && result.Ok ? call : null });
        turn.Receipts.Add(new() { CallId = call, Command = command, Result = json });
        return json;
    }
}
