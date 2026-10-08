using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp.Html.Parser;
using UrlInsight.Core.Net;

namespace UrlInsight.Core.Content;

/// <summary>検索結果を取得する先(名前・URL・結果ページの読み取り方)。</summary>
public sealed record SearchSource(string Name, Uri Uri, Func<string, List<SearchResultItem>> Parse);

/// <summary>検索結果1件(カードの一覧表示とAIへの入力に使う)。</summary>
public sealed class SearchResultItem
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
}

/// <summary>
/// Google 検索の URL(https://www.google.com/search?q=…)の扱い。
/// Google の検索結果ページは JavaScript を実行しないと結果を返さない(「Google Search」という題名だけのページになる)ため、
/// URL から検索キーワードを取り出し、JavaScript なしで結果を返す検索サービス(DuckDuckGo の HTML 版、だめなら Bing)で
/// 同じキーワードを検索して、上位の結果(題名・URL・抜粋)を取得する。
/// </summary>
public static partial class SearchExtractor
{
    public const int MaxResults = 8;
    public const long MaxBytes = 3L * 1024 * 1024;

    [GeneratedRegex(@"^(www\.)?google\.(com|[a-z]{2}|com?\.[a-z]{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex GoogleHost();

    public static bool IsGoogleHost(Uri url) => GoogleHost().IsMatch(url.Host);

    /// <summary>Google 検索の URL なら、検索キーワードを返す。</summary>
    public static bool TryGetGoogleQuery(Uri url, out string query)
    {
        query = string.Empty;
        if (!IsGoogleHost(url) || !string.Equals(url.AbsolutePath, "/search", StringComparison.Ordinal)) return false;
        var q = HttpUtility.ParseQueryString(url.Query)["q"];
        query = TextUtil.SingleLine(q, 200);
        return query.Length > 0;
    }

    /// <summary>
    /// Google の転送用リンク(https://www.google.com/url?q=実際のURL)なら、実際の URL を返す。
    /// 転送用リンクのままでは Google の転送ページしか取得できないため。
    /// </summary>
    public static bool TryUnwrapGoogleRedirect(Uri url, out Uri target)
    {
        target = url;
        if (!IsGoogleHost(url) || !string.Equals(url.AbsolutePath, "/url", StringComparison.Ordinal)) return false;
        var qs = HttpUtility.ParseQueryString(url.Query);
        var value = qs["q"] ?? qs["url"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var t) || (t.Scheme != Uri.UriSchemeHttps && t.Scheme != Uri.UriSchemeHttp)) return false;
        target = t;
        return true;
    }

    public static Uri DuckDuckGoUrl(string query) => new("https://html.duckduckgo.com/html/?kl=jp-jp&q=" + Uri.EscapeDataString(query));
    public static Uri BingUrl(string query) => new("https://www.bing.com/search?setlang=ja&cc=JP&q=" + Uri.EscapeDataString(query));

    public static IReadOnlyList<SearchSource> DefaultSources(string query) => new[]
    {
        new SearchSource("DuckDuckGo", DuckDuckGoUrl(query), ParseDuckDuckGo),
        new SearchSource("Bing", BingUrl(query), ParseBing),
    };

    public static async Task<ExtractedContent> ExtractAsync(SafeHttpFetcher fetcher, Uri url, string query,
        IReadOnlyList<SearchSource> sources, CancellationToken ct)
    {
        List<SearchResultItem> results = new();
        string? source = null;
        foreach (var (name, uri, parse) in sources)
        {
            try
            {
                var res = await fetcher.FetchAsync(uri, new FetchOptions(MaxBytes, TimeSpan.FromSeconds(15), Accept: "text/html"), ct).ConfigureAwait(false);
                var html = HtmlExtractor.Decode(res.Body, res.Charset);
                results = await Task.Run(() => parse(html), ct).WaitAsync(ContentService.ParseTimeout, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InsightException or TimeoutException)
            {
                results = new();
            }
            if (results.Count > 0)
            {
                source = name;
                break;
            }
        }
        return Build(url, query, results, source);
    }

    internal static ExtractedContent Build(Uri url, string query, List<SearchResultItem> results, string? source)
    {
        var sb = new StringBuilder();
        sb.Append("検索キーワード: ").Append(query).Append("\n\n");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.Append(i + 1).Append(". ").Append(r.Title).Append('（').Append(r.Domain).Append("）\n");
            if (r.Snippet.Length > 0) sb.Append(r.Snippet).Append('\n');
            sb.Append('\n');
        }
        var content = new ExtractedContent
        {
            Kind = PageKind.Search,
            Url = url,
            Title = $"「{query}」の検索結果",
            Description = $"検索キーワード: {query}",
            SiteName = "Google 検索",
            Text = results.Count > 0 ? TextUtil.Normalize(sb.ToString()) : string.Empty,
            // 検索結果は1件ごとの抜粋が短いため、件数で根拠の量を判断する
            Quality = results.Count >= 3 ? SourceQuality.FullText : results.Count > 0 ? SourceQuality.Partial : SourceQuality.MetadataOnly,
        };
        content.SearchResults.AddRange(results);
        content.Notes.Add(source != null
            ? $"Google は JavaScript なしでは検索結果を返さないため、同じキーワードを {source} で検索した上位の結果です（Google の結果と順位や内容が異なる場合があります）"
            : "検索結果を取得できませんでした（検索キーワードのみ表示しています）");
        return content;
    }

    /// <summary>DuckDuckGo の HTML 版の結果(広告は除く)。</summary>
    internal static List<SearchResultItem> ParseDuckDuckGo(string html)
    {
        using var doc = new HtmlParser().ParseDocument(html);
        var list = new List<SearchResultItem>();
        foreach (var block in doc.QuerySelectorAll(".result"))
        {
            if (block.ClassList.Contains("result--ad")) continue;
            var a = block.QuerySelector("a.result__a");
            if (a == null) continue;
            var target = UnwrapDuckDuckGo(a.GetAttribute("href"));
            var snippet = block.QuerySelector(".result__snippet")?.TextContent;
            Add(list, a.TextContent, target, snippet);
            if (list.Count >= MaxResults) break;
        }
        return list;
    }

    /// <summary>Bing の検索結果(通常の結果のみ)。</summary>
    internal static List<SearchResultItem> ParseBing(string html)
    {
        using var doc = new HtmlParser().ParseDocument(html);
        var list = new List<SearchResultItem>();
        foreach (var block in doc.QuerySelectorAll("li.b_algo"))
        {
            var a = block.QuerySelector("h2 a");
            if (a == null) continue;
            var target = UnwrapBing(a.GetAttribute("href"));
            var snippet = block.QuerySelector(".b_caption p")?.TextContent ?? block.QuerySelector("p")?.TextContent;
            Add(list, a.TextContent, target, snippet);
            if (list.Count >= MaxResults) break;
        }
        return list;
    }

    private static void Add(List<SearchResultItem> list, string? title, string? url, string? snippet)
    {
        var t = TextUtil.SingleLine(title, 150);
        if (t.Length == 0 || url == null || !UrlPolicy.TryNormalize(url, out var u, out _)) return;
        if (list.Any(r => r.Url == u!.ToString())) return;
        list.Add(new SearchResultItem
        {
            Title = t,
            Url = u!.ToString(),
            Domain = UrlPolicy.DisplayDomain(u),
            Snippet = TextUtil.SingleLine(snippet, 300),
        });
    }

    /// <summary>DuckDuckGo の転送リンク(//duckduckgo.com/l/?uddg=実際のURL)を実際の URL に戻す。</summary>
    internal static string? UnwrapDuckDuckGo(string? href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
        if (Uri.TryCreate(href, UriKind.Absolute, out var u) && u.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath.StartsWith("/l/", StringComparison.Ordinal))
            return HttpUtility.ParseQueryString(u.Query)["uddg"];
        return href;
    }

    /// <summary>Bing の転送リンク(bing.com/ck/a?...&amp;u=a1&lt;Base64URL&gt;)を実際の URL に戻す。</summary>
    internal static string? UnwrapBing(string? href)
    {
        if (string.IsNullOrEmpty(href)) return null;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var u) || !u.Host.EndsWith("bing.com", StringComparison.OrdinalIgnoreCase)
            || !u.AbsolutePath.StartsWith("/ck/", StringComparison.Ordinal))
            return href;
        var encoded = HttpUtility.ParseQueryString(u.Query)["u"];
        if (encoded == null || !encoded.StartsWith("a1", StringComparison.Ordinal)) return null;
        try
        {
            var b64 = encoded[2..].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
