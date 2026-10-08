using System.Text.Json;
using System.Text.RegularExpressions;
using UrlInsight.Core.Content;

namespace UrlInsight.Core.AI;

/// <summary>
/// API を使わない要約プロバイダ。本文をこの PC 内だけで処理し、外部へは一切送信しない。
/// AI ではなく、本文から重要そうな文を抜き出す方式(抽出型要約)。
/// </summary>
public sealed class LocalSummaryProvider : ISummarizerProvider
{
    public const string ProviderId = "local";
    public string Id => ProviderId;
    public string DisplayName => "APIなし（この PC 内で要約）";
    public string Model => "extractive-v1";
    public string EndpointHost => "この PC 内（外部送信なし）";
    public bool IsTestProvider => false;
    public bool IsLocal => true;

    public Task<string> CompleteAsync(SummaryPrompt prompt, CancellationToken ct)
        => Task.Run(() => JsonSerializer.Serialize(LocalSummarizer.Summarize(prompt.Input, ct)), ct);
}

/// <summary>
/// 抽出型要約の本体。
/// 1. 本文を文に分ける  2. 定型文(Cookie・著作権表示など)を除く
/// 3. 本文中に何度も出てくる語・タイトルの語を多く含む文、前の方にある文を高く評価
/// 4. 似た文を避けながら上位の文を選び、本文の順に並べる
/// 形態素解析の辞書を持たないため、日本語の語は「漢字の連続」「カタカナの連続」で近似する。
/// </summary>
public static partial class LocalSummarizer
{
    public const int MaxSentenceChars = 300;
    public const int MinSentenceChars = 20;
    public const int LineChars = 110;

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}々〆ヵヶ]{2,}|[\p{IsKatakana}ー]{2,}|[A-Za-z][A-Za-z0-9'\-]{2,}|[A-Z][A-Z0-9]+|[Ａ-Ｚａ-ｚ]{2,}")]
    private static partial Regex TermPattern();

    [GeneratedRegex(@"(?<=[。．！？!?])|(?<=[.](?=\s+[A-Z0-9""“(]))")]
    private static partial Regex SentenceEnd();

    private static readonly string[] Boilerplate =
    {
        "cookie", "クッキー", "copyright", "©", "all rights reserved", "無断転載", "利用規約", "プライバシーポリシー",
        "ログイン", "会員登録", "javascript", "シェアする", "ツイート", "subscribe", "sign in", "log in", "広告",
        "このページを印刷", "関連記事", "続きを読む", "read more", "skip to",
    };

    private static readonly HashSet<string> EnglishStopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "any", "can", "her", "was", "one", "our", "out", "his", "has",
        "had", "how", "its", "may", "new", "now", "see", "who", "did", "get", "use", "this", "that", "with", "from", "they",
        "will", "have", "been", "were", "what", "when", "your", "which", "their", "there", "would", "about", "into", "than",
        "then", "them", "these", "those", "also", "more", "most", "some", "such", "only", "other", "over", "after", "before",
    };

    private sealed record Sentence(int Index, string Text, HashSet<string> Terms);

    public static SummaryOutput Summarize(SummaryInput input, CancellationToken ct = default)
    {
        var text = TextUtil.Normalize(input.Text);
        var sentences = SplitSentences(text);
        ct.ThrowIfCancellationRequested();

        // 語の出現回数(文単位で数える)
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in sentences)
            foreach (var t in s.Terms)
                freq[t] = freq.GetValueOrDefault(t) + 1;
        var titleTerms = Terms(input.Title ?? string.Empty);

        var scored = sentences
            .Select(s => (s, score: Score(s, freq, titleTerms, sentences.Count)))
            .OrderByDescending(x => x.score)
            .ToList();
        ct.ThrowIfCancellationRequested();

        var lines = new List<(int index, string text)>();
        var description = CleanDescription(input.Description, input.Title);
        if (description != null) lines.Add((-1, description));

        int target = (text.Length < 1500 ? 3 : 4) + (description != null ? 1 : 0);
        target = Math.Min(target, SummaryValidator.MaxLines);
        foreach (var (s, score) in scored)
        {
            if (lines.Count >= target) break;
            if (score <= 0 && lines.Count > 0) break;
            if (lines.Any(l => Similar(l.text, s.Text))) continue;
            lines.Add((s.Index, s.Text));
        }

        // 文として使えるものが無い場合は本文の冒頭をそのまま見せる
        if (lines.Count == 0)
        {
            var head = TextUtil.SingleLine(text, LineChars);
            lines.Add((0, head.Length > 0 ? head : "本文から要約に使える文を見つけられませんでした。"));
        }

        var output = new SummaryOutput
        {
            Title = TextUtil.SingleLine(string.IsNullOrWhiteSpace(input.Title) ? input.Url.Host : input.Title, 60),
            SummaryLines = lines.OrderBy(l => l.index).Select(l => TextUtil.Truncate(l.text, LineChars)).ToList(),
            KeyPoints = KeyPoints(text, freq),
            Confidence = "medium",
        };
        return output;
    }

    private static List<Sentence> SplitSentences(string text)
    {
        var result = new List<Sentence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var raw in SentenceEnd().Split(paragraph))
            {
                var s = raw.Trim();
                if (!IsUsable(s) || !seen.Add(s)) continue;
                result.Add(new Sentence(result.Count, s, Terms(s)));
            }
        }
        return result;
    }

    private static bool IsUsable(string s)
    {
        if (s.Length < MinSentenceChars || s.Length > MaxSentenceChars) return false;
        if (s.Contains("http", StringComparison.OrdinalIgnoreCase) || s.Contains('|') || s.Contains('｜')) return false;
        var lower = s.ToLowerInvariant();
        if (Boilerplate.Any(b => lower.Contains(b, StringComparison.Ordinal))) return false;
        // 記号や数字ばかりの行(メニュー・表の断片など)は除く
        int letters = s.Count(char.IsLetter);
        return letters >= s.Length * 0.6;
    }

    private static HashSet<string> Terms(string s)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TermPattern().Matches(s))
        {
            var t = m.Value.ToLowerInvariant();
            if (EnglishStopWords.Contains(t)) continue;
            set.Add(t);
        }
        return set;
    }

    private static double Score(Sentence s, Dictionary<string, int> freq, HashSet<string> titleTerms, int total)
    {
        if (s.Terms.Count == 0) return 0;
        double score = 0;
        foreach (var t in s.Terms)
        {
            int f = freq.GetValueOrDefault(t);
            if (f >= 2) score += Math.Log(f) * (t.Length >= 3 ? 1.2 : 1.0);
            if (titleTerms.Contains(t)) score += 1.5;
        }
        // 長い文ほど語が多く有利になりすぎないようにする
        score /= Math.Sqrt(Math.Max(s.Terms.Count, 4));
        // 記事は冒頭に要点が書かれることが多いので、前の方の文を少し優先する
        double position = total <= 1 ? 1 : 1.0 + 0.8 / (1.0 + s.Index / 5.0);
        return score * position;
    }

    private static string? CleanDescription(string? description, string? title)
    {
        var d = TextUtil.SingleLine(description, MaxSentenceChars);
        if (d.Length < MinSentenceChars) return null;
        if (!string.IsNullOrWhiteSpace(title) && Similar(d, title)) return null;
        var lower = d.ToLowerInvariant();
        return Boilerplate.Any(b => lower.Contains(b, StringComparison.Ordinal)) ? null : d;
    }

    /// <summary>文字2-gram の重なりが大きい文は同じ内容とみなす。</summary>
    private static bool Similar(string a, string b)
    {
        var ga = Bigrams(a);
        var gb = Bigrams(b);
        if (ga.Count == 0 || gb.Count == 0) return false;
        int common = ga.Count(gb.Contains);
        return (double)common / Math.Min(ga.Count, gb.Count) > 0.6;
    }

    private static HashSet<string> Bigrams(string s)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var t = new string(s.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray()).ToLowerInvariant();
        for (int i = 0; i + 1 < t.Length; i++) set.Add(t.Substring(i, 2));
        return set;
    }

    private static List<string> KeyPoints(string text, Dictionary<string, int> freq)
    {
        var points = new List<string>();
        // 複数の文に出てくる語を、長い語を優先して選ぶ(他の語に含まれる短い語は省く)
        var keywords = new List<string>();
        foreach (var (term, _) in freq.Where(kv => kv.Value >= 2)
                     .OrderByDescending(kv => kv.Value * Math.Min(kv.Key.Length, 6)).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (keywords.Any(k => k.Contains(term, StringComparison.Ordinal) || term.Contains(k, StringComparison.Ordinal))) continue;
            keywords.Add(term);
            if (keywords.Count >= 5) break;
        }
        if (keywords.Count > 0) points.Add(TextUtil.Truncate("よく出てくる語: " + string.Join("、", keywords), 100));

        int cjk = text.Count(c => c >= '぀' && c <= '鿿');
        if (cjk * 3 >= text.Length)
        {
            int minutes = Math.Max(1, (int)Math.Round(text.Length / 500.0));
            points.Add($"本文の長さ: 約{text.Length:N0}文字（読むのに約{minutes}分）");
        }
        else
        {
            int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            int minutes = Math.Max(1, (int)Math.Round(words / 200.0));
            points.Add($"本文の長さ: 約{words:N0}語（読むのに約{minutes}分）");
        }
        return points;
    }
}
