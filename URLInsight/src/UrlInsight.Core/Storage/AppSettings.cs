using System.Text.Json;
using System.Text.Json.Serialization;

namespace UrlInsight.Core.Storage;

public enum DisplayMode { HoverOnly, Pinned }
public enum CardSide { Right, Left }

/// <summary>
/// 秘密情報を含まない設定。APIキーはここに保存しない(<see cref="ISecretStore"/> を使う)。
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool FirstRunCompleted { get; set; }
    public bool StartWithWindows { get; set; }
    public int HoverDelayMs { get; set; } = 600;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.HoverOnly;
    public CardSide CardSide { get; set; } = CardSide.Right;
    public bool Paused { get; set; }
    /// <summary>拡張機能なしでも、Windows の UI オートメーションでカーソル下のリンク/URL文字列を検出する。</summary>
    public bool UseUiAutomationHover { get; set; } = true;
    /// <summary>要約(または検索結果)ができたカードを自動で固定し、別のリンクにカーソルを重ねても消さない。</summary>
    public bool AutoPinSummaries { get; set; } = true;
    /// <summary>Chrome のリンクにカーソルを重ねたら、Chrome のサイドバーの Gemini を開いて要約を頼む(アプリのカードは出さない)。</summary>
    public bool UseChromeGemini { get; set; } = true;
    /// <summary>Edge のリンクにカーソルを重ねたら、Edge のサイドバーの Copilot を開いて要約を頼む(アプリのカードは出さない)。</summary>
    public bool UseEdgeCopilot { get; set; } = true;

    public string ProviderId { get; set; } = "none";
    public string Model { get; set; } = string.Empty;
    public string CustomEndpoint { get; set; } = string.Empty;
    /// <summary>true: 毎回送信前に確認。false: プロバイダごとの初回のみ確認。</summary>
    public bool ConfirmBeforeSend { get; set; } = true;
    /// <summary>初回確認済みの「プロバイダID|ホスト」。</summary>
    public List<string> ConsentedProviders { get; set; } = new();
    public int MaxCharsToSend { get; set; } = 12_000;

    public bool CacheEnabled { get; set; } = true;
    public int CacheTtlDays { get; set; } = 7;
    public int CacheMaxEntries { get; set; } = 1000;
    public int CacheMaxMegabytes { get; set; } = 250;

    public List<string> IgnoredUrls { get; set; } = new();

    public string LogLevel { get; set; } = "Info";
    public string ExtensionId { get; set; } = string.Empty;
    public bool RegisterForEdge { get; set; }

    public void Clamp()
    {
        HoverDelayMs = Math.Clamp(HoverDelayMs, 300, 1500);
        MaxCharsToSend = Math.Clamp(MaxCharsToSend, 1000, 50_000);
        CacheTtlDays = Math.Clamp(CacheTtlDays, 1, 365);
        CacheMaxEntries = Math.Clamp(CacheMaxEntries, 10, 100_000);
        CacheMaxMegabytes = Math.Clamp(CacheMaxMegabytes, 1, 4096);
        if (LogLevel is not ("Error" or "Warning" or "Info" or "Debug")) LogLevel = "Info";
        ProviderId ??= "none";
        Model ??= string.Empty;
        CustomEndpoint ??= string.Empty;
        ExtensionId ??= string.Empty;
        ConsentedProviders ??= new();
        IgnoredUrls ??= new();
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
}

public sealed class SettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _lock = new();

    public SettingsStore(string path) => _path = path;

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return new AppSettings();
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
                settings.Clamp();
                return settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                // 壊れた設定は退避して初期値で起動する
                try { File.Copy(_path, _path + ".broken", overwrite: true); } catch (IOException) { }
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            settings.Clamp();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}
