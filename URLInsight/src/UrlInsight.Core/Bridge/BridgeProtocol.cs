using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UrlInsight.Core.Bridge;

/// <summary>
/// Chrome Native Messaging のフレーミング(4バイト リトルエンディアン長 + UTF-8 JSON)。
/// アプリ内の名前付きパイプでも同じ形式を使う。
/// </summary>
public static class MessageFraming
{
    /// <summary>ブラウザ → アプリ方向の上限(本アプリのメッセージは小さいので 64KB に制限)。</summary>
    public const int MaxInboundBytes = 64 * 1024;
    /// <summary>アプリ → ブラウザ方向の上限(Chrome の仕様上限は 1MB)。</summary>
    public const int MaxOutboundBytes = 1024 * 1024;

    public static async Task<byte[]?> ReadAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var header = new byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = await stream.ReadAsync(header.AsMemory(read, 4 - read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0) return null; // 正常終了
                throw new EndOfStreamException("truncated header");
            }
            read += n;
        }
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > maxBytes) throw new InvalidDataException($"message too large: {length}");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        return payload;
    }

    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        if (payload.Length > MaxOutboundBytes) throw new InvalidDataException("outbound message too large");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>ブラウザ拡張から届くメッセージ(すべて不信データとして検証する)。</summary>
public sealed class BrowserMessage
{
    public const int SchemaVersion = 1;
    public required string Type { get; init; }
    public string? RequestId { get; init; }
    public string? Url { get; init; }
    public string? PageTitle { get; init; }
    public string? LinkText { get; init; }
    public string? Browser { get; init; }
    public string? TabId { get; init; }
    public string? ExtensionVersion { get; init; }
}

public static partial class BrowserMessageParser
{
    public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>
    {
        "hello", "hoverLink", "hoverEnd", "dismiss", "ping", "app.activate", "app.showManual",
    };

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SimpleTokenPattern();

    public static bool TryParse(ReadOnlySpan<byte> json, out BrowserMessage? message, out string error)
    {
        message = null;
        error = string.Empty;
        if (json.Length == 0 || json.Length > MessageFraming.MaxInboundBytes)
        {
            error = "size";
            return false;
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            error = "json";
            return false;
        }
        if (node is not JsonObject obj)
        {
            error = "shape";
            return false;
        }

        if (!TryGetInt(obj, "schemaVersion", out var version) || version != BrowserMessage.SchemaVersion)
        {
            error = "version";
            return false;
        }
        var type = GetString(obj, "type");
        if (type == null || !KnownTypes.Contains(type))
        {
            error = "type";
            return false;
        }

        var requestId = GetString(obj, "requestId");
        if (type is "hoverLink" or "hoverEnd")
        {
            if (requestId == null || !RequestIdPattern().IsMatch(requestId))
            {
                error = "requestId";
                return false;
            }
        }
        else if (requestId != null && !RequestIdPattern().IsMatch(requestId))
        {
            requestId = null;
        }

        string? url = null;
        if (type == "hoverLink")
        {
            url = GetString(obj, "url");
            if (url == null || url.Length > Net.UrlPolicy.MaxUrlLength)
            {
                error = "url";
                return false;
            }
        }

        string? Token(string name)
        {
            var v = GetString(obj, name);
            return v != null && SimpleTokenPattern().IsMatch(v) ? v : null;
        }

        message = new BrowserMessage
        {
            Type = type,
            RequestId = requestId,
            Url = url,
            PageTitle = Clean(GetString(obj, "pageTitle"), 300),
            LinkText = Clean(GetString(obj, "linkText"), 200),
            Browser = Token("browser"),
            TabId = Token("tabId"),
            ExtensionVersion = Token("extensionVersion"),
        };
        return true;
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = Content.TextUtil.SingleLine(s, max);
        return t.Length == 0 ? null : t;
    }

    private static string? GetString(JsonObject obj, string name)
        => obj.TryGetPropertyValue(name, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    private static bool TryGetInt(JsonObject obj, string name, out int value)
    {
        value = 0;
        return obj.TryGetPropertyValue(name, out var v) && v is JsonValue jv && jv.TryGetValue(out value);
    }
}

/// <summary>アプリ → 拡張へのメッセージ。</summary>
public static class HostMessages
{
    public static byte[] Config(int hoverDelayMs, bool paused, string appVersion)
        => Serialize(new JsonObject
        {
            ["schemaVersion"] = BrowserMessage.SchemaVersion,
            ["type"] = "config",
            ["hoverDelayMs"] = hoverDelayMs,
            ["paused"] = paused,
            ["appVersion"] = appVersion,
        });

    public static byte[] Status(bool appRunning)
        => Serialize(new JsonObject
        {
            ["schemaVersion"] = BrowserMessage.SchemaVersion,
            ["type"] = "status",
            ["appRunning"] = appRunning,
        });

    public static byte[] Error(string code)
        => Serialize(new JsonObject
        {
            ["schemaVersion"] = BrowserMessage.SchemaVersion,
            ["type"] = "error",
            ["code"] = code,
        });

    public static byte[] Simple(string type)
        => Serialize(new JsonObject { ["schemaVersion"] = BrowserMessage.SchemaVersion, ["type"] = type });

    private static byte[] Serialize(JsonObject obj) => Encoding.UTF8.GetBytes(obj.ToJsonString());
}
