using UrlInsight.Core.AI;
using UrlInsight.Core.Pipeline;

namespace UrlInsight.Core.Tests;

/// <summary>Ctrl を押しながら重ねたリンクをまとめて Chrome のサイドバーの AI(ChatGPT など)に送る機能のテスト。</summary>
public class LinkBatchTests
{
    [Fact]
    public void CollectsUniqueValidLinksInOrder()
    {
        var b = new LinkBatch();
        Assert.True(b.Add("https://a.example/x", "記事A"));
        Assert.True(b.Add("https://b.example/"));
        Assert.False(b.Add("https://a.example/x"));
        Assert.False(b.Add("javascript:alert(1)"));
        Assert.False(b.Add("http://localhost/"));
        Assert.False(b.Add("https://ignored.example/", null, u => u.Host == "ignored.example"));
        Assert.Equal(new[] { "https://a.example/x", "https://b.example/" }, b.Urls);
        Assert.Equal("記事A", b.Titles[0]);
        Assert.Null(b.Titles[1]);
    }

    [Fact]
    public void StopsAtTheLimit()
    {
        var b = new LinkBatch();
        for (int i = 0; i < LinkBatch.MaxLinks + 3; i++) b.Add($"https://e{i}.example/");
        Assert.Equal(100, LinkBatch.MaxLinks);
        Assert.Equal(LinkBatch.MaxLinks, b.Count);
        Assert.True(b.Overflowed);
        b.Clear();
        Assert.Equal(0, b.Count);
        Assert.False(b.Overflowed);
    }

    [Theory]
    [InlineData("Sider: Chat with all AI", "Sider", true)]
    [InlineData("Sider", "Sider", true)]
    [InlineData("Open Sider sidebar", "Sider", true)]
    [InlineData("Things to be considered", "Sider", false)]
    [InlineData("Outsider", "Sider", false)]
    [InlineData("Gemini in Chrome", "Gemini", true)]
    [InlineData("ChatGPT", "ChatGPT", true)]
    [InlineData("Ask ChatGPT", "ChatGPT", true)]
    [InlineData("chatgpt side chat", "ChatGPT", true)]
    [InlineData("MyChatGPTs list", "ChatGPT", false)]
    [InlineData("Claude", "Claude", true)]
    [InlineData(null, "Sider", false)]
    public void PanelNamesMatchOnlyWholeWords(string? name, string word, bool expected)
        => Assert.Equal(expected, SidePanelPrompts.NameHasWord(name, word));

    [Fact]
    public void PromptsAreSingleLineAndListEveryLink()
    {
        var one = SidePanelPrompts.Summarize(new[] { "https://a.example/\nx" });
        Assert.DoesNotContain('\n', one);
        Assert.Contains("https://a.example/ x", one);

        var many = SidePanelPrompts.Summarize(new[] { "https://a.example/", "https://b.example/", "https://c.example/" });
        Assert.DoesNotContain('\n', many);
        Assert.Contains("3件", many);
        Assert.Contains("(1) https://a.example/ (2) https://b.example/ (3) https://c.example/", many);

        Assert.Contains("開いて", SidePanelPrompts.Claude(new[] { "https://a.example/" }));

        // 上限の100件でも1行のまま、すべて番号付きで入る
        var hundred = SidePanelPrompts.Summarize(Enumerable.Range(1, 100).Select(i => $"https://e{i}.example/").ToList());
        Assert.DoesNotContain('\n', hundred);
        Assert.Contains("100件", hundred);
        Assert.Contains("(100) https://e100.example/", hundred);
        Assert.Throws<ArgumentException>(() => SidePanelPrompts.Summarize(Array.Empty<string>()));
    }

    [Fact]
    public void WebQueryUrlsEncodesThePromptAndKeepsEveryLink()
    {
        var one = SidePanelPrompts.WebQueryUrls("https://chatgpt.com/?q=", new[] { "https://a.example/?x=1&y=2" }, SidePanelPrompts.Summarize);
        Assert.Single(one);
        Assert.StartsWith("https://chatgpt.com/?q=", one[0].Url);
        Assert.DoesNotContain("&y=", one[0].Url);
        Assert.Equal(Uri.EscapeDataString(one[0].Prompt), one[0].Url["https://chatgpt.com/?q=".Length..]);

        var urls = Enumerable.Range(1, 100).Select(i => $"https://example{i}.example/some/long/path/{i}").ToList();
        var pages = SidePanelPrompts.WebQueryUrls("https://chatgpt.com/?q=", urls, SidePanelPrompts.Summarize);
        Assert.True(pages.Count > 1);
        Assert.All(pages, p => Assert.True(p.Url.Length - "https://chatgpt.com/?q=".Length <= SidePanelPrompts.MaxEncodedQuery));
        foreach (var u in urls) Assert.Single(pages, p => p.Prompt.Contains(u + " ") || p.Prompt.EndsWith(u));
    }
}
