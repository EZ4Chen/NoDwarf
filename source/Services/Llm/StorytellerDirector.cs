using System.Text.Json;
using NoDwarf.Simulation;

namespace NoDwarf.Services.Llm;

// Pump is called only by the simulation owner. The Task receives immutable messages/configuration,
// never a world reference. SaveJson can therefore capture a consistent turn + event queue + receipts.
public sealed class StorytellerDirector(ILlmClient client) : IDisposable
{
    private DemoWorld? _world;
    private StoryTurn? _turn;
    private LlmConfiguration? _config;
    private CancellationTokenSource? _cancel;
    private Task<LlmReply>? _pending;
    public bool InFlight => _pending is not null;
    private void Cancel()
    {
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
        if (_pending is {} task) _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        _pending = null; _turn = null;
    }
    public void Dispose() { Cancel(); _world = null; }
    public void Pump(DemoWorld world, LlmConfiguration config, bool advance)
    {
        if (_world != world) { Cancel(); _world = world; }
        var story = world.Story;
        if (!story.Enabled) { Cancel(); return; }
        if (_pending is not null && (_turn != story.Turn || !ReferenceEquals(config, _config)))
        {
            Cancel();
            if (story.Turn is {} stale) { stale.Status = "waiting"; story.Error = "AI 配置或会话已变化，请重试。"; }
        }
        if (_pending is {} request)
        {
            if (!request.IsCompleted) return;
            var received = _turn!; _pending = null; _cancel?.Dispose(); _cancel = null;
            try
            {
                var reply = request.GetAwaiter().GetResult();
                using var parsed = AgentJson.Parse(reply.Json);
                received.AcceptedJson = JsonSerializer.Serialize(parsed.RootElement); received.AcceptedCallId = received.Id + ":" + received.Receipts.Count;
                received.InputTokens += reply.InputTokens ?? 0; received.OutputTokens += reply.OutputTokens ?? 0;
                received.Status = "received"; story.Error = "";
            }
            catch (Exception e)
            { received.Status = "waiting"; story.Error = e is LlmException ? e.Message : "AI 请求失败或被取消，可重试；没有自动改用随机事件。"; }
            return; // Received JSON is a separate saveable phase, including while paused.
        }
        if (!advance) return;
        if (story.Turn is { Status: "waiting" }) return;
        if (story.Turn is { AcceptedJson.Length: > 0 } accepted)
        {
            try
            {
                string json = accepted.AcceptedJson;
                string result = StorytellerTools.Apply(world, accepted);
                accepted.Messages.Add(new() { Role = "assistant", Content = json });
                accepted.Messages.Add(new() { Role = "user", Content = JsonSerializer.Serialize(new { tool_result = result, pending_events = story.Pending.Select(e => new { e.Id, e.Kind, e.Due }) }) });
                accepted.AcceptedJson = ""; accepted.AcceptedCallId = "";
                if (accepted.Status == "completed" || accepted.Requests >= 4) Complete(world, accepted);
                else accepted.Status = "ready";
            }
            catch (Exception e) when (e is LlmException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // These checks fail before world mutations. Accepted actions have receipts in the same snapshot.
                accepted.AcceptedJson = ""; accepted.AcceptedCallId = ""; accepted.Status = "waiting";
                story.Error = e is LlmException ? e.Message : "事件 JSON 无效，未执行；请重试。";
            }
            return;
        }
        if (story.Turn is null || story.Turn.Status == "completed")
        {
            if (world.SimulationTime < story.NextDecisionAt) return;
            if (config.ApiKey == "" && !config.Settings.Endpoint().IsLoopback)
            {
                story.Error = "请先在主菜单设置 AI 服务和密钥，再重新决策。";
                story.Turn = new() { Status = "waiting", ObservedAt = world.SimulationTime, VitalState = world.StoryVitalState() };
                return;
            }
            var value = world.Values(true);
            var turn = new StoryTurn { Model = config.Settings.Model, Provider = config.Settings.Provider,
                ObservedAt = world.SimulationTime, VitalState = world.StoryVitalState() };
            turn.Messages.Add(new() { Role = "system", Content = JsonAgent.Protocol +
                "\n你是 NoDwarf 的营地叙事者。根据真实状态安排有因果的机会、压力和恢复。只使用已实现工具，不虚构外部国家、人物或承诺。" +
                "人口损失后优先恢复；允许本轮只 wait。事件时间按游戏小时计算。合理使用预算，不追求耗尽；排队不代表已发生。" +
                "reason只解释安排依据，不得声称已发生或派遣具体居民执行行动。补给工具只生成物品，不会指派采集或移动；天气不会直接改变清洁或心情，不要承诺这些效果。" +
                "工具失败可按反馈调整，最多四次请求。说明使用中文。\nTools: " + StorytellerTools.CatalogFor(world.Content) });
            turn.Messages.Add(new() { Role = "user", Content = JsonSerializer.Serialize(new
            {
                time = world.SimulationTime, game_hour_seconds = DemoWorld.StoryHour, value_catalog_version = ValueCatalog.Version,
                value, threat_budget = world.StoryThreatBudget(value), gift_budget = world.StoryGiftBudget(value),
                content_revision=world.Content.GameplayHash,
                facilities=world.Buildings.Values.GroupBy(b=>world.BuildingDefinition(b).Id).Take(24).Select(g=>new{id=g.Key,name=world.Content.Building(g.Key).Name,function=world.Content.Building(g.Key).Handler.ToString(),count=g.Count()}),
                supply_items = world.Content.Items.Where(i=>world.Content.StoryEvents.Values.Any(e=>e.Items.Contains(i.Id))).Select(i=>new{i.Id,i.Name,i.Category,unit_value=i.BaseValue,edible=i.Edible}),
                story.GraceUntil, story.RaidAfter, story.SuppliesAfter, story.WeatherAfter, story.RecoveryUntil,
                weather = world.Weather.Id, temperature = world.OutdoorTemperature,
                residents = world.Workers.Take(8).Select(w => new { w.Id, w.Name, w.Person.Dead, w.Person.Incapacitated, w.Person.Traits, memories = w.Person.Memories.Take(2) }),
                pending = story.Pending, history = story.History.TakeLast(12)
            }) });
            story.Turn = turn; story.Error = "";
            if(turn.Messages.Any(m=>m.Content.Length>32768)||turn.Messages.Sum(m=>m.Content.Length)>44000)
            {turn.Status="waiting";story.Error="内容目录和观察超出叙事上下文预算，请减少启用事件或补给物品范围。";return;}
            story.NextDecisionAt = world.SimulationTime + 6 * DemoWorld.StoryHour;
        }
        var active = story.Turn!;
        if (active.Requests >= 4) { Complete(world, active); return; }
        if (world.SimulationTime - active.ObservedAt > 6 * DemoWorld.StoryHour || world.StoryVitalState() != active.VitalState)
        { active.Status = "waiting"; story.Error = "观察已过期，请重新决策。"; return; }
        var messages = active.Messages.Select(m => new LlmMessage(m.Role, m.Content)).ToArray();
        _cancel = new(); var token = _cancel.Token;
        _turn = active; _config = config; active.Requests++; active.Status = "requesting";
        // No mutable simulation data is captured by this worker.
        var transport = client;
        _pending = Task.Run(() => transport.CompleteAsync(config, messages, token), token);
    }
    private static void Complete(DemoWorld world, StoryTurn turn)
    {
        turn.Status = "completed";
        var history = world.Story.History;
        if (history.All(h => h.EventId != turn.Id))
        {
            history.Add(new(turn.Id, "decision", world.SimulationTime, "completed", $"本轮 {turn.Requests} 次请求，{turn.Receipts.Count} 个工具回执。"));
            if (history.Count > 64) history.RemoveAt(0);
        }
    }
}
