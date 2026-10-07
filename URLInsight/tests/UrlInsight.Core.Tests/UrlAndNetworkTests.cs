using System.Net;
using UrlInsight.Core.Net;

namespace UrlInsight.Core.Tests;

public class UrlPolicyTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("chrome://settings")]
    [InlineData("chrome-extension://abc/page.html")]
    [InlineData("ftp://example.com/x")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("not a url")]
    [InlineData("")]
    public void RejectsUnsupportedUrls(string url)
    {
        Assert.False(UrlPolicy.TryNormalize(url, out _, out var err));
        Assert.Equal(ErrorCode.UnsupportedUrl, err);
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://foo.localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://192.168.0.1/admin")]
    [InlineData("http://172.20.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://intranet/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://0.0.0.0/")]
    public void RejectsLocalTargets(string url)
    {
        Assert.False(UrlPolicy.TryNormalize(url, out _, out var err));
        Assert.Equal(ErrorCode.BlockedAddress, err);
    }

    [Fact]
    public void NormalizesHostFragmentPortAndTracking()
    {
        Assert.True(UrlPolicy.TryNormalize("HTTPS://Example.COM:443/a/b?utm_source=x&id=5&fbclid=abc#frag", out var u, out _));
        Assert.Equal("https://example.com/a/b?id=5", u!.ToString());
    }

    [Fact]
    public void KeepsDistinctQueryValuesSeparate()
    {
        Assert.True(UrlPolicy.TryNormalize("https://example.com/item?id=1", out var a, out _));
        Assert.True(UrlPolicy.TryNormalize("https://example.com/item?id=2", out var b, out _));
        Assert.NotEqual(a!.ToString(), b!.ToString());
    }

    [Fact]
    public void ConvertsUnicodeDomainToPunycode()
    {
        Assert.True(UrlPolicy.TryNormalize("https://日本語.jp/パス", out var u, out _));
        Assert.Equal("xn--wgv71a119e.jp", u!.Host);
    }

    [Fact]
    public void RedactsForLog()
    {
        Assert.True(UrlPolicy.TryNormalize("https://example.com/secret/path?token=abc", out var u, out _));
        Assert.Equal("https://example.com/…", UrlPolicy.RedactForLog(u));
    }
}

public class IpGuardTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700::6810:85e5", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2002:0a00:0001::1", false)]
    [InlineData("2001:db8::1", false)]
    public void ClassifiesAddresses(string ip, bool expected)
        => Assert.Equal(expected, IpGuard.IsPublic(IPAddress.Parse(ip)));
}

