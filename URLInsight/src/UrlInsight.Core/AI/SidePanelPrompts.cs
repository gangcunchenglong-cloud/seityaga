namespace UrlInsight.Core.AI;

/// <summary>
/// Chrome のサイドバーの AI(ChatGPT / Claude in Chrome)へ入力する依頼文。
/// 入力欄では改行(Enter)で送信されてしまうため、何件あっても1行にする。
/// </summary>
public static class SidePanelPrompts
{
    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Numbered(IReadOnlyList<string> urls)
        => string.Join(" ", urls.Select((u, i) => $"({i + 1}) {OneLine(u)}"));

    /// <summary>リンク先の要約を頼む依頼文(ChatGPT 用)。</summary>
    public static string Summarize(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0) throw new ArgumentException("no urls", nameof(urls));
        return urls.Count == 1
            ? $"次のリンク先のページの内容を、日本語で3〜5文に要約してください: {OneLine(urls[0])}"
            : $"次の{urls.Count}件のリンク先のページの内容を、それぞれ日本語で3〜5文に要約してください。リンクごとに番号とページの題名を見出しにしてください: {Numbered(urls)}";
    }

    /// <summary>
    /// 画面の部品の名前が、AI の名前(「ChatGPT」など)を単語として含むか。
    /// 「ChatGPTs」の中の「ChatGPT」のような、別の単語の一部には一致させない。
    /// </summary>
    public static bool NameHasWord(string? name, string word)
        => !string.IsNullOrEmpty(name) && System.Text.RegularExpressions.Regex.IsMatch(name,
            $"(?<![A-Za-z]){System.Text.RegularExpressions.Regex.Escape(word)}(?![A-Za-z])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Web 版に付ける依頼文の上限(URL エンコード後の文字数。長すぎる URL はサーバーに断られることがあるため)。</summary>
    public const int MaxEncodedQuery = 6000;

    /// <summary>
    /// Web 版(chatgpt.com/?q= など)を依頼文付きで開く URL と、その依頼文。
    /// 依頼文が長くなりすぎるときは、リンクを数件ずつに分けて、複数の URL にする(どのリンクも落とさない)。
    /// </summary>
    public static List<(string Url, string Prompt)> WebQueryUrls(string prefix, IReadOnlyList<string> urls,
        Func<IReadOnlyList<string>, string> build, int maxEncoded = MaxEncodedQuery)
    {
        if (urls.Count == 0) throw new ArgumentException("no urls", nameof(urls));
        var result = new List<(string, string)>();
        var chunk = new List<string>();
        foreach (var u in urls)
        {
            chunk.Add(u);
            if (chunk.Count > 1 && Uri.EscapeDataString(build(chunk)).Length > maxEncoded)
            {
                chunk.RemoveAt(chunk.Count - 1);
                var p = build(chunk);
                result.Add((prefix + Uri.EscapeDataString(p), p));
                chunk = new List<string> { u };
            }
        }
        var last = build(chunk);
        result.Add((prefix + Uri.EscapeDataString(last), last));
        return result;
    }

    public static string Claude(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0) throw new ArgumentException("no urls", nameof(urls));
        return urls.Count == 1
            ? $"次のリンク先のページを開いて内容を読み、日本語で3〜5文に要約してください: {OneLine(urls[0])}"
            : $"次の{urls.Count}件のリンク先のページをそれぞれ開いて内容を読み、日本語で3〜5文ずつ要約してください。リンクごとに番号とページの題名を見出しにしてください: {Numbered(urls)}";
    }
}
