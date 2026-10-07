using System.Globalization;
using System.Text;

namespace UrlInsight.Core.Diagnostics;

public enum LogLevel { Debug = 0, Info = 1, Warning = 2, Error = 3 }

/// <summary>
/// ローテーション付きファイルロガー。書き込む前に必ず <see cref="Redactor"/> を通す。
/// 記録してよいのは時刻・イベントID・ページ種別・処理段階・所要時間・エラーコード等で、
/// 本文・キー・完全な URL・プロンプトは呼び出し側でも渡さない方針。
/// 保持: 既定 7 日、合計 10MB を超えたら古いファイルから削除。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _dir;
    private static LogLevel _level = LogLevel.Info;
    public const long MaxTotalBytes = 10L * 1024 * 1024;
    public const int RetentionDays = 7;

    public static string? Directory => _dir;

    public static void Initialize(string directory, LogLevel level)
    {
        lock (Gate)
        {
            _dir = directory;
            _level = level;
            System.IO.Directory.CreateDirectory(directory);
            Cleanup();
        }
    }

    public static void SetLevel(LogLevel level) => _level = level;

    public static LogLevel ParseLevel(string? s) => s switch
    {
        "Debug" => LogLevel.Debug,
        "Warning" => LogLevel.Warning,
        "Error" => LogLevel.Error,
        _ => LogLevel.Info,
    };

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null)
        => Write(LogLevel.Error, ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    public static void Write(LogLevel level, string message)
    {
        if (level < _level || _dir == null) return;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {Redactor.Redact(message)}{Environment.NewLine}");
        lock (Gate)
        {
            try
            {
                var file = Path.Combine(_dir, $"urlinsight-{DateTime.Now:yyyyMMdd}.log");
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxTotalBytes / 2) return; // 1日分の上限
                File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Cleanup()
    {
        if (_dir == null) return;
        try
        {
            var files = new DirectoryInfo(_dir).GetFiles("urlinsight-*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            long total = 0;
            foreach (var f in files)
            {
                total += f.Length;
                if (f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-RetentionDays) || total > MaxTotalBytes)
                    f.Delete();
            }
        }
        catch (IOException)
        {
        }
    }

    public static IReadOnlyList<string> Tail(int maxLines)
    {
        if (_dir == null) return Array.Empty<string>();
        lock (Gate)
        {
            var lines = new List<string>();
            foreach (var f in new DirectoryInfo(_dir).GetFiles("urlinsight-*.log").OrderByDescending(f => f.Name).Take(2).Reverse())
            {
                try { lines.AddRange(File.ReadAllLines(f.FullName)); } catch (IOException) { }
            }
            return lines.Skip(Math.Max(0, lines.Count - maxLines)).ToList();
        }
    }
}
