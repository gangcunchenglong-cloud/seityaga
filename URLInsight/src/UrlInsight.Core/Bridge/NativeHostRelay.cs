using UrlInsight.Core.Diagnostics;

namespace UrlInsight.Core.Bridge;

/// <summary>
/// Chrome が起動するネイティブメッセージングホスト側の中継処理。
/// 標準入出力(ブラウザ) ⇔ 名前付きパイプ(常駐アプリ) を中継する。
/// 標準出力はプロトコル専用で、診断は <see cref="AppLog"/> にのみ書く。
/// アプリが起動していないときはアプリを勝手に起動せず、拡張へ status(appRunning=false) を返して待機・再接続する。
/// </summary>
public sealed class NativeHostRelay
{
    public static string PipeName(int sessionId) => $"URLInsight.Bridge.{sessionId}";

    private readonly SemaphoreSlim _stdoutLock = new(1, 1);
    private readonly object _appLock = new();
    private Stream? _app;
    private readonly SemaphoreSlim _appWriteLock = new(1, 1);

    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <returns>終了コード(0: ブラウザ側が接続を閉じた)</returns>
    public async Task<int> RunAsync(Stream stdin, Stream stdout, Func<CancellationToken, Task<Stream?>> connectApp, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var connectLoop = Task.Run(() => AppLoopAsync(stdout, connectApp, cts.Token), cts.Token);
        int exitCode = 0;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                byte[]? frame;
                try
                {
                    frame = await MessageFraming.ReadAsync(stdin, MessageFraming.MaxInboundBytes, cts.Token).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    AppLog.Warn("native host: oversized or invalid frame from browser; closing");
                    exitCode = 2;
                    break;
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                if (frame == null) break; // ブラウザがポートを閉じた

                if (!BrowserMessageParser.TryParse(frame, out var msg, out var error))
                {
                    AppLog.Warn($"native host: rejected message ({error})");
                    await WriteStdoutAsync(stdout, HostMessages.Error(error == "version" ? "unsupported_version" : "invalid_message"), cts.Token).ConfigureAwait(false);
                    continue;
                }
                if (msg!.Type is "app.activate" or "app.showManual")
                {
                    continue; // ブラウザからアプリ内部コマンドは受け付けない
                }
                if (msg.Type == "ping")
                {
                    await WriteStdoutAsync(stdout, HostMessages.Status(CurrentApp != null), cts.Token).ConfigureAwait(false);
                    continue;
                }

                var app = CurrentApp;
                if (app == null)
                {
                    await WriteStdoutAsync(stdout, HostMessages.Status(false), cts.Token).ConfigureAwait(false);
                    continue;
                }
                try
                {
                    await _appWriteLock.WaitAsync(cts.Token).ConfigureAwait(false);
                    try { await MessageFraming.WriteAsync(app, frame, cts.Token).ConfigureAwait(false); }
                    finally { _appWriteLock.Release(); }
                }
                catch (IOException)
                {
                    DropApp(app);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Cancel();
            try { await connectLoop.ConfigureAwait(false); } catch (OperationCanceledException) { }
            DropApp(CurrentApp);
        }
        return exitCode;
    }

    private Stream? CurrentApp
    {
        get { lock (_appLock) return _app; }
    }

    private void DropApp(Stream? app)
    {
        if (app == null) return;
        lock (_appLock)
        {
            if (ReferenceEquals(_app, app)) _app = null;
        }
        try { app.Dispose(); } catch (Exception) { }
    }

    private async Task AppLoopAsync(Stream stdout, Func<CancellationToken, Task<Stream?>> connectApp, CancellationToken ct)
    {
        bool? lastReported = null;
        while (!ct.IsCancellationRequested)
        {
            Stream? app = null;
            try { app = await connectApp(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { AppLog.Debug($"native host: connect failed {ex.GetType().Name}"); }

            if (app == null)
            {
                if (lastReported != false)
                {
                    await WriteStdoutAsync(stdout, HostMessages.Status(false), ct).ConfigureAwait(false);
                    lastReported = false;
                }
                try { await Task.Delay(ReconnectInterval, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }

            lock (_appLock) _app = app;
            await WriteStdoutAsync(stdout, HostMessages.Status(true), ct).ConfigureAwait(false);
            lastReported = true;
            AppLog.Info("native host: connected to app");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await MessageFraming.ReadAsync(app, MessageFraming.MaxOutboundBytes, ct).ConfigureAwait(false);
                    if (frame == null) break;
                    await WriteStdoutAsync(stdout, frame, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
            {
            }
            DropApp(app);
            AppLog.Info("native host: app disconnected");
        }
    }

    private async Task WriteStdoutAsync(Stream stdout, byte[] payload, CancellationToken ct)
    {
        await _stdoutLock.WaitAsync(ct).ConfigureAwait(false);
        try { await MessageFraming.WriteAsync(stdout, payload, ct).ConfigureAwait(false); }
        catch (IOException) { }
        finally { _stdoutLock.Release(); }
    }
}
