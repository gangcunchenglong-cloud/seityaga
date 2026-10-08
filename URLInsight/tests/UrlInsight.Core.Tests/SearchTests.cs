using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Net;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Tests;

/// <summary>Google 検索の URL にカーソルを重ねたときに、検索キーワードと検索結果を出すためのテスト。</summary>
public class SearchTests
{
    [Theory]
    [InlineData("https://www.google.com/search?q=%E6%9D%B1%E4%BA%AC+%E5%A4%A9%E6%B0%97&hl=ja", "東京 天気")]
    [InlineData("https://www.google.co.jp/search?q=URL+Insight&oq=x", "URL Insight")]
    [InlineData("https://google.com/search?client=firefox&q=abc", "abc")]
    [InlineData("https://www.google.com.au/search?q=test", "test")]
    public void DetectsGoogleSearchQuery(string url, string expected)
    {
        Assert.True(SearchExtractor.TryGetGoogleQuery(new Uri(url), out var q));
        Assert.Equal(expected, q);
    }

    [Theory]
    [InlineData("https://www.google.com/maps?q=tokyo")]
    [InlineData("https://www.google.com/search")]
    [InlineData("https://www.google.com/search?q=")]
    [InlineData("https://notgoogle.com/search?q=x")]
    [InlineData("https://www.google.evil.example/search?q=x")]
    public void IgnoresNonSearchUrls(string url) => Assert.False(SearchExtractor.TryGetGoogleQuery(new Uri(url), out _));

    [Fact]
    public void UnwrapsGoogleRedirectLinks()
    {
        Assert.True(SearchExtractor.TryUnwrapGoogleRedirect(
            new Uri("https://www.google.com/url?sa=t&q=https%3A%2F%2Fexample.com%2Fnews%3Fid%3D1&usg=x"), out var t));
        Assert.Equal("https://example.com/news?id=1", t.ToString());
        Assert.True(SearchExtractor.TryUnwrapGoogleRedirect(new Uri("https://www.google.co.jp/url?url=https://example.org/a"), out var t2));
        Assert.Equal("https://example.org/a", t2.ToString());
        Assert.False(SearchExtractor.TryUnwrapGoogleRedirect(new Uri("https://www.google.com/url?q=javascript:alert(1)"), out _));
        Assert.False(SearchExtractor.TryUnwrapGoogleRedirect(new Uri("https://example.com/url?q=https://a.example/"), out _));
    }

    internal const string DuckDuckGoHtml =
        """
        <html><body>
        <div class="result results_links result--ad"><h2 class="result__title"><a class="result__a" href="https://ad.example.com/">広告の結果</a></h2></div>
        <div class="result results_links results_links_deep web-result">
          <h2 class="result__title"><a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fweather.example.jp%2Ftokyo&amp;rut=abc">東京の天気 - 気象サイト</a></h2>
          <a class="result__snippet" href="#">今日の東京は<b>晴れ</b>のち曇り。最高気温は25度の予想です。</a>
        </div>
        <div class="result results_links results_links_deep web-result">
          <h2 class="result__title"><a class="result__a" href="https://news.example.com/w">週間天気予報 | ニュース</a></h2>
          <a class="result__snippet" href="#">週末は雨の可能性があります。</a>
        </div>
        <div class="result results_links web-result">
          <h2 class="result__title"><a class="result__a" href="https://third.example.net/">3件目</a></h2>
        </div>
        <div class="result"><h2><a class="result__a" href="http://localhost/">ローカル</a></h2></div>
        </body></html>
        """;

    [Fact]
    public void ParsesDuckDuckGoResultsSkippingAdsAndUnwrappingLinks()
    {
        var results = SearchExtractor.ParseDuckDuckGo(DuckDuckGoHtml);
        Assert.Equal(3, results.Count);
        Assert.Equal("東京の天気 - 気象サイト", results[0].Title);
        Assert.Equal("https://weather.example.jp/tokyo", results[0].Url);
        Assert.Equal("weather.example.jp", results[0].Domain);
        Assert.Equal("今日の東京は晴れのち曇り。最高気温は25度の予想です。", results[0].Snippet);
        Assert.DoesNotContain(results, r => r.Title.Contains("広告", StringComparison.Ordinal) || r.Title == "ローカル");
    }

