using System.Security.Cryptography;
using System.Text;

namespace UrlInsight.Core.Content;

public enum PageKind { Web, Pdf, YouTube, Text, Unknown, Search }

/// <summary>要約の根拠となる本文の取得状況。</summary>
public enum SourceQuality
{
    /// <summary>本文を十分に取得できた。</summary>
    FullText,
    /// <summary>本文の一部(説明欄・短い本文など)のみ。</summary>
    Partial,
    /// <summary>タイトル等のメタ情報のみ。</summary>
    MetadataOnly,
}

public sealed class ExtractedContent
{
    public required PageKind Kind { get; init; }
    public required Uri Url { get; init; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? SiteName { get; set; }
    public string Text { get; set; } = string.Empty;
    public SourceQuality Quality { get; set; }
    public bool Truncated { get; set; }
    public List<string> Notes { get; } = new();
    /// <summary>検索ページ(<see cref="PageKind.Search"/>)のときの上位の検索結果。</summary>
    public List<SearchResultItem> SearchResults { get; } = new();

    /// <summary>続行前にユーザー確認が必要な注意(例: YouTube 字幕なし)。</summary>
    public ErrorCode Warning { get; set; } = ErrorCode.None;

    public string ContentHash => Hash(Kind + "\n" + Title + "\n" + Description + "\n" + Text);

    public static string Hash(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    public static SourceQuality QualityFor(int textLength)
        => textLength >= 400 ? SourceQuality.FullText : textLength >= 120 ? SourceQuality.Partial : SourceQuality.MetadataOnly;
}

public static class Labels
{
    public static string Kind(PageKind kind) => kind switch
    {
        PageKind.Web => "WEB記事",
        PageKind.Pdf => "PDF",
        PageKind.YouTube => "YouTube",
        PageKind.Text => "テキスト",
        PageKind.Search => "検索",
        _ => "取得不能",
    };

    public static string Quality(SourceQuality q) => q switch
    {
        SourceQuality.FullText => "本文を取得して要約",
        SourceQuality.Partial => "本文の一部のみから要約（根拠が限定的）",
        _ => "本文を取得できず、タイトル等のメタ情報のみ",
    };
}
