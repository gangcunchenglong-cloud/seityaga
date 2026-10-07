using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using UrlInsight.Core.Content;
using UrlInsight.Core.Net;

namespace UrlInsight.Core.Tests;

public class HtmlExtractorTests
{
    private static readonly Uri U = new("https://example.com/article");

    [Fact]
    public void ExtractsMetadataAndBody()
    {
        var para = string.Concat(Enumerable.Repeat("生成AIの普及で定型業務の自動化が進んでいます。導入には人の確認が欠かせません。", 10));
        var html = $"""
            <html><head><title>Doc Title</title>
            <meta property="og:title" content="OGタイトル">
            <meta name="description" content="ページの説明文">
            <meta property="og:site_name" content="Example News">
            </head><body><nav>メニュー</nav><article><h1>見出し</h1><p>{para}</p><p>{para}</p></article>
            <script>alert('x')</script></body></html>
            """;
        var c = HtmlExtractor.Extract(html, U);
        Assert.Equal("OGタイトル", c.Title);
        Assert.Equal("ページの説明文", c.Description);
        Assert.Equal("Example News", c.SiteName);
        Assert.Contains("定型業務の自動化", c.Text);
        Assert.DoesNotContain("alert(", c.Text);
        Assert.Equal(SourceQuality.FullText, c.Quality);
    }

    [Fact]
    public void EmptyPageIsMetadataOnly()
    {
        var c = HtmlExtractor.Extract("<html><head><title>T</title></head><body></body></html>", U);
        Assert.Equal("T", c.Title);
        Assert.Equal(SourceQuality.MetadataOnly, c.Quality);
    }

    [Fact]
    public void TruncatesHugeBody()
    {
        var p = new string('あ', 1000);
        var html = "<html><body><article>" + string.Concat(Enumerable.Repeat($"<p>{p}</p>", 80)) + "</article></body></html>";
        var c = HtmlExtractor.Extract(html, U);
        Assert.True(c.Truncated);
        Assert.True(c.Text.Length <= TextUtil.MaxBodyChars);
    }

    [Fact]
    public void DecodesShiftJisFromMetaCharset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var html = "<html><head><meta charset=\"Shift_JIS\"><title>日本語タイトル</title></head><body><p>本文です</p></body></html>";
        var bytes = Encoding.GetEncoding("shift_jis").GetBytes(html);
        var text = HtmlExtractor.Decode(bytes, null);
        Assert.Contains("日本語タイトル", text);
    }

    [Fact]
    public void HeaderCharsetWins()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding("euc-jp").GetBytes("<html><title>テスト</title></html>");
        Assert.Contains("テスト", HtmlExtractor.Decode(bytes, "EUC-JP"));
    }

    [Fact]
    public void MaliciousHtmlDoesNotThrowAndControlCharsAreRemoved()
    {
        var html = "<html><head><title>\u0001悪意\u0007<img src=x onerror=alert(1)></title>" +
                   "<script type='application/ld+json'>{bad json</script></head><body>" +
                   string.Concat(Enumerable.Repeat("<div><div><div>", 2000)) + "text\u0000" + "</body></html>";
        var c = HtmlExtractor.Extract(html, U);
        Assert.DoesNotContain('\u0001', c.Title ?? "");
        Assert.DoesNotContain('\u0000', c.Text);
    }

    [Fact]
    public void ReadsJsonLdWhenMetaMissing()
    {
        var html = """
            <html><head><script type="application/ld+json">{"@graph":[{"@type":"NewsArticle","headline":"LD見出し","description":"LD説明"}]}</script></head><body></body></html>
            """;
        var c = HtmlExtractor.Extract(html, U);
        Assert.Equal("LD見出し", c.Title);
        Assert.Equal("LD説明", c.Description);
    }
}

public class PdfExtractorTests
{
    internal static byte[] MakePdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        double y = 780;
        foreach (var line in lines)
        {
            if (line.Length > 0) page.AddText(line, 10, new PdfPoint(30, y), font);
            y -= 14;
        }
        return builder.Build();
    }

    [Fact]
    public void ExtractsText()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"Line {i}: The project schedule includes milestone number {i}.").ToArray();
        var pdf = MakePdf(lines);
        Assert.True(PdfExtractor.LooksLikePdf(pdf));
        var c = PdfExtractor.Extract(pdf, new Uri("https://example.com/docs/plan-2026.pdf"));
        Assert.Equal(PageKind.Pdf, c.Kind);
        Assert.Contains("milestone", c.Text);
        Assert.Equal("plan-2026.pdf", c.Title);
    }

    [Fact]
    public void ImageOnlyPdfIsReported()
    {
        var pdf = MakePdf("");
        var ex = Assert.Throws<InsightException>(() => PdfExtractor.Extract(pdf, new Uri("https://example.com/scan.pdf")));
        Assert.Equal(ErrorCode.PdfImageOnly, ex.Code);
    }

    [Fact]
    public void GarbageIsUnsupported()
    {
        var ex = Assert.Throws<InsightException>(() => PdfExtractor.Extract(Encoding.ASCII.GetBytes("%PDF-1.4 garbage"), new Uri("https://example.com/x.pdf")));
        Assert.True(ex.Code is ErrorCode.UnsupportedContent or ErrorCode.PdfImageOnly);
    }
}

public class YouTubeTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=10", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void ParsesVideoIds(string url, string id)
    {
        Assert.True(YouTubeExtractor.TryGetVideoId(new Uri(url), out var got));
        Assert.Equal(id, got);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/channel/UCxyz")]
    [InlineData("https://evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ<script>")]
    public void RejectsInvalid(string url)
        => Assert.False(YouTubeExtractor.TryGetVideoId(new Uri(url), out _));
}

public class ContentServiceTests
{
    [Fact]
    public async Task ClassifiesHtmlPdfAndUnsupported()
    {
        using var server = new TestServer();
        var body = string.Concat(Enumerable.Repeat("<p>This is a sufficiently long paragraph about technology and work.</p>", 20));
        server.MapText("/page", $"<html><head><title>Page</title></head><body><article>{body}</article></body></html>");
        server.MapBytes("/doc", PdfExtractorTests.MakePdf(Enumerable.Range(0, 20).Select(i => $"PDF body line {i} with enough words").ToArray()), "application/pdf");
        server.MapBytes("/img", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        using var fetcher = new SafeHttpFetcher(true);
        var svc = new ContentService(fetcher);

        var html = await svc.ExtractAsync(server.Url("/page"), default);
        Assert.Equal(PageKind.Web, html.Kind);
        Assert.Equal("Page", html.Title);

        var pdf = await svc.ExtractAsync(server.Url("/doc"), default);
        Assert.Equal(PageKind.Pdf, pdf.Kind);

        var ex = await Assert.ThrowsAsync<InsightException>(() => svc.ExtractAsync(server.Url("/img"), default));
        Assert.Equal(ErrorCode.UnsupportedContent, ex.Code);
    }
}
