using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace UrlInsight.Core.Content;

/// <summary>
/// HTML からメタ情報と本文を取り出す。スクリプトは実行しない(AngleSharp は既定でスクリプト無効)。
/// 本文抽出は SmartReader(Mozilla Readability の .NET 移植)を優先し、失敗時は簡易ヒューリスティックへ。
/// </summary>
public static partial class HtmlExtractor
{
    static HtmlExtractor()
    {
        // Shift_JIS / EUC-JP 等の日本語サイトに対応
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?\s*([A-Za-z0-9_\-:.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharset();

    public static string Decode(byte[] body, string? headerCharset)
    {
        Encoding? enc = null;
        // BOM
        if (body.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF) enc = Encoding.UTF8;
        else if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE) enc = Encoding.Unicode;
        else if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF) enc = Encoding.BigEndianUnicode;

        enc ??= TryGetEncoding(headerCharset);
        if (enc == null)
        {
            var head = Encoding.ASCII.GetString(body, 0, Math.Min(body.Length, 4096));
            var m = MetaCharset().Match(head);
            if (m.Success) enc = TryGetEncoding(m.Groups[1].Value);
        }
        enc ??= Encoding.UTF8;
        var text = enc.GetString(body);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static Encoding? TryGetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return Encoding.GetEncoding(name.Trim()); }
        catch (ArgumentException) { return null; }
    }

    public static ExtractedContent Extract(string html, Uri url)
    {
        var parser = new HtmlParser();
        using var doc = parser.ParseDocument(html);

        string? Meta(string attr, string key)
            => doc.QuerySelectorAll($"meta[{attr}]")
                  .FirstOrDefault(m => string.Equals(m.GetAttribute(attr), key, StringComparison.OrdinalIgnoreCase))
                  ?.GetAttribute("content");

        var title = FirstNonEmpty(Meta("property", "og:title"), Meta("name", "twitter:title"), doc.Title);
        var description = FirstNonEmpty(Meta("name", "description"), Meta("property", "og:description"), Meta("name", "twitter:description"));
        var siteName = FirstNonEmpty(Meta("property", "og:site_name"), Meta("name", "application-name"));
        var (ldHeadline, ldDescription) = ReadJsonLd(doc);
        title = FirstNonEmpty(title, ldHeadline);
        description = FirstNonEmpty(description, ldDescription);

        string body = string.Empty;
        // 極端に深い/大きい DOM は Readability 系の処理が非常に遅くなるため(悪意あるページ対策)、簡易抽出のみにする
        bool pathological = html.Length > MaxHtmlCharsForReader || IsPathological(doc);
        if (!pathological)
        {
            try
            {
                var reader = new SmartReader.Reader(url.ToString(), html);
                var article = reader.GetArticle();
                if (article.IsReadable && !string.IsNullOrWhiteSpace(article.TextContent))
                    body = TextUtil.Normalize(article.TextContent);
            }
            catch (Exception)
            {
                // フォールバックへ
            }
        }

        if (body.Length < 200)
        {
            var fallback = pathological ? IterativeText(doc) : HeuristicBody(doc);
            if (fallback.Length > body.Length) body = fallback;
        }

        var (limited, truncated) = TextUtil.LimitBody(body);
        var content = new ExtractedContent
        {
            Kind = PageKind.Web,
            Url = url,
            Title = TextUtil.SingleLine(title, 200),
            Description = TextUtil.SingleLine(description, 500),
            SiteName = TextUtil.SingleLine(siteName, 100),
            Text = limited,
            Truncated = truncated,
            Quality = ExtractedContent.QualityFor(limited.Length),
        };
        if (truncated) content.Notes.Add($"本文が長いため先頭 {TextUtil.MaxBodyChars:N0} 文字までを対象にしました");
        if (content.Quality != SourceQuality.FullText && doc.QuerySelector("input[type=password]") != null)
            content.Notes.Add("ログインが必要なページの可能性があります");
        return content;
    }

    public const int MaxHtmlCharsForReader = 3_000_000;
    public const int MaxDomDepth = 200;
    public const int MaxDomElements = 40_000;

    /// <summary>DOM の深さと要素数を反復(非再帰)で調べる。</summary>
    internal static bool IsPathological(IDocument doc)
    {
        if (doc.DocumentElement == null) return false;
        var stack = new Stack<(IElement el, int depth)>();
        stack.Push((doc.DocumentElement, 1));
        int count = 0;
        while (stack.Count > 0)
        {
            var (el, depth) = stack.Pop();
            if (++count > MaxDomElements || depth > MaxDomDepth) return true;
            foreach (var child in el.Children) stack.Push((child, depth + 1));
        }
        return false;
    }

    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "iframe", "nav", "header", "footer", "aside", "form", "button", "head",
    };

    /// <summary>再帰を使わずに本文テキストを集める(深い DOM でもスタックを消費しない)。</summary>
    internal static string IterativeText(IDocument doc)
    {
        var root = (INode?)doc.Body ?? doc.DocumentElement;
        if (root == null) return string.Empty;
        var sb = new StringBuilder();
        var stack = new Stack<INode>();
        stack.Push(root);
        while (stack.Count > 0 && sb.Length < TextUtil.MaxBodyChars * 2)
        {
            var node = stack.Pop();
            if (node is IElement el)
            {
                if (SkipTags.Contains(el.LocalName)) continue;
                if (el.LocalName is "p" or "div" or "li" or "br" or "h1" or "h2" or "h3" or "tr") sb.Append('\n');
            }
            else if (node.NodeType == NodeType.Text)
            {
                sb.Append(node.NodeValue).Append(' ');
                continue;
            }
            for (int i = node.ChildNodes.Length - 1; i >= 0; i--) stack.Push(node.ChildNodes[i]);
        }
        return TextUtil.Normalize(sb.ToString());
    }

    private static string HeuristicBody(IDocument doc)
    {
        foreach (var el in doc.QuerySelectorAll("script,style,noscript,template,svg,iframe,nav,header,footer,aside,form,button").ToList())
            el.Remove();
        var root = doc.QuerySelector("article") ?? doc.QuerySelector("main") ?? doc.QuerySelector("[role=main]") ?? doc.Body;
        if (root == null) return string.Empty;
        var blocks = root.QuerySelectorAll("h1,h2,h3,p,li,blockquote,pre,td")
            .Select(e => TextUtil.SingleLine(e.TextContent, 5000))
            .Where(t => t.Length >= 2)
            .ToList();
        var text = blocks.Count > 0 ? string.Join('\n', blocks) : TextUtil.Normalize(root.TextContent);
        return TextUtil.Normalize(text);
    }

    private static (string? headline, string? description) ReadJsonLd(IDocument doc)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var json = script.TextContent;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 200_000) continue;
            try
            {
                using var jd = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, MaxDepth = 32 });
                var found = FindLd(jd.RootElement);
                if (found.headline != null || found.description != null) return found;
            }
            catch (JsonException)
            {
            }
        }
        return (null, null);
    }

    private static (string? headline, string? description) FindLd(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
            {
                var r = FindLd(item);
                if (r.headline != null || r.description != null) return r;
            }
            return (null, null);
        }
        if (e.ValueKind != JsonValueKind.Object) return (null, null);
        if (e.TryGetProperty("@graph", out var graph)) return FindLd(graph);
        string? Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return (Str("headline") ?? Str("name"), Str("description"));
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
