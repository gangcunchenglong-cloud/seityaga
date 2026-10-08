using System.Text;
using UrlInsight.Core.Net;

namespace UrlInsight.Core.Content;

/// <summary>URL の種類を判定し、取得・本文抽出を行う。</summary>
public sealed class ContentService
{
    public const long MaxDownloadBytes = 20L * 1024 * 1024;
    private readonly SafeHttpFetcher _fetcher;
    private readonly Func<string?> _youTubeApiKey;

    public ContentService(SafeHttpFetcher fetcher, Func<string?>? youTubeApiKey = null)
    {
        _fetcher = fetcher;
        _youTubeApiKey = youTubeApiKey ?? (() => null);
    }

    public async Task<ExtractedContent> ExtractAsync(Uri url, CancellationToken ct)
    {
        if (SearchExtractor.TryGetGoogleQuery(url, out var query))
            return await SearchExtractor.ExtractAsync(_fetcher, url, query,
                (SearchSourcesForTesting ?? SearchExtractor.DefaultSources)(query), ct).ConfigureAwait(false);

        if (YouTubeExtractor.TryGetVideoId(url, out var videoId))
            return await YouTubeExtractor.ExtractAsync(_fetcher, url, videoId, _youTubeApiKey(), ct).ConfigureAwait(false);

        var res = await _fetcher.FetchAsync(url, new FetchOptions(MaxDownloadBytes, TimeSpan.FromSeconds(20)), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // リダイレクト先が YouTube だった場合
        if (YouTubeExtractor.TryGetVideoId(res.FinalUri, out var redirectedId))
            return await YouTubeExtractor.ExtractAsync(_fetcher, res.FinalUri, redirectedId, _youTubeApiKey(), ct).ConfigureAwait(false);

        // 解析処理(HTML/PDF)は CPU 処理のため別スレッドで実行し、上限時間を設ける
        var parse = Task.Run(() => Classify(res), CancellationToken.None);
        try
        {
            return await parse.WaitAsync(ParseTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InsightException(ErrorCode.Timeout, "content parsing took too long");
        }
    }

    /// <summary>テスト用: 検索結果の取得先をローカルのテストサーバーに差し替える。</summary>
    internal Func<string, IReadOnlyList<SearchSource>>? SearchSourcesForTesting { get; init; }

    public static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(15);

    internal static ExtractedContent Classify(FetchResult res)
    {
        var type = res.MediaType ?? string.Empty;
        if (type == "application/pdf" || PdfExtractor.LooksLikePdf(res.Body))
            return PdfExtractor.Extract(res.Body, res.FinalUri);

        bool html = type is "text/html" or "application/xhtml+xml" ||
                    (type.Length == 0 && LooksLikeHtml(res.Body));
        if (html)
        {
            var text = HtmlExtractor.Decode(res.Body, res.Charset);
            return HtmlExtractor.Extract(text, res.FinalUri);
        }

        if (type == "text/plain")
        {
            var text = TextUtil.Normalize(HtmlExtractor.Decode(res.Body, res.Charset));
            var (limited, truncated) = TextUtil.LimitBody(text);
            return new ExtractedContent
            {
                Kind = PageKind.Text,
                Url = res.FinalUri,
                Title = PdfExtractor.FileNameFromUrl(res.FinalUri),
                Text = limited,
                Truncated = truncated,
                Quality = ExtractedContent.QualityFor(limited.Length),
            };
        }

        throw new InsightException(ErrorCode.UnsupportedContent, "content-type " + (type.Length == 0 ? "unknown" : type));
    }

    private static bool LooksLikeHtml(byte[] body)
    {
        var head = Encoding.ASCII.GetString(body, 0, Math.Min(body.Length, 1024)).TrimStart().ToLowerInvariant();
        return head.StartsWith("<!doctype html") || head.StartsWith("<html") || head.Contains("<head");
    }
}
