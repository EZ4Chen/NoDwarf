using System.Text.Json;

namespace NoDwarf.Services.Llm;

public sealed record LlmSettings
{
    public string Provider { get; init; } = "deepseek";
    public string BaseUrl { get; init; } = "https://api.deepseek.com";
    public string Model { get; init; } = "deepseek-flash";
    public int TimeoutSeconds { get; init; } = 45;
    public int MaxOutputTokens { get; init; } = 1024;
    public int SessionRequestLimit { get; init; } = 100;

    public Uri Endpoint()
    {
        if (Provider is not ("deepseek" or "compatible")) throw new LlmException("供应商类型无效。");
        if (string.IsNullOrWhiteSpace(BaseUrl) || BaseUrl.Length > 512 ||
            !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ||
            uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
            throw new LlmException("接口地址需要 HTTPS；本机服务可使用 HTTP。地址不能包含密钥、查询参数或片段。");
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 128 || Model.Any(char.IsControl))
            throw new LlmException("请输入有效的模型名称（最多 128 字符）。");
        if (TimeoutSeconds is < 5 or > 180 || MaxOutputTokens is < 128 or > 8192 || SessionRequestLimit is < 1 or > 10000)
            throw new LlmException("请求限制超出允许范围。");
        var url = uri.AbsoluteUri.TrimEnd('/');
        return new Uri(url.EndsWith("/chat/completions", StringComparison.Ordinal) ? url : url + "/chat/completions");
    }
}

// Credentials are deliberately outside LlmSettings, world saves, and record ToString().
public sealed class LlmConfiguration
{
    public LlmSettings Settings { get; init; } = new();
    public string ApiKey { get; init; } = "";
    public bool RememberKey { get; init; }
    public void Validate()
    {
        Settings.Endpoint();
        if (ApiKey.Length > 4096 || ApiKey.Any(char.IsControl)) throw new LlmException("API 密钥格式无效。");
    }
}

public static class LlmSettingsStore
{
    private sealed class Stored
    {
        public int Version { get; set; } = 1;
        public LlmSettings Settings { get; set; } = new();
        public string SavedKey { get; set; } = "";
    }
    public static LlmConfiguration Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            if (new FileInfo(path).Length > 16384) throw new LlmException("LLM 设置文件过大。");
            var saved = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            if (saved is null || saved.Version != 1 || saved.Settings is null || saved.SavedKey is null)
                throw new LlmException("LLM 设置版本或内容无效。");
            var config = new LlmConfiguration { Settings = saved.Settings, ApiKey = saved.SavedKey, RememberKey = saved.SavedKey != "" };
            config.Validate();
            return config;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new LlmException("无法读取 LLM 设置，请在设置页重新保存。"); }
    }
    public static void Save(string path, LlmConfiguration config)
    {
        config.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
                JsonSerializer.Serialize(stream, new Stored { Settings = config.Settings, SavedKey = config.RememberKey ? config.ApiKey : "" });
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class LlmException(string message) : Exception(message);
