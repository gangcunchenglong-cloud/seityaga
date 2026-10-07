using System.IO.Pipes;
using System.Text;
using UrlInsight.Core.Bridge;
using UrlInsight.Core.Content;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "urlinsight-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

public class SummaryCacheTests
{
    private static SummaryCard Card(string url, string line = "要約") => new()
    {
        Url = url, Domain = "example.com", Kind = PageKind.Web, Title = "T", SummaryLines = { line },
    };

    [Fact]
    public void StoresAndExpires()
    {
        using var dir = new TempDir();
        var now = DateTimeOffset.UtcNow;
        var cache = new SummaryCache(System.IO.Path.Combine(dir.Path, "c.db"), () => now);
        var key = SummaryCache.LookupKey("https://example.com/a", "openai", "m1");
        cache.Put(key, Card("https://example.com/a"), "hash", "openai", "m1");
        Assert.NotNull(cache.TryGet(key, TimeSpan.FromDays(7)));
        now = now.AddDays(8);
        Assert.Null(cache.TryGet(key, TimeSpan.FromDays(7)));
        Assert.Equal(0, cache.Stats().Count);
    }

    [Fact]
    public void KeySeparatesUrlProviderAndModel()
    {
        var a = SummaryCache.LookupKey("https://example.com/item?id=1", "openai", "m1");
        Assert.NotEqual(a, SummaryCache.LookupKey("https://example.com/item?id=2", "openai", "m1"));
        Assert.NotEqual(a, SummaryCache.LookupKey("https://example.com/item?id=1", "openai", "m2"));
        Assert.NotEqual(a, SummaryCache.LookupKey("https://example.com/item?id=1", "anthropic", "m1"));
        Assert.NotEqual(a, SummaryCache.LookupKey("https://example.com/item?id=1", "openai", "m1", "en"));
    }

    [Fact]
    public void PrunesLeastRecentlyUsed()
    {
        using var dir = new TempDir();
        var now = DateTimeOffset.UtcNow;
        var cache = new SummaryCache(System.IO.Path.Combine(dir.Path, "c.db"), () => now);
        for (int i = 0; i < 5; i++)
        {
            now = now.AddSeconds(1);
            cache.Put("k" + i, Card("https://e.com/" + i), "h", "p", "m");
        }
        now = now.AddSeconds(1);
        Assert.NotNull(cache.TryGet("k0", TimeSpan.FromDays(1))); // k0 を最近使った扱いに
        cache.Prune(TimeSpan.FromDays(1), maxEntries: 3, maxBytes: long.MaxValue);
        Assert.Equal(3, cache.Stats().Count);
        Assert.NotNull(cache.TryGet("k0", TimeSpan.FromDays(1)));
        Assert.Null(cache.TryGet("k1", TimeSpan.FromDays(1)));
    }

    [Fact]
    public void RecentDeleteAndClear()
    {
        using var dir = new TempDir();
        var cache = new SummaryCache(System.IO.Path.Combine(dir.Path, "c.db"));
        cache.Put("a", Card("https://e.com/a"), "h", "p", "m");
        cache.Put("b", Card("https://e.com/b"), "h", "p", "m");
        var recent = cache.Recent(10);
        Assert.Equal(2, recent.Count);
        cache.Delete(recent[0].Id);
        Assert.Single(cache.Recent(10));
        cache.Clear();
        Assert.Empty(cache.Recent(10));
    }

    [Fact]
    public void DatabaseDoesNotContainBodyOrKey()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "c.db");
        var cache = new SummaryCache(path);
        cache.Put("a", Card("https://e.com/a", "短い要約"), "hash", "openai", "m");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var raw = File.ReadAllBytes(path);
        Assert.DoesNotContain("sk-", Encoding.UTF8.GetString(raw));
    }
}

