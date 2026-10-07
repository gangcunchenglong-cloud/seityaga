using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UrlInsight.Core.Storage;

namespace UrlInsight.App.Platform;

/// <summary>
/// Chrome / Edge への Native Messaging ホスト登録(ユーザー単位 HKCU。管理者権限不要)。
/// マニフェスト JSON を %LOCALAPPDATA%\URLInsight\native-host に置き、レジストリからその場所を指す。
/// </summary>
internal static class NativeHostRegistrar
{
    public const string HostName = "com.urlinsight.linklens";
    /// <summary>同梱拡張(manifest.json の "key")から決まる拡張ID。</summary>
    public const string DefaultExtensionId = "jmmabchpljfpflppkhkhbfejbhjaadii";
    public const string HostExeName = "URLInsight.NativeHost.exe";

    private const string ChromeKey = @"Software\Google\Chrome\NativeMessagingHosts\" + HostName;
    private const string EdgeKey = @"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName;

    private static readonly Regex ExtensionIdPattern = new("^[a-p]{32}$", RegexOptions.Compiled);

    public static bool IsValidExtensionId(string? id) => id != null && ExtensionIdPattern.IsMatch(id);

    public static string EffectiveExtensionId(AppSettings s)
        => IsValidExtensionId(s.ExtensionId) ? s.ExtensionId : DefaultExtensionId;

    public static string HostExePath => Path.Combine(AppContext.BaseDirectory, HostExeName);

    public static string ManifestPath(AppPaths paths) => Path.Combine(paths.NativeHostDir, HostName + ".json");

    public static void Register(AppPaths paths, string extensionId, bool includeEdge)
    {
        if (!IsValidExtensionId(extensionId)) throw new ArgumentException("拡張機能IDの形式が正しくありません(英小文字a〜pの32文字)");
        if (!File.Exists(HostExePath)) throw new FileNotFoundException("ネイティブホストの実行ファイルが見つかりません", HostExePath);

        Directory.CreateDirectory(paths.NativeHostDir);
        var manifest = new JsonObject
        {
            ["name"] = HostName,
            ["description"] = "URL Insight / LinkLens native messaging host",
            ["path"] = HostExePath,
            ["type"] = "stdio",
            ["allowed_origins"] = new JsonArray { $"chrome-extension://{extensionId}/" },
        };
        var manifestPath = ManifestPath(paths);
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        using (var key = Registry.CurrentUser.CreateSubKey(ChromeKey, writable: true))
            key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);

        if (includeEdge)
        {
            using var key = Registry.CurrentUser.CreateSubKey(EdgeKey, writable: true);
            key.SetValue(string.Empty, manifestPath, RegistryValueKind.String);
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(EdgeKey, throwOnMissingSubKey: false);
        }
    }

    public static void Unregister(AppPaths paths)
    {
        Registry.CurrentUser.DeleteSubKeyTree(ChromeKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(EdgeKey, throwOnMissingSubKey: false);
        var manifestPath = ManifestPath(paths);
        if (File.Exists(manifestPath)) File.Delete(manifestPath);
    }

    /// <summary>Chrome 向け登録が存在し、現在の実行ファイルと拡張IDを指しているか。</summary>
    public static bool IsRegistered(AppPaths paths, string extensionId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ChromeKey);
            if (key?.GetValue(string.Empty) is not string manifestPath || !File.Exists(manifestPath)) return false;
            var node = JsonNode.Parse(File.ReadAllText(manifestPath));
            var path = node?["path"]?.GetValue<string>();
            var origin = node?["allowed_origins"]?[0]?.GetValue<string>();
            return string.Equals(path, HostExePath, StringComparison.OrdinalIgnoreCase)
                   && origin == $"chrome-extension://{extensionId}/";
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>登録済みだが実行ファイルの場所が変わった場合などに自動で登録し直す。</summary>
    public static void RepairIfMoved(AppPaths paths, AppSettings settings)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ChromeKey);
            if (key == null) return; // 未登録(ユーザーが登録していない)なら何もしない
            var id = EffectiveExtensionId(settings);
            if (!IsRegistered(paths, id) && File.Exists(HostExePath)) Register(paths, id, settings.RegisterForEdge);
        }
        catch (Exception)
        {
        }
    }
}
