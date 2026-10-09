namespace UrlInsight.Core.AI;

/// <summary>ブラウザのサイドバーの AI(Chrome の Gemini / Edge の Copilot)に入力する依頼文。</summary>
public static class SidebarPrompt
{
    /// <summary>依頼文を作る。改行を入れると入力の途中で送信されてしまうため、必ず1行にする。</summary>
    public static string Build(IReadOnlyList<string> urls)
    {
        var clean = urls.Select(u => u.Replace('\r', ' ').Replace('\n', ' ').Trim()).Where(u => u.Length > 0).ToList();
        if (clean.Count == 0) throw new ArgumentException("urls is empty", nameof(urls));
        return clean.Count == 1
            ? $"次のリンク先のページの内容を、日本語で3〜5文に要約してください: {clean[0]}"
            : $"次の{clean.Count}件のリンク先のページの内容を、それぞれ日本語で3〜5文に要約してください（リンクごとに見出しを付けてください）: "
              + string.Join(" ", clean.Select((u, i) => $"{i + 1}. {u}"));
    }
}