public class SafeHttpFetcherTests
{
    private static readonly FetchOptions Opts = new(1024 * 1024, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task DefaultModeRefusesLoopback()
    {
        using var server = new TestServer();
        server.MapText("/a", "<html>hi</html>");
        using var fetcher = new SafeHttpFetcher();
        var ex = await Assert.ThrowsAsync<InsightException>(() => fetcher.FetchAsync(server.Url("/a"), Opts, default));
        Assert.Equal(ErrorCode.BlockedAddress, ex.Code);
        Assert.Equal(0, server.Hits);
    }

    [Fact]
    public async Task ConnectTimeIpCheckBlocksPrivateAddressEvenIfUrlCheckIsBypassed()
    {
        // DNS rebinding 等で URL 段階の検査をすり抜けても、接続直前の IP 検査で止まることを確認
        using var server = new TestServer();
        server.MapText("/a", "<html>hi</html>");
        using var fetcher = new SafeHttpFetcher(allowPrivateAddressesForTesting: false, skipUrlPolicyForTesting: true);
        var ex = await Assert.ThrowsAsync<InsightException>(() => fetcher.FetchAsync(server.Url("/a"), Opts, default));
        Assert.Equal(ErrorCode.BlockedAddress, ex.Code);
        Assert.Equal(0, server.Hits);
    }

    [Fact]
    public async Task FollowsRedirectsUpToLimit()
    {
        using var server = new TestServer();
        server.MapRedirect("/r1", "/r2");
        server.MapRedirect("/r2", "/final");
        server.MapText("/final", "<html><title>ok</title></html>");
        using var fetcher = new SafeHttpFetcher(allowPrivateAddressesForTesting: true);
        var res = await fetcher.FetchAsync(server.Url("/r1"), Opts, default);
        Assert.Equal("/final", res.FinalUri.AbsolutePath);
        Assert.Equal("text/html", res.MediaType);
    }

    [Fact]
    public async Task StopsRedirectLoops()
    {
        using var server = new TestServer();
        server.MapRedirect("/loop", "/loop");
        using var fetcher = new SafeHttpFetcher(true);
        var ex = await Assert.ThrowsAsync<InsightException>(() => fetcher.FetchAsync(server.Url("/loop"), Opts, default));
        Assert.Equal(ErrorCode.TooManyRedirects, ex.Code);
    }

    [Fact]
    public async Task RejectsRedirectToNonHttpScheme()
    {
        using var server = new TestServer();
        server.MapRedirect("/r", "file:///etc/passwd");
        using var fetcher = new SafeHttpFetcher(true);
        var ex = await Assert.ThrowsAsync<InsightException>(() => fetcher.FetchAsync(server.Url("/r"), Opts, default));
        Assert.Equal(ErrorCode.UnsupportedUrl, ex.Code);
    }

    [Fact]
    public async Task EnforcesSizeLimit()
    {
        using var server = new TestServer();
        server.MapBytes("/big", new byte[300_000], "application/octet-stream");
        using var fetcher = new SafeHttpFetcher(true);
        var ex = await Assert.ThrowsAsync<InsightException>(() =>
            fetcher.FetchAsync(server.Url("/big"), new FetchOptions(100_000, TimeSpan.FromSeconds(5)), default));
        Assert.Equal(ErrorCode.TooLarge, ex.Code);
    }

    [Fact]
    public async Task TimesOut()
    {
        using var server = new TestServer();
        server.Map("/slow", async ctx => { await Task.Delay(3000); await TestServer.Write(ctx, new byte[1], "text/html", 200); });
        using var fetcher = new SafeHttpFetcher(true);
        var ex = await Assert.ThrowsAsync<InsightException>(() =>
            fetcher.FetchAsync(server.Url("/slow"), new FetchOptions(1000, TimeSpan.FromMilliseconds(400)), default));
        Assert.Equal(ErrorCode.Timeout, ex.Code);
    }

    [Theory]
    [InlineData(403, ErrorCode.AccessRestricted)]
    [InlineData(401, ErrorCode.AccessRestricted)]
    [InlineData(404, ErrorCode.NotFound)]
    [InlineData(503, ErrorCode.SiteError)]
    public async Task MapsStatusCodes(int status, ErrorCode expected)
    {
        using var server = new TestServer();
        server.MapText("/s", "x", status: status);
        using var fetcher = new SafeHttpFetcher(true);
        var ex = await Assert.ThrowsAsync<InsightException>(() => fetcher.FetchAsync(server.Url("/s"), Opts, default));
        Assert.Equal(expected, ex.Code);
    }

    [Fact]
    public async Task DoesNotSendCookiesOrAuthorization()
    {
        using var server = new TestServer();
        string? cookie = "unset", auth = "unset";
        server.Map("/h", async ctx =>
        {
            cookie = ctx.Request.Headers["Cookie"];
            auth = ctx.Request.Headers["Authorization"];
            await TestServer.Write(ctx, "<html></html>"u8.ToArray(), "text/html", 200);
        });
        using var fetcher = new SafeHttpFetcher(true);
        await fetcher.FetchAsync(server.Url("/h"), Opts, default);
        Assert.Null(cookie);
        Assert.Null(auth);
    }
}
