using System.Text.Json;
using UrlInsight.Core.AI;
using UrlInsight.Core.Content;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Tests;

public class LocalSummarizerTests
{
    private const string JapaneseArticle =
        """
        ホーム | ニュース | お問い合わせ
        政府は2026年度から、再生可能エネルギーの導入を加速する新しい補助金制度を始めると発表した。
        新制度では、家庭用の太陽光発電と蓄電池をあわせて導入する世帯に、費用の一部を補助する。
        補助金の対象は全国の一般家庭で、申請は自治体の窓口とオンラインの両方で受け付ける予定だ。
        今日は天気が良かったので散歩に行きました。
        専門家は、蓄電池の普及によって再生可能エネルギーの電力を夜間にも使えるようになると指摘している。
        一方で、補助金の予算規模が十分かどうかについては議論が続いている。
        当サイトではCookieを使用しています。詳しくはプライバシーポリシーをご覧ください。
        Copyright © 2026 Example News. All rights reserved.
        """;

    private static SummaryInput Input(string text, string? title = "再生可能エネルギーの補助金制度を開始", string? description = null)
        => new(new Uri("https://news.example.com/a/1"), PageKind.Web, title, description, text, SourceQuality.FullText);

    [Fact]
    public void PicksSentencesFromTheBodyAndSkipsBoilerplate()
    {
        var output = LocalSummarizer.Summarize(Input(JapaneseArticle));

        Assert.InRange(output.SummaryLines.Count, 1, SummaryValidator.MaxLines);
        // 抽出型なので、要約の各文は本文にそのまま含まれている
        Assert.All(output.SummaryLines, l => Assert.Contains(l, JapaneseArticle));
        Assert.Contains(output.SummaryLines, l => l.Contains("補助金制度", StringComparison.Ordinal));
        Assert.DoesNotContain(output.SummaryLines, l => l.Contains("Cookie", StringComparison.Ordinal) || l.Contains("Copyright", StringComparison.Ordinal));
        Assert.DoesNotContain(output.SummaryLines, l => l.Contains("ホーム |", StringComparison.Ordinal));
        Assert.DoesNotContain(output.SummaryLines, l => l.Contains("散歩", StringComparison.Ordinal));
        Assert.Equal("再生可能エネルギーの補助金制度を開始", output.Title);
    }

    [Fact]
    public void KeepsOriginalOrderAndAddsKeywordsAndLength()
    {
        var output = LocalSummarizer.Summarize(Input(JapaneseArticle));
        var positions = output.SummaryLines.Select(l => JapaneseArticle.IndexOf(l, StringComparison.Ordinal)).ToList();
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.Contains(output.KeyPoints, p => p.StartsWith("よく出てくる語: ", StringComparison.Ordinal) && p.Contains("補助金", StringComparison.Ordinal));
        Assert.Contains(output.KeyPoints, p => p.StartsWith("本文の長さ: 約", StringComparison.Ordinal) && p.Contains("文字", StringComparison.Ordinal));
    }

    [Fact]
    public void UsesMetaDescriptionAsFirstLine()
    {
        const string description = "政府が家庭向けの太陽光発電と蓄電池の補助金制度を新設すると発表しました。";
        var output = LocalSummarizer.Summarize(Input(JapaneseArticle, description: description));
        Assert.Equal(description, output.SummaryLines[0]);
    }

    [Fact]
    public void WorksForEnglishText()
    {
        const string text =
            "The city council approved a new budget for public transport on Monday. " +
            "The budget adds funding for electric buses and longer service hours. " +
            "Council members said the electric buses will replace older diesel vehicles by 2030. " +
            "Click here to subscribe to our newsletter today. " +
            "Residents can comment on the transport plan until the end of the month.";
        var output = LocalSummarizer.Summarize(Input(text, title: "Council approves transport budget"));
        Assert.NotEmpty(output.SummaryLines);
        Assert.All(output.SummaryLines, l => Assert.Contains(l, text));
        Assert.DoesNotContain(output.SummaryLines, l => l.Contains("subscribe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(output.KeyPoints, p => p.Contains("語（読むのに約", StringComparison.Ordinal));
    }

    [Fact]
    public void FallsBackToTheBeginningWhenNoSentenceIsUsable()
    {
        var output = LocalSummarizer.Summarize(Input("短い文。", title: null));
        Assert.Single(output.SummaryLines);
        Assert.Equal("news.example.com", output.Title);
    }

    [Fact]
    public async Task ProviderOutputPassesTheSchemaValidator()
    {
        var provider = new LocalSummaryProvider();
        var prompt = SummaryPromptBuilder.Build(Input(JapaneseArticle), 12_000);
        var raw = await provider.CompleteAsync(prompt, default);
        Assert.True(SummaryValidator.TryParse(raw, out var parsed));
        Assert.NotEmpty(parsed.SummaryLines);
        Assert.True(((ISummarizerProvider)provider).IsLocal);
        Assert.False(provider.IsTestProvider);
    }

    [Fact]
    public void CatalogCreatesLocalProviderWithoutKeyOrModel()
    {
        var p = ProviderCatalog.Create("local", null, "", null);
        Assert.IsType<LocalSummaryProvider>(p);
        Assert.False(ProviderCatalog.Get("local").RequiresKey);
    }

    [Fact]
    public void NewAndOldUnconfiguredSettingsUseLocalSummary()
    {
        Assert.Equal("local", new AppSettings().ProviderId);

        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        // 旧版(SchemaVersion 1)で「未設定」のまま保存された設定
        File.WriteAllText(path, """{"SchemaVersion":1,"ProviderId":"none"}""");
        Assert.Equal("local", new SettingsStore(path).Load().ProviderId);

        // API を設定済みの人の選択は変えない
        File.WriteAllText(path, """{"SchemaVersion":1,"ProviderId":"anthropic","Model":"m"}""");
        Assert.Equal("anthropic", new SettingsStore(path).Load().ProviderId);

        // 新しい版で、あえて「未設定」を選んだ人の選択も変えない
        File.WriteAllText(path, """{"SchemaVersion":2,"ProviderId":"none"}""");
        Assert.Equal("none", new SettingsStore(path).Load().ProviderId);
    }
}
