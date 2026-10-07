using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;

namespace UrlInsight.Core.Net;

public sealed record FetchOptions(long MaxBytes, TimeSpan Timeout, int MaxRedirects = 5, string? Accept = null);

public sealed class FetchResult
{
    public required Uri FinalUri { get; init; }
    public required int StatusCode { get; init; }
    public string? MediaType { get; init; }
    public string? Charset { get; init; }
    public required byte[] Body { get; init; }
}

/// <summary>
/// ページ取得専用の HTTP クライアント。
/// - 匿名取得(Cookie・認証情報・プロキシ資格情報を一切付与しない)
/// - DNS 解決後の IP を検査してから、その IP へ直接接続(DNS rebinding 対策)
/// - リダイレクトは自前で最大5回まで追跡し、毎回 URL と接続先を再検査
/// - 本文サイズ(展開後)・時間を制限
/// </summary>
public sealed class SafeHttpFetcher : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _allowPrivateForTesting;
    private readonly bool _skipUrlPolicyForTesting;

    public const string BrowserLikeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) URLInsight/1.0 Safari/537.36";

    public SafeHttpFetcher(bool allowPrivateAddressesForTesting = false)
        : this(allowPrivateAddressesForTesting, skipUrlPolicyForTesting: false)
    {
    }

    /// <summary>テスト用: URL 段階の検査を省き、接続時(DNS 解決後)の IP 検査だけを検証できるようにする。</summary>
    internal SafeHttpFetcher(bool allowPrivateAddressesForTesting, bool skipUrlPolicyForTesting)
    {
        _allowPrivateForTesting = allowPrivateAddressesForTesting;
        _skipUrlPolicyForTesting = skipUrlPolicyForTesting;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false, // 接続先IP検証を確実にするため、ページ取得ではプロキシを使わない
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 64,
            ConnectCallback = ConnectAsync,
        };
        _client = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserLikeUserAgent);
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja,en;q=0.8");
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var host = ctx.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        var allowed = addresses.Where(a => _allowPrivateForTesting || IpGuard.IsPublic(a)).ToArray();
        if (allowed.Length == 0)
            throw new InsightException(ErrorCode.BlockedAddress, "resolved address is not public");

        Exception? last = null;
        foreach (var ip in allowed)
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                socket.Dispose();
                last = ex;
            }
        }
        throw new HttpRequestException("connect failed", last);
    }

    private Uri CheckTarget(Uri uri)
    {
        if (_allowPrivateForTesting || _skipUrlPolicyForTesting)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                throw new InsightException(ErrorCode.UnsupportedUrl);
            return uri;
        }
        if (!UrlPolicy.TryNormalize(uri.OriginalString, out var normalized, out var error))
            throw new InsightException(error == ErrorCode.None ? ErrorCode.UnsupportedUrl : error);
        return normalized!;
    }

    public async Task<FetchResult> FetchAsync(Uri uri, FetchOptions options, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Timeout);
        var current = CheckTarget(uri);

        for (int redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.ParseAdd(options.Accept ?? "text/html,application/xhtml+xml,application/pdf;q=0.9,*/*;q=0.5");

            HttpResponseMessage response;
            try
            {
                response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InsightException(ErrorCode.Timeout);
            }
            catch (HttpRequestException ex)
            {
                throw MapException(ex);
            }

            using (response)
            {
                int status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location != null)
                {
                    if (redirects >= options.MaxRedirects) throw new InsightException(ErrorCode.TooManyRedirects);
                    var location = response.Headers.Location;
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    current = CheckTarget(next);
                    continue;
                }

                if (status is 401 or 403 or 407 or 451) throw new InsightException(ErrorCode.AccessRestricted, $"http {status}");
                if (status is 404 or 410) throw new InsightException(ErrorCode.NotFound, $"http {status}");
                if (status == 429) throw new InsightException(ErrorCode.AccessRestricted, "http 429 (site rate limit)");
                if (status >= 500) throw new InsightException(ErrorCode.SiteError, $"http {status}");
                if (status < 200 || status >= 300) throw new InsightException(ErrorCode.SiteError, $"http {status}");

                if (response.Content.Headers.ContentLength is long declared && declared > options.MaxBytes)
                    throw new InsightException(ErrorCode.TooLarge);

                byte[] body;
                try
                {
                    body = await ReadCappedAsync(response.Content, options.MaxBytes, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new InsightException(ErrorCode.Timeout);
                }
                catch (HttpRequestException ex)
                {
                    throw MapException(ex);
                }
                catch (IOException ex)
                {
                    throw new InsightException(ErrorCode.NetworkError, "read failed", ex);
                }

                MediaTypeHeaderValue? ctype = response.Content.Headers.ContentType;
                return new FetchResult
                {
                    FinalUri = current,
                    StatusCode = status,
                    MediaType = ctype?.MediaType?.ToLowerInvariant(),
                    Charset = ctype?.CharSet?.Trim('"'),
                    Body = body,
                };
            }
        }
    }

    private static async Task<byte[]> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            int n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) break;
            if (ms.Length + n > maxBytes) throw new InsightException(ErrorCode.TooLarge);
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private static InsightException MapException(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is InsightException ie) return ie;
            if (e is AuthenticationException) return new InsightException(ErrorCode.NetworkError, "tls failure", ex);
            if (e is SocketException se && se.SocketErrorCode == SocketError.HostNotFound)
                return new InsightException(ErrorCode.NetworkError, "dns failure", ex);
        }
        return new InsightException(ErrorCode.NetworkError, "network failure", ex);
    }

    public void Dispose() => _client.Dispose();
}