public class SettingsAndSecretsTests
{
    [Fact]
    public void SettingsRoundTripAndClamp()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(System.IO.Path.Combine(dir.Path, "s.json"));
        var s = store.Load();
        s.HoverDelayMs = 99999;
        s.ProviderId = "openai";
        store.Save(s);
        var loaded = store.Load();
        Assert.Equal(1500, loaded.HoverDelayMs);
        Assert.Equal("openai", loaded.ProviderId);
        Assert.DoesNotContain("apiKey", File.ReadAllText(System.IO.Path.Combine(dir.Path, "s.json")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrokenSettingsFallBackToDefaults()
    {
        using var dir = new TempDir();
        var path = System.IO.Path.Combine(dir.Path, "s.json");
        File.WriteAllText(path, "{ not json");
        var s = new SettingsStore(path).Load();
        Assert.Equal(600, s.HoverDelayMs);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void DpapiStoreNeverFallsBackToPlaintext()
    {
        using var dir = new TempDir();
        var store = new DpapiSecretStore(dir.Path);
        const string secret = "sk-plaintext-should-never-appear-123";
        if (OperatingSystem.IsWindows())
        {
            store.Save("provider-openai", secret);
            var bytes = File.ReadAllBytes(System.IO.Path.Combine(dir.Path, "provider-openai.bin"));
            Assert.DoesNotContain(secret, Encoding.UTF8.GetString(bytes));
            Assert.Equal(secret, store.TryGet("provider-openai"));
            store.Delete("provider-openai");
            Assert.False(store.Exists("provider-openai"));
        }
        else
        {
            var ex = Assert.Throws<InsightException>(() => store.Save("provider-openai", secret));
            Assert.Equal(ErrorCode.KeyStorageFailed, ex.Code);
            Assert.Empty(Directory.GetFiles(dir.Path));
        }
    }

    [Fact]
    public void CorruptedSecretReadsAsMissing()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(System.IO.Path.Combine(dir.Path, "provider-x.bin"), Encoding.UTF8.GetBytes("sk-not-encrypted"));
        Assert.Null(new DpapiSecretStore(dir.Path).TryGet("provider-x"));
    }

    [Fact]
    public void RejectsPathTraversalNames()
        => Assert.Throws<ArgumentException>(() => new DpapiSecretStore("/tmp").TryGet("../evil"));

    [Fact]
    public void LayeredStoreUsesSessionWhenPersistentFails()
    {
        using var dir = new TempDir();
        var layered = new LayeredSecretStore(new DpapiSecretStore(dir.Path), new SessionSecretStore());
        layered.Session.Save("provider-a", "k1");
        Assert.Equal("k1", layered.TryGet("provider-a"));
        layered.Delete("provider-a");
        Assert.Null(layered.TryGet("provider-a"));
    }
}

public class RedactorTests
{
    [Theory]
    [InlineData("fetch https://example.com/private/path?token=abc done", "https://example.com/…")]
    [InlineData("key sk-abcdefghijklmnopqrstuv used", "***")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Bearer ***")]
    [InlineData("x-api-key: sk-ant-api03-abcdefgh", "x-api-key: ***")]
    [InlineData("https://www.googleapis.com/youtube/v3/videos?id=1&key=AIzaSyA1234567890abcdefghij", "https://www.googleapis.com/…")]
    public void RedactsSecretsAndUrls(string input, string expectedFragment)
    {
        var r = Redactor.Redact(input);
        Assert.Contains(expectedFragment, r);
        Assert.False(Redactor.ContainsSensitive(r), r);
    }

    [Fact]
    public void DetectsSensitive()
    {
        Assert.True(Redactor.ContainsSensitive("see https://e.com/a/b"));
        Assert.True(Redactor.ContainsSensitive("sk-abcdefghijklmnopqrstuv"));
        Assert.False(Redactor.ContainsSensitive("[abc12345] summarized kind=Web 120ms"));
    }

    [Fact]
    public void DiagnosticsReportContainsNoUrlsOrKeys()
    {
        var settings = new AppSettings { ProviderId = "openai", Model = "secret-model", IgnoredUrls = { "https://example.com/private/page" } };
        var report = DiagnosticsReport.Build("1.0.0", settings, new CacheStats(3, 2048), true, true,
            new[] { "2026 [Info] fetched https://example.com/private?x=1", "key sk-abcdefghijklmnopqrstuv" });
        Assert.False(Redactor.ContainsSensitive(report));
        Assert.DoesNotContain("private", report);
        Assert.DoesNotContain("secret-model", report);
        Assert.Contains("openai", report);
    }

    [Fact]
    public void LogFileNeverContainsSecrets()
    {
        using var dir = new TempDir();
        AppLog.Initialize(dir.Path, LogLevel.Debug);
        AppLog.Error("failed for https://example.com/secret?q=1 with sk-abcdefghijklmnopqrstuv");
        var text = string.Join("\n", Directory.GetFiles(dir.Path).Select(File.ReadAllText));
        Assert.DoesNotContain("secret?q=1", text);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuv", text);
        Assert.Contains("https://example.com/…", text);
    }
}

public class BridgeTests
{
    private static byte[] J(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void ParsesValidHover()
    {
        Assert.True(BrowserMessageParser.TryParse(J("""{"schemaVersion":1,"type":"hoverLink","requestId":"abcd1234-0001","url":"https://e.com/","pageTitle":"P","linkText":"L","browser":"chrome","tabId":"t-1"}"""), out var m, out _));
        Assert.Equal("hoverLink", m!.Type);
        Assert.Equal("https://e.com/", m.Url);
        Assert.Equal("L", m.LinkText);
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"type":"hoverLink","requestId":"abcd1234","url":"https://e.com/"}""", "version")]
    [InlineData("""{"schemaVersion":1,"type":"exec","requestId":"abcd1234"}""", "type")]
    [InlineData("""{"schemaVersion":1,"type":"hoverLink","requestId":"bad id!","url":"https://e.com/"}""", "requestId")]
    [InlineData("""{"schemaVersion":1,"type":"hoverLink","requestId":"abcd1234"}""", "url")]
    [InlineData("""not json""", "json")]
    [InlineData("""[1]""", "shape")]
    public void RejectsInvalid(string json, string expectedError)
    {
        Assert.False(BrowserMessageParser.TryParse(J(json), out _, out var err));
        Assert.Equal(expectedError, err);
    }

    [Fact]
    public void RejectsOversized()
    {
        var big = new byte[MessageFraming.MaxInboundBytes + 1];
        Assert.False(BrowserMessageParser.TryParse(big, out _, out var err));
        Assert.Equal("size", err);
    }

    [Fact]
    public async Task FramingRoundTripAndSizeLimit()
    {
        var ms = new MemoryStream();
        await MessageFraming.WriteAsync(ms, J("{\"a\":1}"), default);
        ms.Position = 0;
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString((await MessageFraming.ReadAsync(ms, 100, default))!));
        Assert.Null(await MessageFraming.ReadAsync(ms, 100, default));

        var bad = new MemoryStream(new byte[] { 0xFF, 0xFF, 0x00, 0x00 });
        await Assert.ThrowsAsync<InvalidDataException>(() => MessageFraming.ReadAsync(bad, 1000, default));
    }

    [Fact]
    public async Task RelayForwardsBothWaysAndReportsStatus()
    {
        // ブラウザ側(stdin/stdout)とアプリ側(パイプ)をそれぞれ匿名パイプで模擬する
        var browserToHost = new AnonymousPipeServerStream(PipeDirection.Out);
        var hostStdin = new AnonymousPipeClientStream(PipeDirection.In, browserToHost.ClientSafePipeHandle);
        var hostToBrowser = new AnonymousPipeServerStream(PipeDirection.In);
        var hostStdout = new AnonymousPipeClientStream(PipeDirection.Out, hostToBrowser.ClientSafePipeHandle);

        var appSide = new DuplexPair();
        bool appAvailable = false;
        var relay = new NativeHostRelay { ReconnectInterval = TimeSpan.FromMilliseconds(100) };
        var run = relay.RunAsync(hostStdin, hostStdout,
            _ => Task.FromResult<Stream?>(appAvailable ? appSide.HostEnd : null), default);

        // 1) アプリ未起動 → status(false)
        var first = Encoding.UTF8.GetString((await MessageFraming.ReadAsync(hostToBrowser, 1 << 20, default))!);
        Assert.Contains("\"appRunning\":false", first);

        // 2) アプリ起動 → status(true)
        appAvailable = true;
        var second = Encoding.UTF8.GetString((await MessageFraming.ReadAsync(hostToBrowser, 1 << 20, default))!);
        Assert.Contains("\"appRunning\":true", second);
        var hello = Encoding.UTF8.GetString((await MessageFraming.ReadAsync(appSide.AppEnd, 1 << 20, default))!);
        Assert.Contains("\"type\":\"hello\"", hello);

        // 3) ブラウザ → アプリ
        await MessageFraming.WriteAsync(browserToHost, J("""{"schemaVersion":1,"type":"hoverLink","requestId":"abcd1234","url":"https://e.com/"}"""), default);
        var atApp = Encoding.UTF8.GetString((await MessageFraming.ReadAsync(appSide.AppEnd, 1 << 20, default))!);
        Assert.Contains("hoverLink", atApp);

        // 4) 不正メッセージはアプリへ渡さずエラー応答
        await MessageFraming.WriteAsync(browserToHost, J("""{"schemaVersion":9,"type":"hoverLink"}"""), default);
        var err = Encoding.UTF8.GetString((await MessageFraming.ReadAsync(hostToBrowser, 1 << 20, default))!);
        Assert.Contains("unsupported_version", err);

        // 5) アプリ → ブラウザ
        await MessageFraming.WriteAsync(appSide.AppEnd, HostMessages.Config(600, false, "1.0.0"), default);
        var cfg = Encoding.UTF8.GetString((await MessageFraming.ReadAsync(hostToBrowser, 1 << 20, default))!);
        Assert.Contains("\"type\":\"config\"", cfg);

        // 6) ブラウザがポートを閉じたらホストは終了
        browserToHost.Dispose();
        var exit = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, exit);
    }

    /// <summary>双方向ストリームのペア(テスト用)。</summary>
    private sealed class DuplexPair
    {
        public DuplexPair()
        {
            var a = new System.IO.Pipelines.Pipe();
            var b = new System.IO.Pipelines.Pipe();
            HostEnd = new DuplexStream(a.Reader.AsStream(), b.Writer.AsStream());
            AppEnd = new DuplexStream(b.Reader.AsStream(), a.Writer.AsStream());
        }
        public Stream HostEnd { get; }
        public Stream AppEnd { get; }
    }

    private sealed class DuplexStream : Stream
    {
        private readonly Stream _read, _write;
        public DuplexStream(Stream read, Stream write) { _read = read; _write = write; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _write.Flush();
        public override Task FlushAsync(CancellationToken ct) => _write.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => _read.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _read.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => _write.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _write.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