    [Fact]
    public void ParsesBingResultsAndDecodesTrackingLinks()
    {
        var encoded = "a1" + Convert.ToBase64String("https://example.com/page?x=1"u8.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var html = $"""
            <ol id="b_results">
              <li class="b_algo"><h2><a href="https://www.bing.com/ck/a?!&amp;&amp;p=abc&amp;u={encoded}&amp;ntb=1">ページの題名</a></h2>
                <div class="b_caption"><p>ページの抜粋です。</p></div></li>
              <li class="b_algo"><h2><a href="https://direct.example.org/">直接リンク</a></h2><p>別の抜粋</p></li>
            </ol>
            """;
        var results = SearchExtractor.ParseBing(html);
        Assert.Equal(2, results.Count);
        Assert.Equal("https://example.com/page?x=1", results[0].Url);
        Assert.Equal("ページの抜粋です。", results[0].Snippet);
        Assert.Equal("https://direct.example.org/", results[1].Url);
    }

    [Fact]
    public void BuildsSearchContentForTheSummary()
    {
        var c = SearchExtractor.Build(new Uri("https://www.google.com/search?q=x"), "東京 天気", SearchExtractor.ParseDuckDuckGo(DuckDuckGoHtml), "DuckDuckGo");
        Assert.Equal(PageKind.Search, c.Kind);
        Assert.Equal("「東京 天気」の検索結果", c.Title);
        Assert.Equal(SourceQuality.FullText, c.Quality);
        Assert.StartsWith("検索キーワード: 東京 天気", c.Text);
        Assert.Contains("1. 東京の天気 - 気象サイト（weather.example.jp）", c.Text);
        Assert.Contains(c.Notes, n => n.Contains("DuckDuckGo", StringComparison.Ordinal));

        var empty = SearchExtractor.Build(new Uri("https://www.google.com/search?q=x"), "x", new(), null);
        Assert.Equal(SourceQuality.MetadataOnly, empty.Quality);
        Assert.Contains(empty.Notes, n => n.Contains("取得できませんでした", StringComparison.Ordinal));
    }

    private sealed class Ui : IPipelineUi
    {
        public List<CardState> States { get; } = new();
        public void Show(CardState state) { lock (States) States.Add(state); }
        public Task<ConsentDecision> RequestConsentAsync(ConsentInfo info, CancellationToken ct) => Task.FromResult(ConsentDecision.Send);
    }

    private sealed class EchoAi : ISummarizerProvider
    {
        public string? LastUser;
        public string Id => "echo";
        public string DisplayName => "Echo";
        public string Model => "m";
        public string EndpointHost => "api.example";
        public bool IsTestProvider => false;
        public Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
        {
            LastUser = prompt.User;
            return Task.FromResult("""{"title":"東京 天気の検索結果","summary":["東京は晴れのち曇りの予想です。"],"keyPoints":[],"confidence":"medium"}""");
        }
    }

    private static (SummaryPipeline pipeline, TestServer server, TempDir dir, SummaryCache cache) Pipeline(AppSettings settings, Func<ISummarizerProvider?> provider, bool ddgFails = false)
    {
        var server = new TestServer();
        if (ddgFails) server.MapText("/ddg", "<html><body>anomaly</body></html>");
        else server.MapText("/ddg", DuckDuckGoHtml);
        server.MapText("/bing", """<ol><li class="b_algo"><h2><a href="https://bing-result.example.com/">Bingの結果</a></h2><p>抜粋</p></li></ol>""");
        var content = new ContentService(new SafeHttpFetcher(true))
        {
            SearchSourcesForTesting = q => new[]
            {
                new SearchSource("DuckDuckGo", server.Url("/ddg?q=" + Uri.EscapeDataString(q)), SearchExtractor.ParseDuckDuckGo),
                new SearchSource("Bing", server.Url("/bing?q=" + Uri.EscapeDataString(q)), SearchExtractor.ParseBing),
            },
        };
        var dir = new TempDir();
        var cache = new SummaryCache(Path.Combine(dir.Path, "c.db"));
        return (new SummaryPipeline(content, cache, () => settings, provider, _ => { }, new SummarizerEngine((_, _) => Task.CompletedTask)), server, dir, cache);
    }

    private const string GoogleUrl = "https://www.google.com/search?q=%E6%9D%B1%E4%BA%AC+%E5%A4%A9%E6%B0%97";

    [Fact]
    public async Task ShowsSearchResultsWithoutAi()
    {
        var (pipeline, server, dir, _) = Pipeline(new AppSettings(), () => null);
        using (server) using (dir)
        {
            var ui = new Ui();
            await pipeline.RunAsync(new SummaryRequest(GoogleUrl, "abcd1234"), ui, default);
            var card = ui.States[^1].Card!;
            Assert.Equal(CardPhase.Result, ui.States[^1].Phase);
            Assert.Equal(PageKind.Search, card.Kind);
            Assert.Equal("「東京 天気」の検索結果", card.Title);
            Assert.Equal(3, card.SearchResults.Count);
            Assert.Equal("東京の天気 - 気象サイト", card.SearchResults[0].Title);
        }
    }

    [Fact]
    public async Task AiSummarizesTheSearchResultsAndTheyAreNotCached()
    {
        var ai = new EchoAi();
        var settings = new AppSettings { ConfirmBeforeSend = false };
        var (pipeline, server, dir, cache) = Pipeline(settings, () => ai);
        settings.ConsentedProviders.Add("echo|api.example");
        using (server) using (dir)
        {
            var ui = new Ui();
            await pipeline.RunAsync(new SummaryRequest(GoogleUrl, "abcd1234"), ui, default);
            var card = ui.States[^1].Card!;
            Assert.Equal("東京は晴れのち曇りの予想です。", card.SummaryLines[0]);
            Assert.NotEmpty(card.SearchResults);
            Assert.Contains("種別: 検索", ai.LastUser);
            Assert.Contains("晴れのち曇り", ai.LastUser);
            // 検索結果は時間で変わるため保存しない
            Assert.Equal(0, cache.Stats().Count);
        }
    }

    [Fact]
    public async Task FallsBackToBingWhenDuckDuckGoReturnsNoResults()
    {
        var (pipeline, server, dir, _) = Pipeline(new AppSettings(), () => null, ddgFails: true);
        using (server) using (dir)
        {
            var ui = new Ui();
            await pipeline.RunAsync(new SummaryRequest(GoogleUrl, "abcd1234"), ui, default);
            var card = ui.States[^1].Card!;
            Assert.Equal("Bingの結果", Assert.Single(card.SearchResults).Title);
            Assert.Contains(card.Notes, n => n.Contains("Bing", StringComparison.Ordinal));
        }
    }
}
