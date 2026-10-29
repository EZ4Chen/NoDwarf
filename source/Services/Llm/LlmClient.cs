using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NoDwarf.Services.Llm;

public sealed record LlmMessage(string role, string content);
public sealed record LlmReply(string Json, int? InputTokens, int? OutputTokens, long Milliseconds);
public interface ILlmClient
{
    Task<LlmReply> CompleteAsync(LlmConfiguration config, IReadOnlyList<LlmMessage> messages, CancellationToken cancellation);
}

// One app-wide instance: saving settings does not reset the session budget or concurrency cap.
public sealed class LlmClient : ILlmClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _slots = new(2, 2);
    private int _requests;
    public int Requests => Volatile.Read(ref _requests);
    public LlmClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }
    public async Task<LlmReply> CompleteAsync(LlmConfiguration config, IReadOnlyList<LlmMessage> messages, CancellationToken cancellation)
    {
        config.Validate();
        if (messages.Count is < 1 or > 24 || messages.Any(m => m.role is not ("system" or "user" or "assistant") || m.content is null) ||
            messages.Sum(m => (long)m.content.Length) > 48000)
            throw new LlmException("LLM 上下文过大或消息格式无效。");
        if (!await _slots.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new LlmException("LLM 正忙（最多两个并发请求），请稍后重试。");
        try
        {
            // Reserve before sending. Failed/uncertain requests count too; no automatic paid retries.
            while (true)
            {
                int current = Requests;
                if (current >= config.Settings.SessionRequestLimit) throw new LlmException("已达到本次启动的请求上限，请调整设置或下次启动后继续。");
                if (Interlocked.CompareExchange(ref _requests, current + 1, current) == current) break;
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(config.Settings.TimeoutSeconds));
            var body = new Dictionary<string, object>
            {
                ["model"] = config.Settings.Model.Trim(), ["messages"] = messages,
                ["stream"] = false, ["max_tokens"] = config.Settings.MaxOutputTokens,
                ["response_format"] = new { type = "json_object" }
            };
            if (config.Settings.Provider == "deepseek") body["thinking"] = new { type = "disabled" };
            using var request = new HttpRequestMessage(HttpMethod.Post, config.Settings.Endpoint());
            if (config.ApiKey != "") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var watch = Stopwatch.StartNew();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new LlmException((int)response.StatusCode switch
            {
                401 or 403 => "认证失败，请检查 API 密钥和访问权限。",
                404 => "接口或模型不存在，请检查接口地址和模型名称。",
                429 => "供应商限流或额度不足，请稍后重试。",
                >= 300 and < 400 => "接口返回重定向；请直接填写最终 API 地址。",
                _ => $"LLM 请求失败（HTTP {(int)response.StatusCode}）。请检查供应商配置。"
            });
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > 262144) throw new LlmException("供应商响应过大，已停止读取。");
                buffer.Write(chunk, 0, read);
            }
            using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            var choice = root.GetProperty("choices")[0];
            if (choice.GetProperty("finish_reason").GetString() != "stop") throw new LlmException("模型结果未完整结束，未执行任何工具。");
            string json = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
            using var parsed = AgentJson.Parse(json);
            int? input = null, output = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var n) && n >= 0) input = n;
                if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out n) && n >= 0) output = n;
            }
            return new(json, input, output, watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new LlmException("LLM 请求超时；没有自动重试。"); }
        catch (Exception e) when (e is HttpRequestException or IOException) { throw new LlmException("无法连接或读取 LLM 服务，请检查地址和网络。"); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        { throw new LlmException("供应商未返回有效的完整 JSON 结果。"); }
        finally { _slots.Release(); }
    }
    public void Dispose() { _http.Dispose(); _slots.Dispose(); }
}

public static class LlmRuntime
{
    public static LlmConfiguration Configuration { get; private set; } = new();
    public static LlmClient Client { get; } = new();
    public static bool Initialized { get; private set; }
    public static void Configure(LlmConfiguration configuration)
    { configuration.Validate(); Configuration = configuration; Initialized = true; }
}
