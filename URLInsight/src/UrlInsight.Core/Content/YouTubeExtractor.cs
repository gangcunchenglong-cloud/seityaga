using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UrlInsight.Core.Net;

namespace UrlInsight.Core.Content;

/// <summary>
/// YouTube 動画の情報取得。公式に公開されている手段だけを使う:
///  - oEmbed(キー不要): タイトル・チャンネル名
///  - YouTube Data API v3 videos.list(ユーザーが任意で設定したキー): 説明欄・字幕有無・ライブ/年齢/地域制限
/// 字幕本文は Data API の captions.download が動画所有者の OAuth 認可を必要とするため取得しない。
/// ページのスクレイピングや非公開エンドポイントの利用、制限回避は行わない。
/// </summary>
public static partial class YouTubeExtractor
{
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();

    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "www.youtube-nocookie.com",
    };

    public static bool TryGetVideoId(Uri url, out string videoId)
    {
        videoId = string.Empty;
        if (!Hosts.Contains(url.Host)) return false;
        string? candidate = null;
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (url.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            candidate = segments.FirstOrDefault();
        }
        else if (segments.Length >= 1 && segments[0] == "watch")
        {
            candidate = GetQueryValue(url.Query, "v");
        }
        else if (segments.Length >= 2 && segments[0] is "shorts" or "embed" or "live" or "v")
        {
            candidate = segments[1];
        }
        if (candidate != null && VideoIdPattern().IsMatch(candidate))
        {
            videoId = candidate;
            return true;
        }
        return false;
    }

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == name) return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return null;
    }

    public static async Task<ExtractedContent> ExtractAsync(
        SafeHttpFetcher fetcher, Uri originalUrl, string videoId, string? dataApiKey, CancellationToken ct)
    {
        var watchUrl = new Uri($"https://www.youtube.com/watch?v={videoId}");
        var content = new ExtractedContent { Kind = PageKind.YouTube, Url = watchUrl, SiteName = "YouTube" };

        // oEmbed
        try
        {
            var oembed = new Uri("https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(watchUrl.ToString()));
            var res = await fetcher.FetchAsync(oembed, new FetchOptions(256 * 1024, TimeSpan.FromSeconds(10), 3, "application/json"), ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(res.Body);
            content.Title = TextUtil.SingleLine(GetString(doc.RootElement, "title"), 200);
            var author = GetString(doc.RootElement, "author_name");
            if (!string.IsNullOrWhiteSpace(author)) content.SiteName = $"YouTube ・ {TextUtil.SingleLine(author, 80)}";
        }
        catch (InsightException ex) when (ex.Code is ErrorCode.NotFound or ErrorCode.AccessRestricted or ErrorCode.SiteError)
        {
            // 400/401/404: 非公開・削除・埋め込み不可など
            if (string.IsNullOrEmpty(dataApiKey))
                throw new InsightException(ErrorCode.YouTubeUnavailable, "oembed unavailable", ex);
        }
        catch (JsonException)
        {
        }

        if (!string.IsNullOrEmpty(dataApiKey))
        {
            await FillFromDataApiAsync(fetcher, content, videoId, dataApiKey, ct).ConfigureAwait(false);
        }
        else
        {
            content.Notes.Add("説明欄を使うには、設定の「AI」で YouTube Data API キー(任意)を設定してください");
        }

        content.Notes.Add("字幕本文は取得していません（公式APIでは動画所有者以外は字幕をダウンロードできないため）");
        var (limited, truncated) = TextUtil.LimitBody(content.Text);
        content.Text = limited;
        content.Truncated = truncated;
        content.Quality = content.Text.Length >= 120 ? SourceQuality.Partial : SourceQuality.MetadataOnly;
        content.Warning = ErrorCode.YouTubeNoCaptions;
        return content;
    }

    private static async Task FillFromDataApiAsync(SafeHttpFetcher fetcher, ExtractedContent content, string videoId, string key, CancellationToken ct)
    {
        var api = new Uri("https://www.googleapis.com/youtube/v3/videos?part=snippet,contentDetails,status&id="
                          + Uri.EscapeDataString(videoId) + "&key=" + Uri.EscapeDataString(key));
        FetchResult res;
        try
        {
            res = await fetcher.FetchAsync(api, new FetchOptions(2 * 1024 * 1024, TimeSpan.FromSeconds(15), 0, "application/json"), ct).ConfigureAwait(false);
        }
        catch (InsightException ex) when (ex.Code == ErrorCode.AccessRestricted)
        {
            content.Notes.Add("YouTube Data API キーが無効か、利用枠を超えています");
            return;
        }

        using var doc = JsonDocument.Parse(res.Body);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            throw new InsightException(ErrorCode.YouTubeUnavailable, "video not found");

        var item = items[0];
        if (item.TryGetProperty("snippet", out var snippet))
        {
            content.Title ??= TextUtil.SingleLine(GetString(snippet, "title"), 200);
            var desc = TextUtil.Normalize(GetString(snippet, "description"));
            content.Description = TextUtil.SingleLine(desc, 300);
            content.Text = desc;
            var live = GetString(snippet, "liveBroadcastContent");
            if (live == "live") content.Notes.Add("ライブ配信中の動画です");
            else if (live == "upcoming") content.Notes.Add("配信予定の動画です");
            var channel = GetString(snippet, "channelTitle");
            if (!string.IsNullOrWhiteSpace(channel)) content.SiteName = $"YouTube ・ {TextUtil.SingleLine(channel, 80)}";
        }
        if (item.TryGetProperty("contentDetails", out var details))
        {
            if (GetString(details, "caption") == "true")
                content.Notes.Add("この動画には字幕がありますが、公式APIの制約により本文は取得できません");
            if (details.TryGetProperty("contentRating", out var rating) && GetString(rating, "ytRating") == "ytAgeRestricted")
                content.Notes.Add("年齢制限のある動画です");
            if (details.TryGetProperty("regionRestriction", out _))
                content.Notes.Add("地域制限のある動画です");
        }
        if (item.TryGetProperty("status", out var status) && GetString(status, "privacyStatus") == "private")
            throw new InsightException(ErrorCode.YouTubeUnavailable, "private video");
    }

    private static string? GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
