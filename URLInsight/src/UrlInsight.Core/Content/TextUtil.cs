using System.Text;
using System.Text.RegularExpressions;

namespace UrlInsight.Core.Content;

public static partial class TextUtil
{
    public const int MaxBodyChars = 50_000;

    [GeneratedRegex(@"[ \t\f\v 　]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();

    /// <summary>空白を整理し、制御文字を取り除く。</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\r') continue;
            if (ch == '\n' || ch == '\t' || !char.IsControl(ch)) sb.Append(ch);
        }
        var t = Spaces().Replace(sb.ToString(), " ");
        var lines = t.Split('\n').Select(l => l.Trim());
        t = string.Join('\n', lines);
        t = ManyNewlines().Replace(t, "\n\n");
        return t.Trim();
    }

    /// <summary>1行用: 改行も空白にまとめる。</summary>
    public static string SingleLine(string? s, int maxLength)
    {
        var t = Normalize(s).Replace('\n', ' ');
        return Truncate(t, maxLength);
    }

    public static string Truncate(string s, int maxLength)
    {
        if (s.Length <= maxLength) return s;
        // サロゲートペアの途中で切らない
        int cut = maxLength - 1;
        if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut] + "…";
    }

    public static (string text, bool truncated) LimitBody(string text, int max = MaxBodyChars)
    {
        if (text.Length <= max) return (text, false);
        int cut = max;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return (text[..cut], true);
    }
}
