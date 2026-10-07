using System.Text.RegularExpressions;

namespace UrlInsight.Core.Diagnostics;

/// <summary>
/// ログ・診断情報から秘密情報と URL の詳細を取り除く。
/// URL はスキームとホストのみ残し、パス・クエリを伏せる。API キーらしき文字列は *** に置換する。
/// </summary>
public static partial class Redactor
{
    [GeneratedRegex(@"\b(https?)://([A-Za-z0-9.\-\[\]:]+?)(:\d+)?(/[^\s""'<>]*)?(?=[\s""'<>]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(sk-ant-[A-Za-z0-9_\-]{8,}|sk-[A-Za-z0-9_\-]{12,}|gsk_[A-Za-z0-9]{12,}|AIza[0-9A-Za-z_\-]{20,}|xai-[A-Za-z0-9]{12,}|hf_[A-Za-z0-9]{12,})")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"(?i)(bearer\s+|x-api-key[:=]\s*|api[_-]?key[""']?\s*[:=]\s*[""']?|[?&]key=)[^\s&""',]+")]
    private static partial Regex LabeledSecretPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var t = LabeledSecretPattern().Replace(text, m => m.Groups[1].Value + "***");
        t = KeyPattern().Replace(t, "***");
        t = UrlPattern().Replace(t, m => $"{m.Groups[1].Value}://{m.Groups[2].Value}/…");
        return t;
    }

    /// <summary>伏せ字処理後に残ってはいけないものが含まれていないかの自動検査。</summary>
    public static bool ContainsSensitive(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (KeyPattern().IsMatch(text)) return true;
        foreach (Match m in UrlPattern().Matches(text))
        {
            var path = m.Groups[4].Value;
            if (path.Length > 0 && path != "/" && path != "/…") return true;
        }
        if (LabeledSecretPattern().Matches(text).Any(m => !m.Value.EndsWith("***", StringComparison.Ordinal))) return true;
        return false;
    }
}
