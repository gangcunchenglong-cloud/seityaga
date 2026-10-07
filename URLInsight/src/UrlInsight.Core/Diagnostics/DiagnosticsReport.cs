using System.Runtime.InteropServices;
using System.Text;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Diagnostics;

/// <summary>
/// ユーザー操作時のみ作成する診断情報。URL・キー・本文を含めない。
/// 作成後に <see cref="Redactor.ContainsSensitive"/> で自動検査し、残っていれば該当行を除去する。
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(string appVersion, AppSettings settings, CacheStats? cache, bool extensionConnected,
        bool nativeHostRegistered, IEnumerable<string> logLines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("URL Insight 診断情報");
        sb.AppendLine($"作成日時: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"アプリ版: {appVersion}");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine();
        sb.AppendLine("[設定(秘密情報を除く)]");
        sb.AppendLine($"プロバイダ: {settings.ProviderId}");
        sb.AppendLine($"モデル設定あり: {(!string.IsNullOrWhiteSpace(settings.Model) ? "はい" : "いいえ")}");
        sb.AppendLine($"送信前に毎回確認: {settings.ConfirmBeforeSend}");
        sb.AppendLine($"送信上限文字数: {settings.MaxCharsToSend}");
        sb.AppendLine($"ホバー待ち時間: {settings.HoverDelayMs}ms / 表示モード: {settings.DisplayMode} / 位置: {settings.CardSide}");
        sb.AppendLine($"一時停止: {settings.Paused}");
        sb.AppendLine($"キャッシュ: {(settings.CacheEnabled ? "有効" : "無効")} 期限{settings.CacheTtlDays}日 上限{settings.CacheMaxEntries}件/{settings.CacheMaxMegabytes}MB");
        sb.AppendLine($"無視リスト件数: {settings.IgnoredUrls.Count}");
        sb.AppendLine($"ログレベル: {settings.LogLevel}");
        sb.AppendLine();
        sb.AppendLine("[状態]");
        sb.AppendLine($"拡張接続: {extensionConnected}");
        sb.AppendLine($"ネイティブホスト登録: {nativeHostRegistered}");
        if (cache != null) sb.AppendLine($"キャッシュ件数: {cache.Count} / 推定容量: {cache.Bytes / 1024}KB");
        sb.AppendLine();
        sb.AppendLine("[最近のログ]");
        foreach (var line in logLines) sb.AppendLine(Redactor.Redact(line));

        var text = sb.ToString();
        if (!Redactor.ContainsSensitive(text)) return text;
        // 自動検査に引っかかった行は出力しない
        var safe = text.Split('\n').Where(l => !Redactor.ContainsSensitive(l)).ToList();
        safe.Add("(自動検査により一部の行を除外しました)");
        return string.Join('\n', safe);
    }
}
