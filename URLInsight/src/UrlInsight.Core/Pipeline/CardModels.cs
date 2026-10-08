using UrlInsight.Core.Content;

namespace UrlInsight.Core.Pipeline;

public enum CardPhase { Loading, Consent, Summarizing, Result, Error }

/// <summary>カードに表示する完成済みの内容(キャッシュにも保存する)。</summary>
public sealed class SummaryCard
{
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public PageKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SiteName { get; set; }
    public string? Description { get; set; }
    public List<string> SummaryLines { get; set; } = new();
    public List<string> KeyPoints { get; set; } = new();
    public string Confidence { get; set; } = "medium";
    public SourceQuality Quality { get; set; }
    public List<string> Notes { get; set; } = new();
    /// <summary>検索ページのときの上位の検索結果(カードに一覧表示)。</summary>
    public List<SearchResultItem> SearchResults { get; set; } = new();
    public string ProviderName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool IsTestProvider { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>AI要約が無い(メタ情報のみ)ときの理由。</summary>
    public ErrorCode Notice { get; set; } = ErrorCode.None;

    public bool HasSummary => SummaryLines.Count > 0;
}

/// <summary>カードの表示状態。パイプラインから UI へ渡す。</summary>
public sealed class CardState
{
    public required CardPhase Phase { get; init; }
    public required string Url { get; init; }
    public required string Domain { get; init; }
    public PageKind? Kind { get; init; }
    public string? Title { get; init; }
    public string? StatusText { get; init; }
    public SummaryCard? Card { get; init; }
    public bool FromCache { get; init; }
    public ErrorCode Error { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record ConsentInfo(
    string ProviderName,
    string EndpointHost,
    string Url,
    PageKind Kind,
    int CharCount,
    ErrorCode Warning,
    bool ShowRememberOption);

public enum ConsentDecision { Send, SendAndRemember, Decline }
