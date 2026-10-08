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

    [Theory]
    [InlineData("https://example.com/very/long/pa…")]
    [InlineData("https://example.com/very/long/pa...")]
    public void TruncatedDisplayUrlsAreNotUsed(string text)
    {
        Assert.Null(LinkTextDetector.FindSingleUrl(text));
        Assert.Null(LinkTextDetector.FromLinkValue(text));
    }

    [Fact]
    public void ReturnsWholeUrlWhenCursorIsInTheMiddle()
    {
        // 前後の文章ごと取得した文字列の中で、カーソルが URL の途中にあっても URL 全体を返す
        const string text = "前の段落です。\n参考: https://example.com/articles/2026/10/very-long-article-name?id=12345 をご覧ください。\n次の段落";
        int middle = text.IndexOf("very-long", StringComparison.Ordinal);
        Assert.Equal("https://example.com/articles/2026/10/very-long-article-name?id=12345", LinkTextDetector.UrlAtOffset(text, middle));
        int end = text.IndexOf("12345", StringComparison.Ordinal) + 4;
        Assert.Equal("https://example.com/articles/2026/10/very-long-article-name?id=12345", LinkTextDetector.UrlAtOffset(text, end));
    }

    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("example.com/news/123?id=4", "https://example.com/news/123?id=4")]
    [InlineData("  www.example.co.jp  ", "https://www.example.co.jp")]
    [InlineData("google.com/search?q=東京+天気", "https://google.com/search?q=東京+天気")]
    [InlineData("例え.jp/ページ", "https://例え.jp/ページ")]
    [InlineData("example.com:8443/x", "https://example.com:8443/x")]
    [InlineData("東京 天気", null)]
    [InlineData("exam", null)]
    [InlineData("chrome://settings", null)]
    [InlineData("file:///C:/a.txt", null)]
    [InlineData("localhost:3000", null)]
    [InlineData("192.168.0.1/admin", null)]
    [InlineData("", null)]
    public void AddressBarValue(string value, string? expected)
    {
        var actual = LinkTextDetector.FromAddressBar(value);
        if (expected == null) Assert.Null(actual);
        else Assert.Equal(new Uri(expected), new Uri(actual!));
    }

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
