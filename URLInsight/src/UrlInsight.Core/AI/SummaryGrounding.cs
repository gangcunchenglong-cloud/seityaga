using System.Text;
using System.Text.RegularExpressions;

namespace UrlInsight.Core.AI;

/// <summary>
/// 要約の裏付け確認。AI が本文に無い数値(日付・金額・割合など)を書いていないかを機械的に調べる。
/// 数値の誤りは読み手が気づきにくく、要約の誤りの中でも影響が大きいため、ここだけは本文と突き合わせる。
/// </summary>
public static partial class SummaryGrounding
{
    /// <summary>2桁以上の数値だけを確認する(「3つの理由」のような1桁の数は言い換えで生じやすいため対象外)。</summary>
    public const int MinDigits = 2;

    [GeneratedRegex(@"\d(?:[\d,]*\d)?(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    /// <summary>要約・重要ポイント・タイトルに出てくる数値のうち、本文・タイトル・説明のどこにも無いものを返す。</summary>
    public static List<string> FindUnsupportedNumbers(SummaryOutput output, SummaryInput input)
    {
        var source = Canonical(string.Join('\n', input.Title, input.Description, input.Text));
        var result = new List<string>();
        var texts = output.SummaryLines.Concat(output.KeyPoints).Append(output.Title);
        foreach (var text in texts)
        {
            foreach (Match m in NumberPattern().Matches(Canonical(text)))
            {
                var number = m.Value;
                if (number.Count(char.IsDigit) < MinDigits || result.Contains(number)) continue;
                if (!ContainsNumber(source, number)) result.Add(number);
            }
        }
        return result;
    }

    /// <summary>全角数字を半角にし、桁区切りのカンマを取り除く(「１，２００」と「1200」を同じとみなす)。</summary>
    internal static string Canonical(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var t = s.Normalize(NormalizationForm.FormKC);
        return DigitComma().Replace(t, "");
    }

    [GeneratedRegex(@"(?<=\d),(?=\d)")]
    private static partial Regex DigitComma();

    /// <summary>「12」が「2012」の一部として見つかるような誤一致を避け、数値として一致するかを調べる。</summary>
    private static bool ContainsNumber(string source, string number)
    {
        int start = 0;
        while ((start = source.IndexOf(number, start, StringComparison.Ordinal)) >= 0)
        {
            int end = start + number.Length;
            bool leftOk = start == 0 || !char.IsDigit(source[start - 1]);
            bool rightOk = end >= source.Length || !char.IsDigit(source[end]);
            if (leftOk && rightOk) return true;
            start = end;
        }
        return false;
    }
}
