using UrlInsight.Core.AI;
using UrlInsight.Core.Pipeline;

namespace UrlInsight.Core.Tests;

/// <summary>Ctrl を押しながら重ねたリンクをまとめて Chrome の Gemini に送る機能のテスト。</summary>
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
        Assert.Equal(LinkBatch.MaxLinks, b.Count);
        Assert.True(b.Overflowed);
        b.Clear();
        Assert.Equal(0, b.Count);
        Assert.False(b.Overflowed);
    }

    [Fact]
    public void PromptsAreSingleLineAndListEveryLink()
    {
        var one = SidePanelPrompts.Gemini(new[] { "https://a.example/\nx" });
        Assert.DoesNotContain('\n', one);
        Assert.Contains("https://a.example/ x", one);

        var many = SidePanelPrompts.Gemini(new[] { "https://a.example/", "https://b.example/", "https://c.example/" });
        Assert.DoesNotContain('\n', many);
        Assert.Contains("3件", many);
        Assert.Contains("(1) https://a.example/ (2) https://b.example/ (3) https://c.example/", many);

        Assert.Contains("開いて", SidePanelPrompts.Claude(new[] { "https://a.example/" }));
        Assert.Throws<ArgumentException>(() => SidePanelPrompts.Gemini(Array.Empty<string>()));
    }
}
