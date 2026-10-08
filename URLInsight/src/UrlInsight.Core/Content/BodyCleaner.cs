namespace UrlInsight.Core.Content;

/// <summary>
/// AI へ渡す前に、本文から要約の邪魔になる行(メニュー・Cookie 表示・著作権表示・共有ボタン・関連記事など)を取り除く。
/// ノイズが減るぶん、送信上限の文字数の中に本来の本文を多く入れられ、AI が無関係な部分を要約に混ぜにくくなる。
/// 本文の文そのものは書き換えない(行単位で残すか捨てるかだけを決める)。
/// </summary>
public static class BodyCleaner
{
    /// <summary>この長さ未満の行に限り、定型文のキーワードで取り除く(長い文は本文の可能性があるため残す)。</summary>
    public const int BoilerplateMaxChars = 80;
    /// <summary>メニューやタグ一覧とみなす短い行の長さ。</summary>
    public const int ShortLineChars = 15;

    private static readonly string[] BoilerplateWords =
    {
        "cookie", "クッキー", "copyright", "©", "all rights reserved", "無断転載", "無断複製",
        "シェアする", "ツイート", "ポストする", "いいね", "ブックマーク", "はてブ", "line で送る", "lineで送る",
        "ログイン", "会員登録", "新規登録", "利用規約", "プライバシーポリシー", "個人情報の取り扱い",
        "関連記事", "おすすめ記事", "人気記事", "あわせて読みたい", "ランキング", "続きを読む", "もっと見る", "一覧へ",
        "広告", "スポンサー", "javascript", "skip to", "subscribe", "sign in", "sign up", "log in", "accept all",
        "share on", "related articles", "read more", "advertisement",
    };

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = TextUtil.Normalize(text).Split('\n');
        var keep = new bool[lines.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) { keep[i] = true; continue; }
            // 同じ行の繰り返し(ページ上部と下部のメニューなど)は最初の1回だけ残す
            if (!seen.Add(line)) continue;
            if (line.Length < BoilerplateMaxChars && IsBoilerplate(line)) continue;
            // 文字も数字も無い行(区切り線・矢印・記号だけの行)は捨てる。価格や日付だけの行は残す
            if (!line.Any(char.IsLetterOrDigit)) continue;
            keep[i] = true;
        }

        // 文の終わりが無い短い行が3行以上続く部分は、メニュー・タグ・パンくずとみなして取り除く
        int run = 0;
        for (int i = 0; i <= lines.Length; i++)
        {
            bool shortLine = i < lines.Length && keep[i] && lines[i].Length > 0 && IsMenuLike(lines[i]);
            bool blankInRun = i < lines.Length && lines[i].Length == 0 && run > 0;
            if (shortLine) { run++; continue; }
            if (blankInRun) continue;
            if (run >= 3)
            {
                for (int j = i - 1, removed = 0; j >= 0 && removed < run; j--)
                {
                    if (lines[j].Length == 0) continue;
                    keep[j] = false;
                    removed++;
                }
            }
            run = 0;
        }

        var result = TextUtil.Normalize(string.Join('\n', lines.Where((_, i) => keep[i])));
        // 取り除きすぎて何も残らない場合は元の本文を使う
        return result.Length > 0 ? result : TextUtil.Normalize(text);
    }

    private static bool IsBoilerplate(string line)
    {
        var lower = line.ToLowerInvariant();
        return BoilerplateWords.Any(w => lower.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>数字や「：」を含む行(仕様・料金・日時の箇条書きなど)は本文の情報なので対象外。</summary>
    private static bool IsMenuLike(string line)
        => line.Length <= ShortLineChars && !"。．.!?！？」』）)".Contains(line[^1])
           && !line.Any(char.IsDigit) && !line.Contains(':') && !line.Contains('：');
}
