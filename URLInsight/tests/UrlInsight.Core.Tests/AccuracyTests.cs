using System.Text.Json;
using UrlInsight.Core.AI;
using UrlInsight.Core.Content;

namespace UrlInsight.Core.Tests;

/// <summary>要約の精度向上(本文の整理・冒頭と末尾の送信・数値の裏付け確認)のテスト。</summary>
public class AccuracyTests
{
    [Fact]
    public void CleanerRemovesNoiseButKeepsBodyAndFacts()
    {
        const string text =
            """
            ホーム
            ニュース
            経済
            スポーツ
            新しい補助金制度が来年4月から始まる。
            対象は全国の一般家庭で、申請はオンラインでも受け付ける。
            価格: 12,800円
            ¥12,800
            このサイトではCookieを使用しています
            この記事をシェアする
            関連記事
            Copyright © 2026 Example
            ───────
            専門家は、制度の周知にはCookieのような身近な例えを使った説明が欠かせないと指摘しており、自治体の説明会も各地で予定されているほか、オンラインでの相談窓口も新たに設けられる見通しだ。
            ホーム
            """;
        var cleaned = BodyCleaner.Clean(text);

        Assert.Contains("新しい補助金制度が来年4月から始まる。", cleaned);
        Assert.Contains("価格: 12,800円", cleaned);
        Assert.Contains("¥12,800", cleaned);
        // 長い文は、定型文の語(Cookie)を含んでいても本文として残す
        Assert.Contains("専門家は、制度の周知には", cleaned);
        Assert.DoesNotContain("ニュース", cleaned);
        Assert.DoesNotContain("スポーツ", cleaned);
        Assert.DoesNotContain("このサイトではCookie", cleaned);
        Assert.DoesNotContain("シェア", cleaned);
        Assert.DoesNotContain("関連記事", cleaned);
        Assert.DoesNotContain("Copyright", cleaned);
        Assert.DoesNotContain("───", cleaned);
        Assert.DoesNotContain("ホーム", cleaned);
    }

    [Fact]
    public void CleanerKeepsOriginalWhenEverythingWouldBeRemoved()
    {
        Assert.Equal("ログイン", BodyCleaner.Clean("ログイン"));
        Assert.Equal(string.Empty, BodyCleaner.Clean("  "));
    }

    [Fact]
    public void LongBodySendsBeginningAndEnd()
    {
        var head = string.Concat(Enumerable.Repeat("冒頭の段落です。\n", 400));
        var text = "最初の一文。\n" + head + "まとめ: 結論はこれです。";
        var (excerpt, cut) = SummaryPromptBuilder.Excerpt(text, 1000);

        Assert.True(cut);
        Assert.StartsWith("最初の一文。", excerpt);
        Assert.EndsWith("まとめ: 結論はこれです。", excerpt);
        Assert.Contains("（…中略…）", excerpt);
        Assert.True(excerpt.Length <= 1000 + SummaryPromptBuilder.OmissionMarker.Length);

        var p = SummaryPromptBuilder.Build(new SummaryInput(new Uri("https://e.com/"), PageKind.Web, "t", null, text, SourceQuality.FullText), 1000);
        Assert.Contains("冒頭と末尾の一部のみ", p.User);
        Assert.Contains("結論はこれです", p.User);
    }

    [Fact]
    public void ShortBodyIsSentAsIs()
    {
        var (excerpt, cut) = SummaryPromptBuilder.Excerpt("短い本文", 1000);
        Assert.False(cut);
        Assert.Equal("短い本文", excerpt);
    }

    private static SummaryInput Input(string text) =>
        new(new Uri("https://e.com/"), PageKind.Web, "2026年の制度", "説明", text, SourceQuality.FullText);

    private static SummaryOutput Output(params string[] lines) => new() { Title = "t", SummaryLines = lines.ToList() };

    [Fact]
    public void GroundingFindsNumbersThatAreNotInTheBody()
    {
        var input = Input("参加者は１，２００人で、満足度は85%だった。2012年に始まった。");
        var output = Output("参加者は1200人だった。", "満足度は85%。", "2012年開始。", "3つの理由がある。", "費用は300万円。", "12月に開催。");
        var unsupported = SummaryGrounding.FindUnsupportedNumbers(output, input);
        // 全角・桁区切りの違いは同じ数値とみなす。1桁の数は確認しない。「12」は「2012」の一部としては一致させない
        Assert.Equal(new[] { "300", "12" }, unsupported);
    }

    [Fact]
    public void GroundingAcceptsNumbersFromTitleAndDescription()
    {
        var unsupported = SummaryGrounding.FindUnsupportedNumbers(Output("2026年に始まる制度。"), Input("本文には年が無い。"));
        Assert.Empty(unsupported);
    }

    private sealed class SequenceProvider : ISummarizerProvider
    {
        private readonly Queue<string> _outputs;
        public SequenceProvider(params string[] outputs) => _outputs = new(outputs);
        public List<string> Prompts { get; } = new();
        public string Id => "seq";
        public string DisplayName => "seq";
        public string Model => "m";
        public string EndpointHost => "api.example";
        public bool IsTestProvider => false;
        public Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
        {
            Prompts.Add(prompt.User);
            return Task.FromResult(_outputs.Dequeue());
        }
    }

    private static string Json(params string[] lines) => JsonSerializer.Serialize(new { title = "t", summary = lines, keyPoints = Array.Empty<string>(), confidence = "high" });

    [Fact]
    public async Task EngineRetriesOnceWhenNumbersAreNotInTheBody()
    {
        var provider = new SequenceProvider(Json("参加者は5000人だった。"), Json("参加者は1200人だった。"));
        var output = await new SummarizerEngine((_, _) => Task.CompletedTask)
            .SummarizeAsync(provider, Input("参加者は1200人だった。"), 12_000, default);

        Assert.Equal(2, provider.Prompts.Count);
        Assert.Contains("本文に見当たらない数値（5000）", provider.Prompts[1]);
        Assert.Equal("参加者は1200人だった。", output.SummaryLines[0]);
        Assert.Empty(output.UnsupportedNumbers);
        Assert.Equal("high", output.Confidence);
    }

    [Fact]
    public async Task EngineMarksLowConfidenceWhenRetryStillHasUnsupportedNumbers()
    {
        var provider = new SequenceProvider(Json("参加者は5000人だった。"), Json("参加者は5000人で、9999件だった。"));
        var output = await new SummarizerEngine((_, _) => Task.CompletedTask)
            .SummarizeAsync(provider, Input("参加者は1200人だった。"), 12_000, default);

        Assert.Equal(2, provider.Prompts.Count);
        // 作り直しの方が悪ければ最初の要約を使う
        Assert.Equal("参加者は5000人だった。", output.SummaryLines[0]);
        Assert.Equal(new[] { "5000" }, output.UnsupportedNumbers);
        Assert.Equal("low", output.Confidence);
    }

    [Fact]
    public async Task EngineDoesNotRetryWhenNumbersAreGrounded()
    {
        var provider = new SequenceProvider(Json("参加者は1200人だった。"));
        await new SummarizerEngine((_, _) => Task.CompletedTask).SummarizeAsync(provider, Input("参加者は1200人だった。"), 12_000, default);
        Assert.Single(provider.Prompts);
    }

    [Fact]
    public void PromptAsksForConclusionFirstAndExactFacts()
    {
        Assert.Contains("summary の1文目", SummaryPromptBuilder.SystemPrompt);
        Assert.Contains("本文の表記どおり", SummaryPromptBuilder.SystemPrompt);
        Assert.Equal("p2", SummaryPromptBuilder.PromptVersion);
    }
}
