namespace UrlInsight.Core.Storage;

/// <summary>ユーザーデータの保存場所。既定は %LOCALAPPDATA%\URLInsight。</summary>
public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "URLInsight");
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string CacheDb => Path.Combine(Root, "cache.db");
    public string SecretsDir => Path.Combine(Root, "secrets");
    public string LogsDir => Path.Combine(Root, "logs");
    public string NativeHostDir => Path.Combine(Root, "native-host");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SecretsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
