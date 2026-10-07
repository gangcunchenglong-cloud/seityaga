using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Net;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Tests;

/// <summary>取得 → 抽出 → 同意 → AI → 検証 → キャッシュ までの通しテスト(ローカルHTTPサーバー + 偽AI)。</summary>
public class PipelineTests
{
    private sealed class RecordingUi : IPipelineUi
    {
        public List<CardState> States { get; } = new();
        public ConsentDecision Decision { get; set; } = ConsentDecision.Send;
        public List<ConsentInfo> ConsentRequests { get; } = new();
        public void Show(CardState state) { lock (States) States.Add(state); }
        public Task<ConsentDecision> RequestConsentAsync(ConsentInfo info, CancellationToken ct)
        {
            ConsentRequests.Add(info);
            return Task.FromResult(Decision);
        }
        public CardState Last => States[^1];
    }

    private sealed class FakeAi : ISummarizerProvider
    {
        public int Calls;
        public string? LastUserPrompt;
        public string Id => "fake";
        public string DisplayName => "Fake AI";
        public string Model { get; set; } = "fake-1";
        public string EndpointHost => "api.fake.example";
        public bool IsTestProvider => false;
        public Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            LastUserPrompt = prompt.User;
            return Task.FromResult("""{"title":"AIタイトル","summary":["要約1","要約2"],"keyPoints":["点1","点2"],"confidence":"high"}""");
        }
    }

    private sealed class Harness : IDisposable
    {
        public TestServer Server { get; } = new();
        public TempDir Dir { get; } = new();
        public AppSettings Settings { get; } = new();
        public ISummarizerProvider? Provider { get; set; }
        public SummaryCache Cache { get; }
        public SummaryPipeline Pipeline { get; }

        public Harness()
        {
            Cache = new SummaryCache(Path.Combine(Dir.Path, "c.db"));
            Pipeline = new SummaryPipeline(new ContentService(new SafeHttpFetcher(true)), Cache, () => Settings, () => Provider,
                k => Settings.ConsentedProviders.Add(k), new SummarizerEngine((_, _) => Task.CompletedTask))
            {
                AllowLocalUrlsForTesting = true,
            };
            Server.MapText("/article", Article);
            Server.MapText("/empty", "<html><head><title>空</title></head><body></body></html>");
            Server.MapText("/forbidden", "no", status: 403);
        }

        public string Url(string path) => Server.Url(path).ToString();
        public void Dispose() { Server.Dispose(); Dir.Dispose(); }
    }

    private static string Article => "<html><head><title>記事タイトル</title><meta name=\"description\" content=\"説明\"></head><body><article>" +
        string.Concat(Enumerable.Repeat("<p>これは十分な長さの本文段落です。テクノロジーと働き方について説明しています。</p>", 20)) +
        "</article></body></html>";

    [Fact]
    public async Task RejectsLocalUrlsInProductionMode()
    {
        using var h = new Harness();
        var pipeline = new SummaryPipeline(new ContentService(new SafeHttpFetcher()), h.Cache, () => h.Settings, () => null, _ => { });
        var ui = new RecordingUi();
        await pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1234"), ui, default);
        Assert.Equal(CardPhase.Error, ui.Last.Phase);
        Assert.Equal(ErrorCode.BlockedAddress, ui.Last.Error);
        Assert.Equal(0, h.Server.Hits);
    }

    [Fact]
    public async Task RejectsJavascriptUrls()
    {
        using var h = new Harness();
        var ui = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest("javascript:alert(1)", "abcd1234"), ui, default);
        Assert.Equal(ErrorCode.UnsupportedUrl, ui.Last.Error);
    }

    [Fact]
    public async Task WithoutAiShowsMetadataOnlyAndNoSend()
    {
        using var h = new Harness();
        var ui = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1234"), ui, default);
        Assert.Equal(CardPhase.Loading, ui.States[0].Phase);
        Assert.Equal(CardPhase.Result, ui.Last.Phase);
        Assert.Equal(ErrorCode.AiNotConfigured, ui.Last.Card!.Notice);
        Assert.Equal("記事タイトル", ui.Last.Card.Title);
        Assert.False(ui.Last.Card.HasSummary);
    }

    [Fact]
    public async Task ConsentDeclinedMeansNoAiCall()
    {
        using var h = new Harness();
        var ai = new FakeAi();
        h.Provider = ai;
        var ui = new RecordingUi { Decision = ConsentDecision.Decline };
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1234"), ui, default);
        Assert.Single(ui.ConsentRequests);
        Assert.Equal("api.fake.example", ui.ConsentRequests[0].EndpointHost);
        Assert.True(ui.ConsentRequests[0].CharCount > 0);
        Assert.Equal(0, ai.Calls);
        Assert.Equal(ErrorCode.ConsentDeclined, ui.Last.Card!.Notice);
    }

    [Fact]
    public async Task SummarizesThenServesFromCacheWithoutSecondAiCall()
    {
        using var h = new Harness();
        var ai = new FakeAi();
        h.Provider = ai;
        var ui = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1234"), ui, default);
        Assert.Equal(1, ai.Calls);
        Assert.Equal(CardPhase.Result, ui.Last.Phase);
        Assert.Equal("AIタイトル", ui.Last.Card!.Title);
        Assert.Equal(new[] { "要約1", "要約2" }, ui.Last.Card.SummaryLines);
        Assert.Contains("<document>", ai.LastUserPrompt);
        Assert.Contains(ui.States, s => s.Phase == CardPhase.Summarizing);

        var hitsBefore = h.Server.Hits;
        var ui2 = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1235"), ui2, default);
        Assert.True(ui2.Last.FromCache);
        Assert.Equal(1, ai.Calls);
        Assert.Equal(hitsBefore, h.Server.Hits);

        // 再要約(強制)はキャッシュを使わない
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1236", ForceRefresh: true), new RecordingUi(), default);
        Assert.Equal(2, ai.Calls);

        // モデル変更で再生成
        ai.Model = "fake-2";
        var ui3 = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1237"), ui3, default);
        Assert.False(ui3.Last.FromCache);
        Assert.Equal(3, ai.Calls);
    }

    [Fact]
    public async Task RememberedConsentSkipsPromptWhenConfirmIsOff()
    {
        using var h = new Harness();
        h.Settings.ConfirmBeforeSend = false;
        h.Settings.CacheEnabled = false;
        var ai = new FakeAi();
        h.Provider = ai;
        var ui = new RecordingUi { Decision = ConsentDecision.SendAndRemember };
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1234"), ui, default);
        Assert.Single(ui.ConsentRequests);
        Assert.True(ui.ConsentRequests[0].ShowRememberOption);

        var ui2 = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1235"), ui2, default);
        Assert.Empty(ui2.ConsentRequests);
        Assert.Equal(2, ai.Calls);
    }

    [Fact]
    public async Task EmptyPageIsNotSentToAi()
    {
        using var h = new Harness();
        var ai = new FakeAi();
        h.Provider = ai;
        var ui = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/empty"), "abcd1234"), ui, default);
        Assert.Equal(0, ai.Calls);
        Assert.Equal(ErrorCode.ContentTooShort, ui.Last.Card!.Notice);
    }

    [Fact]
    public async Task SiteErrorsAreReported()
    {
        using var h = new Harness();
        h.Provider = new FakeAi();
        var ui = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/forbidden"), "abcd1234"), ui, default);
        Assert.Equal(CardPhase.Error, ui.Last.Phase);
        Assert.Equal(ErrorCode.AccessRestricted, ui.Last.Error);
    }

    [Fact]
    public async Task FixtureProviderNeedsNoConsentAndIsLabelled()
    {
        using var h = new Harness();
        h.Provider = new FixtureProvider();
        var ui = new RecordingUi();
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/article"), "abcd1234"), ui, default);
        Assert.Empty(ui.ConsentRequests);
        Assert.True(ui.Last.Card!.IsTestProvider);
        Assert.StartsWith("【テスト用】", ui.Last.Card.SummaryLines[0]);
    }

    [Fact]
    public async Task CancellationStopsWithoutShowingError()
    {
        using var h = new Harness();
        h.Server.Map("/slow", async ctx => { await Task.Delay(2000); await TestServer.Write(ctx, "x"u8.ToArray(), "text/html", 200); });
        h.Provider = new FakeAi();
        var ui = new RecordingUi();
        using var cts = new CancellationTokenSource(200);
        await h.Pipeline.RunAsync(new SummaryRequest(h.Url("/slow"), "abcd1234"), ui, cts.Token);
        Assert.DoesNotContain(ui.States, s => s.Phase == CardPhase.Error);
    }
}
