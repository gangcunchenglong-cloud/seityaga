using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UrlInsight.Core.Tests;

/// <summary>テスト用のローカル HTTP サーバー(127.0.0.1 のみ)。</summary>
public sealed class TestServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, Func<HttpListenerContext, Task>> _routes = new();
    private readonly CancellationTokenSource _cts = new();

    public TestServer()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public int Port { get; }
    public Uri Url(string path) => new($"http://127.0.0.1:{Port}{path}");
    public int Hits { get; private set; }

    public void Map(string path, Func<HttpListenerContext, Task> handler) => _routes[path] = handler;

    public void MapText(string path, string body, string contentType = "text/html; charset=utf-8", int status = 200)
        => Map(path, ctx => Write(ctx, Encoding.UTF8.GetBytes(body), contentType, status));

    public void MapBytes(string path, byte[] body, string contentType)
        => Map(path, ctx => Write(ctx, body, contentType, 200));

    public void MapRedirect(string path, string location)
        => Map(path, ctx =>
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = location;
            ctx.Response.Close();
            return Task.CompletedTask;
        });

    public static async Task Write(HttpListenerContext ctx, byte[] body, string contentType, int status)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = body.Length;
        await ctx.Response.OutputStream.WriteAsync(body);
        ctx.Response.Close();
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            Hits++;
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_routes.TryGetValue(ctx.Request.Url!.AbsolutePath, out var h)) await h(ctx);
                    else { ctx.Response.StatusCode = 404; ctx.Response.Close(); }
                }
                catch (Exception)
                {
                    try { ctx.Response.Abort(); } catch (Exception) { }
                }
            });
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
    }
}
