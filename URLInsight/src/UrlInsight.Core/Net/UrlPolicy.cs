using System.Net;
using System.Text;

namespace UrlInsight.Core.Net;

/// <summary>
/// URL の検証と正規化。http/https 以外、資格情報付きURL、明らかなローカル宛てURLを拒否する。
/// 追跡パラメータは「除去しても内容が変わらないことが一般に確認できるもの」(utm_* 等) だけ除去する。
/// </summary>
public static class UrlPolicy
{
    public const int MaxUrlLength = 4096;

    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "mc_eid", "igshid",
    };

    public static bool TryNormalize(string? input, out Uri? normalized, out ErrorCode error)
    {
        normalized = null;
        error = ErrorCode.UnsupportedUrl;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        if (input.Length > MaxUrlLength) return false;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;
        if (string.IsNullOrEmpty(u.Host)) return false;

        string host;
        switch (u.HostNameType)
        {
            case UriHostNameType.Dns:
                host = u.IdnHost.ToLowerInvariant().TrimEnd('.');
                if (host.Length == 0) return false;
                if (IsLocalHostName(host))
                {
                    error = ErrorCode.BlockedAddress;
                    return false;
                }
                break;
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
                if (!IPAddress.TryParse(u.DnsSafeHost, out var ip) || !IpGuard.IsPublic(ip))
                {
                    error = ErrorCode.BlockedAddress;
                    return false;
                }
                host = u.Host.ToLowerInvariant();
                break;
            default:
                return false;
        }

        var sb = new StringBuilder(input.Length);
        sb.Append(u.Scheme).Append("://").Append(host);
        if (!u.IsDefaultPort) sb.Append(':').Append(u.Port);
        sb.Append(string.IsNullOrEmpty(u.AbsolutePath) ? "/" : u.AbsolutePath);
        var query = FilterQuery(u.Query);
        if (query.Length > 0) sb.Append('?').Append(query);

        if (!Uri.TryCreate(sb.ToString(), UriKind.Absolute, out var result)) return false;
        normalized = result;
        error = ErrorCode.None;
        return true;
    }

    public static bool IsLocalHostName(string host)
    {
        host = host.ToLowerInvariant().TrimEnd('.');
        if (host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)) return true;
        if (host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)
            || host.EndsWith(".lan", StringComparison.Ordinal) || host.EndsWith(".home.arpa", StringComparison.Ordinal)) return true;
        // ドットの無い単一ラベル名(イントラネット名)は対象外
        if (!host.Contains('.')) return true;
        return false;
    }

    internal static string FilterQuery(string rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery)) return string.Empty;
        var q = rawQuery[0] == '?' ? rawQuery[1..] : rawQuery;
        if (q.Length == 0) return string.Empty;
        var kept = new List<string>();
        foreach (var part in q.Split('&'))
        {
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=');
            var name = eq >= 0 ? part[..eq] : part;
            if (name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;
            if (TrackingParams.Contains(name)) continue;
            kept.Add(part);
        }
        return string.Join('&', kept);
    }

    /// <summary>ログ表示用。パス・クエリを伏せ、スキームとホストのみ残す。</summary>
    public static string RedactForLog(Uri? uri)
        => uri == null ? "(none)" : $"{uri.Scheme}://{uri.Host}/…";

    public static string DisplayDomain(Uri uri)
    {
        var host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
