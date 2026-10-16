using UrlInsight.Core.Net;

namespace UrlInsight.Core.Pipeline;

/// <summary>
/// Ctrl を押しながらカーソルを重ねたリンクを集める入れ物(Ctrl を離したときに、まとめて Chrome の Gemini に送る)。
/// 同じリンクは1回だけ数え、http/https として検証できない URL と無視リストの URL は入れない。多すぎると依頼文が長くなるため上限を設ける。
/// </summary>
public sealed class LinkBatch
{
    public const int MaxLinks = 100;

    private readonly List<string> _urls = new();
    private readonly List<string?> _titles = new();

    public IReadOnlyList<string> Urls => _urls;
    /// <summary>表示用のリンク文字列(無い場合は null)。Urls と同じ順。</summary>
    public IReadOnlyList<string?> Titles => _titles;
    public int Count => _urls.Count;
    /// <summary>上限に達したあとに重ねたリンクがあった。</summary>
    public bool Overflowed { get; private set; }

    /// <returns>新しく加えたとき true。</returns>
    public bool Add(string url, string? linkText = null, Func<Uri, bool>? isIgnored = null)
    {
        if (!UrlPolicy.TryNormalize(url, out var normalized, out _)) return false;
        if (isIgnored?.Invoke(normalized!) == true) return false;
        var key = normalized!.ToString();
        if (_urls.Contains(key)) return false;
        if (_urls.Count >= MaxLinks)
        {
            Overflowed = true;
            return false;
        }
        _urls.Add(key);
        _titles.Add(string.IsNullOrWhiteSpace(linkText) ? null : linkText.Trim());
        return true;
    }

    public void Clear()
    {
        _urls.Clear();
        _titles.Clear();
        Overflowed = false;
    }
}
