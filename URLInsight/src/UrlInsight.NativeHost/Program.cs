using System.Diagnostics;
using System.IO.Pipes;
using UrlInsight.Core.Bridge;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Storage;

namespace UrlInsight.NativeHost;

/// <summary>
/// Chrome は「ホスト実行ファイル chrome-extension://&lt;拡張ID&gt;/」の形で起動する。
/// 許可された拡張IDはホストマニフェストの allowed_origins で Chrome 自身が強制する。
/// 標準出力はプロトコル専用。診断はログファイルにのみ出す。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var paths = new AppPaths();
        try
        {
            paths.EnsureCreated();
            AppLog.Initialize(paths.LogsDir, LogLevel.Info, "urlinsight-host");
        }
        catch (Exception)
        {
            // ログが書けなくても中継は続ける
        }

        var origin = args.FirstOrDefault(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal));
        if (origin == null)
        {
            Console.Error.WriteLine("This program is started by the browser (Native Messaging host for URL Insight).");
            return 1;
        }
        AppLog.Info($"native host started for {origin}");

        int sessionId = Process.GetCurrentProcess().SessionId;
        var pipeName = NativeHostRelay.PipeName(sessionId);
        await using var stdin = Console.OpenStandardInput();
        await using var stdout = Console.OpenStandardOutput();

        var relay = new NativeHostRelay();
        try
        {
            return await relay.RunAsync(stdin, stdout, async ct =>
            {
                var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await client.ConnectAsync(1000, ct).ConfigureAwait(false);
                    return client;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    return null;
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("native host crashed", ex);
            return 3;
        }
        finally
        {
            AppLog.Info("native host exited");
        }
    }
}
