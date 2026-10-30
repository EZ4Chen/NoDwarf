using System.Text.Json;
using NoDwarf.Simulation;

namespace NoDwarf.Services.Llm;

public static class StorytellerTools
{
    private static object Number(double min, double max, string type = "number") => new { type, minimum = min, maximum = max };
    private static object Text(int max) => new { type = "string", maxLength = max };
    private static object Tool(string name, string description, params (string Name, object Schema)[] fields) => new
    {
        name, description, parameters = new { type = "object", properties = fields.ToDictionary(f => f.Name, f => f.Schema), required = fields.Select(f => f.Name).ToArray(), additionalProperties = false }
    };
    public static string Catalog=>CatalogFor(ContentRuntime.Current);
    public static string CatalogFor(ContentCatalog content)=>JsonSerializer.Serialize(content.StoryEvents.Values.Select(e=>e.ToolSchema()).Concat(new[]{
        Tool("cancel_event", "取消尚未发生的事件，不能撤销已发生的后果。",("event_id",Text(100)),("reason",Text(240))),
        Tool("wait", "让营地休养，安排下次决策；不会取消已排队事件。",("hours",Number(1,24)),("reason",Text(240)))
    }));

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
            if(!world.Content.StoryTools.TryGetValue(name,out var definition))throw new LlmException("未注册的叙事工具。");
            var e=definition.Create(args,call,world.SimulationTime);
            result=world.ScheduleStoryEvent(e);
        }
        string json = JsonSerializer.Serialize(new { ok = result.Ok, message = result.Message, event_id = world.Content.StoryTools.ContainsKey(name) && result.Ok ? call : null });
        turn.Receipts.Add(new() { CallId = call, Command = command, Result = json });
        return json;
    }
}
