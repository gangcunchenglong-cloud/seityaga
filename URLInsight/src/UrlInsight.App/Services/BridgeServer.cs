using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UrlInsight.Core.Bridge;
using UrlInsight.Core.Diagnostics;

namespace UrlInsight.App.Services;

/// <summary>
/// ネイティブホスト(および2重起動時の2つ目のプロセス)からの接続を受ける名前付きパイプサーバー。
/// PipeOptions.CurrentUserOnly により、同じWindowsユーザーのプロセスだけが接続できる。
/// ネットワークポートは開かない。
/// </summary>
internal sealed class BridgeServer : IDisposable
{
    private sealed class Connection
    {
        public required NamedPipeServerStream Stream { get; init; }
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public bool IsBrowser { get; set; }
        public string? ExtensionVersion { get; set; }
    }

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Connection> _connections = new();
    private readonly Func<byte[]> _configMessage;

    public BridgeServer(Func<byte[]> configMessage) => _configMessage = configMessage;

    public static string PipeName => NativeHostRelay.PipeName(Process.GetCurrentProcess().SessionId);

    /// <summary>検証済みメッセージ(スレッドプールから呼ばれる)</summary>
    public event Action<BrowserMessage>? MessageReceived;
    public event Action? ConnectionsChanged;

    public bool BrowserConnected
    {
        get { lock (_connections) return _connections.Any(c => c.IsBrowser); }
    }

    public string? ExtensionVersion
    {
        get { lock (_connections) return _connections.Where(c => c.IsBrowser).Select(c => c.ExtensionVersion).FirstOrDefault(v => v != null); }
    }

    public void Start() => _ = Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("bridge: cannot create pipe", ex);
                try { await Task.Delay(2000, _cts.Token); } catch (OperationCanceledException) { return; }
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }
            catch (IOException)
            {
                await server.DisposeAsync();
                continue;
            }
            _ = Task.Run(() => HandleAsync(server));
        }
    }

    private async Task HandleAsync(NamedPipeServerStream stream)
    {
        var conn = new Connection { Stream = stream };
        lock (_connections) _connections.Add(conn);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var frame = await MessageFraming.ReadAsync(stream, MessageFraming.MaxInboundBytes, _cts.Token).ConfigureAwait(false);
                if (frame == null) break;
                if (!BrowserMessageParser.TryParse(frame, out var msg, out var error))
                {
                    AppLog.Warn($"bridge: rejected message ({error})");
                    continue;
                }
                if (msg!.Type == "hello" || msg.Type.StartsWith("hover", StringComparison.Ordinal))
                {
                    bool changed = !conn.IsBrowser;
                    conn.IsBrowser = true;
                    if (msg.ExtensionVersion != null) conn.ExtensionVersion = msg.ExtensionVersion;
                    if (changed)
                    {
                        AppLog.Info("bridge: browser connected");
                        ConnectionsChanged?.Invoke();
                        await SendAsync(conn, _configMessage()).ConfigureAwait(false);
                    }
                }
                MessageReceived?.Invoke(msg);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            bool wasBrowser;
            lock (_connections)
            {
                _connections.Remove(conn);
                wasBrowser = conn.IsBrowser;
            }
            try { await stream.DisposeAsync(); } catch (Exception) { }
            if (wasBrowser)
            {
                AppLog.Info("bridge: browser disconnected");
                ConnectionsChanged?.Invoke();
            }
        }
    }

    private static async Task SendAsync(Connection conn, byte[] payload)
    {
        try
        {
            await conn.WriteLock.WaitAsync().ConfigureAwait(false);
            try { await MessageFraming.WriteAsync(conn.Stream, payload, CancellationToken.None).ConfigureAwait(false); }
            finally { conn.WriteLock.Release(); }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    public void Broadcast(byte[] payload)
    {
        List<Connection> targets;
        lock (_connections) targets = _connections.Where(c => c.IsBrowser).ToList();
        foreach (var c in targets) _ = SendAsync(c, payload);
    }

    /// <summary>既に起動しているインスタンスへコマンドを送る(2重起動時)。</summary>
    public static bool TrySendToRunningInstance(string type)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            MessageFraming.WriteAsync(client, HostMessages.Simple(type), CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_connections)
        {
            foreach (var c in _connections)
            {
                try { c.Stream.Dispose(); } catch (Exception) { }
            }
            _connections.Clear();
        }
    }
}
