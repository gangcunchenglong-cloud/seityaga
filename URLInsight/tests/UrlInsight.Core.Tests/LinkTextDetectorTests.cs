using UrlInsight.Core.Net;

namespace UrlInsight.Core.Tests;

public class LinkTextDetectorTests
{
    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    [InlineData("javascript:void(0)", null)]
    [InlineData("http://localhost:3000/", null)]
    [InlineData("mailto:a@example.com", null)]
    [InlineData("", null)]
    public void LinkValue(string value, string? expected) => Assert.Equal(expected, LinkTextDetector.FromLinkValue(value));

    [Theory]
    [InlineData("詳しくは https://example.com/news/1 を参照。", "https://example.com/news/1")]
    [InlineData("（https://example.com/a）", "https://example.com/a")]
    [InlineData("(https://en.wikipedia.org/wiki/Foo_(bar))", "https://en.wikipedia.org/wiki/Foo_(bar)")]
    [InlineData("https://example.com/a.", "https://example.com/a")]
    [InlineData("https://a.example.com/ と https://b.example.com/", null)]
    [InlineData("URLはありません", null)]
    public void SingleUrl(string text, string? expected) => Assert.Equal(expected, LinkTextDetector.FindSingleUrl(text));

    [Fact]
    public void UrlAtCursorOffset()
    {
        const string line = "A: https://a.example.com/x B: https://b.example.com/y";
        Assert.Equal("https://a.example.com/x", LinkTextDetector.UrlAtOffset(line, 10));
        Assert.Equal("https://b.example.com/y", LinkTextDetector.UrlAtOffset(line, 40));
        Assert.Null(LinkTextDetector.UrlAtOffset(line, 0));
        Assert.Null(LinkTextDetector.UrlAtOffset(line, 28));
    }
}
