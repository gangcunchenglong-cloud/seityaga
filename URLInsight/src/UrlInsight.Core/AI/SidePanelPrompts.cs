namespace UrlInsight.Core.AI;

/// <summary>
/// Chrome のサイドバーの AI(Gemini in Chrome / Claude in Chrome)へ入力する依頼文。
/// 入力欄では改行(Enter)で送信されてしまうため、何件あっても1行にする。
/// </summary>
public static class SidePanelPrompts
{
    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Numbered(IReadOnlyList<string> urls)
        => string.Join(" ", urls.Select((u, i) => $"({i + 1}) {OneLine(u)}"));

    public static string Gemini(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0) throw new ArgumentException("no urls", nameof(urls));
        return urls.Count == 1
            ? $"次のリンク先のページの内容を、日本語で3〜5文に要約してください: {OneLine(urls[0])}"
            : $"次の{urls.Count}件のリンク先のページの内容を、それぞれ日本語で3〜5文に要約してください。リンクごとに番号とページの題名を見出しにしてください: {Numbered(urls)}";
    }

    public static string Claude(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0) throw new ArgumentException("no urls", nameof(urls));
        return urls.Count == 1
            ? $"次のリンク先のページを開いて内容を読み、日本語で3〜5文に要約してください: {OneLine(urls[0])}"
            : $"次の{urls.Count}件のリンク先のページをそれぞれ開いて内容を読み、日本語で3〜5文ずつ要約してください。リンクごとに番号とページの題名を見出しにしてください: {Numbered(urls)}";
    }
}
