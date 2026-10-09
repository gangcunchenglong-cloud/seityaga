using UrlInsight.Core.AI;

namespace UrlInsight.Core.Tests;

public class SidebarPromptTests
{
    [Fact]
    public void SingleLinkPrompt()
    {
        var p = SidebarPrompt.Build(new[] { "https://example.com/a" });
        Assert.Equal("次のリンク先のページの内容を、日本語で3〜5文に要約してください: https://example.com/a", p);
    }

    [Fact]
    public void MultipleLinksAreNumberedInOneLine()
    {
        var p = SidebarPrompt.Build(new[] { "https://a.example/", "https://b.example/x\n", "https://c.example/" });
        Assert.StartsWith("次の3件のリンク先のページの内容を、それぞれ", p);
        Assert.EndsWith("1. https://a.example/ 2. https://b.example/x 3. https://c.example/", p);
        // 改行があると途中で送信されてしまう
        Assert.DoesNotContain('\n', p);
        Assert.DoesNotContain('\r', p);
    }

    [Fact]
    public void EmptyListIsRejected() => Assert.Throws<ArgumentException>(() => SidebarPrompt.Build(Array.Empty<string>()));
}
