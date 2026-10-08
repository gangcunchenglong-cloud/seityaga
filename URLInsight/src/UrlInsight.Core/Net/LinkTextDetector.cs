using System.Text.RegularExpressions;

namespace UrlInsight.Core.Net;

/// <summary>
/// 画面上のリンク・文字列から要約対象の URL を取り出す(拡張機能なしのホバー検出用)。
/// 取り出した URL は <see cref="UrlPolicy"/> で検証できたものだけを返す(javascript: や localhost 等は返さない)。
/// </summary>
public static partial class LinkTextDetector
{
    [GeneratedRegex(@"https?://[^\s<>""'「」『』【】、。，＜＞]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>表示上省略された URL(「…」付き)。途中までの URL は使わない。</summary>
    private static bool IsTruncated(string url) => url.Contains('…') || url.Contains("...", StringComparison.Ordinal);

    /// <summary>リンク要素の値(多くのブラウザでは href)を検証する。</summary>
    public static string? FromLinkValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return IsValid(v) ? v : null;
    }

    /// <summary>文字列中に URL がちょうど1つだけあればそれを返す(複数あると、どれか判断できないため null)。</summary>
    public static string? FindSingleUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) return null;
        var urls = UrlPattern().Matches(text).Where(m => !IsTruncated(m.Value)).Select(m => Clean(m.Value)).Where(IsValid).Distinct().ToList();
        return urls.Count == 1 ? urls[0] : null;
    }

    /// <summary>
    /// カーソル周辺の文字列(折り返された複数行を含んでよい)と、その中のカーソル位置(文字数)から、
    /// カーソルに重なっている URL を先頭から末尾まで丸ごと返す。
    /// </summary>
    public static string? UrlAtOffset(string? text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0) return null;
        foreach (Match m in UrlPattern().Matches(text))
        {
            var url = Clean(m.Value);
            if (offset >= m.Index && offset <= m.Index + url.Length)
                return !IsTruncated(m.Value) && IsValid(url) ? url : null;
        }
        return null;
    }

    private static string Clean(string url)
    {
        url = url.TrimEnd('.', ',', ';', ':', '!', '?', ']', '}', '」', '』', '。', '、', '）');
        // 文中の「(https://…)」の閉じかっこは取り除く(URL 内で対になっているかっこは残す)
        while (url.EndsWith(')') && url.Count(c => c == ')') > url.Count(c => c == '('))
            url = url[..^1];
        return url;
    }

    private static bool IsValid(string url) => !IsTruncated(url) && UrlPolicy.TryNormalize(url, out _, out _);
}
