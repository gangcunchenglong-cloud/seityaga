using System.Reflection;
using UrlInsight.Core;
using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Platform;

namespace UrlInsight.Mac.Services;

/// <summary>
/// アプリ全体で共有するサービス群(Windows 版の AppServices と同じ役割)。
/// 要約の処理(取得・AI・キャッシュ)は Windows 版と共通の UrlInsight.Core を使う。
/// 違いは、API キーの保存先がキーチェーンであることと、Chrome 拡張との接続(ネイティブメッセージング)を持たないこと。
/// </summary>
internal sealed class MacServices : IDisposable
{
    private readonly object _settingsLock = new();
    private AppSettings _settings;

    public MacServices(AppPaths paths, ISecretStore? persistentSecrets = null)
    {
        Paths = paths;
        SettingsStore = new SettingsStore(paths.SettingsFile);
        _settings = SettingsStore.Load();
        AppLog.SetLevel(AppLog.ParseLevel(_settings.LogLevel));
        Secrets = new LayeredSecretStore(persistentSecrets ?? new KeychainSecretStore(), new SessionSecretStore());
        Cache = OpenCache(paths);
        Fetcher = new SafeHttpFetcher();
        Content = new ContentService(Fetcher, () => Secrets.TryGet(LayeredSecretStore.YouTubeKeyName));
        Pipeline = new SummaryPipeline(Content, Cache, () => Settings, CreateProvider, RememberConsent);
    }

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";

    public AppPaths Paths { get; }
    public SettingsStore SettingsStore { get; }
    public LayeredSecretStore Secrets { get; }
    public SummaryCache? Cache { get; }
    public SafeHttpFetcher Fetcher { get; }
    public ContentService Content { get; }
    public SummaryPipeline Pipeline { get; }

    public AppSettings Settings
    {
        get { lock (_settingsLock) return _settings; }
    }

    public event Action? SettingsChanged;

    public void UpdateSettings(Action<AppSettings> mutate)
    {
        AppSettings next;
        lock (_settingsLock)
        {
            next = _settings.Clone();
            mutate(next);
            next.Clamp();
            SettingsStore.Save(next);
            _settings = next;
        }
        AppLog.SetLevel(AppLog.ParseLevel(next.LogLevel));
        SettingsChanged?.Invoke();
    }

    private void RememberConsent(string key)
        => UpdateSettings(s => { if (!s.ConsentedProviders.Contains(key)) s.ConsentedProviders = s.ConsentedProviders.Append(key).ToList(); });

    public ISummarizerProvider? CreateProvider()
    {
        var s = Settings;
        var preset = ProviderCatalog.Get(s.ProviderId);
        var key = preset.RequiresKey ? Secrets.TryGet(LayeredSecretStore.ProviderKeyName(preset.Id)) : null;
        try
        {
            return ProviderCatalog.Create(s.ProviderId, s.CustomEndpoint, s.Model, key);
        }
        catch (InsightException)
        {
            return null;
        }
    }

    public (bool ok, string text) DescribeProvider()
    {
        var s = Settings;
        var preset = ProviderCatalog.Get(s.ProviderId);
        if (preset.Kind == ProviderKind.None) return (false, "AI未設定（ページ情報のみ表示）");
        if (preset.Kind == ProviderKind.Fixture) return (true, "テスト用プロバイダ（AI送信なし）");
        if (string.IsNullOrWhiteSpace(s.Model)) return (false, $"{preset.DisplayName}: モデル未入力");
        if (!Secrets.Exists(LayeredSecretStore.ProviderKeyName(preset.Id))) return (false, $"{preset.DisplayName}: APIキー未設定");
        return (true, $"{preset.DisplayName} ・ {s.Model}");
    }

    public bool IsIgnored(Uri normalized) => Settings.IgnoredUrls.Contains(normalized.ToString());

    private static SummaryCache? OpenCache(AppPaths paths)
    {
        try
        {
            return new SummaryCache(paths.CacheDb);
        }
        catch (Exception ex)
        {
            AppLog.Error("cache open failed; recreating", ex);
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(paths.CacheDb)) File.Move(paths.CacheDb, paths.CacheDb + ".broken", overwrite: true);
                return new SummaryCache(paths.CacheDb);
            }
            catch (Exception ex2)
            {
                AppLog.Error("cache unavailable", ex2);
                return null;
            }
        }
    }

    public void Dispose() => Fetcher.Dispose();
}
