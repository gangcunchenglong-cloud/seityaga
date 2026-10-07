using System.Net;
using System.Net.Sockets;

namespace UrlInsight.Core.Net;

/// <summary>
/// SSRF 対策: 接続先 IP がインターネット上の公開ユニキャストアドレスかを判定する。
/// localhost、プライベート、リンクローカル、CGNAT、予約・文書用、マルチキャスト、
/// IPv4-mapped / NAT64 / 6to4 に埋め込まれた非公開 IPv4 をすべて拒否する。
/// </summary>
public static class IpGuard
{
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return IsPublicV4(ip.GetAddressBytes());

        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;

        var b = ip.GetAddressBytes();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None)) return false;
        if ((b[0] & 0xFE) == 0xFC) return false;                       // fc00::/7 ユニークローカル
        if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return false;       // fe80::/10 リンクローカル
        if (b[0] == 0xFE && (b[1] & 0xC0) == 0xC0) return false;       // fec0::/10 サイトローカル(廃止)
        if (b[0] == 0xFF) return false;                                // マルチキャスト
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32 文書用
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) return false; // 2001::/32 Teredo
        if (b[0] == 0x20 && b[1] == 0x02) return IsPublicV4(b[2..6]);  // 2002::/16 6to4
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && AllZero(b, 4, 12))
            return IsPublicV4(b[12..16]);                              // 64:ff9b::/96 NAT64
        if (AllZero(b, 0, 12)) return false;                           // ::/96 IPv4互換(廃止)
        if (b[0] == 0x01 && b[1] == 0x00 && AllZero(b, 2, 8)) return false; // 100::/64 破棄用
        return (b[0] & 0xE0) == 0x20;                                  // 2000::/3 グローバルユニキャストのみ
    }

    private static bool AllZero(byte[] b, int start, int endExclusive)
    {
        for (int i = start; i < endExclusive; i++) if (b[i] != 0) return false;
        return true;
    }

    internal static bool IsPublicV4(byte[] b)
    {
        if (b.Length != 4) return false;
        return !(
            b[0] == 0 ||                                            // 0.0.0.0/8
            b[0] == 10 ||                                           // 10/8
            (b[0] == 100 && (b[1] & 0xC0) == 64) ||                 // 100.64/10 CGNAT
            b[0] == 127 ||                                          // ループバック
            (b[0] == 169 && b[1] == 254) ||                         // リンクローカル
            (b[0] == 172 && (b[1] & 0xF0) == 16) ||                 // 172.16/12
            (b[0] == 192 && b[1] == 0 && b[2] == 0) ||              // 192.0.0/24
            (b[0] == 192 && b[1] == 0 && b[2] == 2) ||              // TEST-NET-1
            (b[0] == 192 && b[1] == 88 && b[2] == 99) ||            // 6to4 relay
            (b[0] == 192 && b[1] == 168) ||                         // 192.168/16
            (b[0] == 198 && (b[1] & 0xFE) == 18) ||                 // 198.18/15 ベンチマーク
            (b[0] == 198 && b[1] == 51 && b[2] == 100) ||           // TEST-NET-2
            (b[0] == 203 && b[1] == 0 && b[2] == 113) ||            // TEST-NET-3
            b[0] >= 224                                             // マルチキャスト・予約・ブロードキャスト
        );
    }
}
