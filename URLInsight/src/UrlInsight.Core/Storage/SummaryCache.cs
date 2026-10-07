using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using UrlInsight.Core.Content;
using UrlInsight.Core.Pipeline;

namespace UrlInsight.Core.Storage;

public sealed record CacheEntryInfo(long Id, string Url, string Domain, PageKind Kind, string Title, string FirstLine, DateTimeOffset CreatedAt, DateTimeOffset LastAccess);

public sealed record CacheStats(int Count, long Bytes);

/// <summary>
/// SQLite による要約キャッシュ兼履歴。保存するのは要約結果と最小限のメタデータのみ(ページ本文・APIキーは保存しない)。
/// キャッシュキーは 正規化URL + プロンプト版 + プロバイダ/モデル + 出力言語 + 処理バージョン。
/// 取得した本文のハッシュも記録し、再要約時の比較に使う。
/// </summary>
public sealed class SummaryCache
{
    public const string ProcessingVersion = "1";
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly string _connectionString;
    private readonly Func<DateTimeOffset> _now;

    public SummaryCache(string dbPath, Func<DateTimeOffset>? now = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Initialize();
    }

    public static string LookupKey(string normalizedUrl, string providerId, string model, string language = "ja")
        => ExtractedContent.Hash(string.Join('\n', normalizedUrl, AI.SummaryPromptBuilder.PromptVersion, providerId, model, language, ProcessingVersion));

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Initialize()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS summaries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lookup_key TEXT NOT NULL UNIQUE,
                url TEXT NOT NULL,
                domain TEXT NOT NULL,
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                first_line TEXT NOT NULL,
                card_json TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                created_utc INTEGER NOT NULL,
                last_access_utc INTEGER NOT NULL,
                size_bytes INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_summaries_access ON summaries(last_access_utc);
            """;
        cmd.ExecuteNonQuery();
    }

    public SummaryCard? TryGet(string lookupKey, TimeSpan ttl)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, card_json, created_utc FROM summaries WHERE lookup_key = $k";
        cmd.Parameters.AddWithValue("$k", lookupKey);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var id = r.GetInt64(0);
        var json = r.GetString(1);
        var created = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2));
        r.Close();
        if (_now() - created > ttl)
        {
            Delete(id);
            return null;
        }
        Touch(c, id);
        return Deserialize(json);
    }

    public SummaryCard? GetById(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT card_json FROM summaries WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        var json = cmd.ExecuteScalar() as string;
        if (json == null) return null;
        Touch(c, id);
        return Deserialize(json);
    }

    private void Touch(SqliteConnection c, long id)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE summaries SET last_access_utc = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$t", _now().ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static SummaryCard? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<SummaryCard>(json, Json); }
        catch (JsonException) { return null; }
    }

    public void Put(string lookupKey, SummaryCard card, string contentHash, string providerId, string model)
    {
        var json = JsonSerializer.Serialize(card, Json);
        var now = _now().ToUnixTimeSeconds();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO summaries (lookup_key, url, domain, kind, title, first_line, card_json, content_hash, provider, model, created_utc, last_access_utc, size_bytes)
            VALUES ($k, $url, $domain, $kind, $title, $first, $json, $hash, $provider, $model, $now, $now, $size)
            ON CONFLICT(lookup_key) DO UPDATE SET
                url = excluded.url, domain = excluded.domain, kind = excluded.kind, title = excluded.title,
                first_line = excluded.first_line, card_json = excluded.card_json, content_hash = excluded.content_hash,
                provider = excluded.provider, model = excluded.model, created_utc = excluded.created_utc,
                last_access_utc = excluded.last_access_utc, size_bytes = excluded.size_bytes;
            """;
        cmd.Parameters.AddWithValue("$k", lookupKey);
        cmd.Parameters.AddWithValue("$url", card.Url);
        cmd.Parameters.AddWithValue("$domain", card.Domain);
        cmd.Parameters.AddWithValue("$kind", card.Kind.ToString());
        cmd.Parameters.AddWithValue("$title", card.Title);
        cmd.Parameters.AddWithValue("$first", card.SummaryLines.FirstOrDefault() ?? card.Description ?? string.Empty);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$hash", contentHash);
        cmd.Parameters.AddWithValue("$provider", providerId);
        cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$size", (long)json.Length * 2 + card.Url.Length * 2 + 256);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<CacheEntryInfo> Recent(int limit)
    {
        var list = new List<CacheEntryInfo>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, url, domain, kind, title, first_line, created_utc, last_access_utc FROM summaries ORDER BY last_access_utc DESC, id DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            Enum.TryParse<PageKind>(r.GetString(3), out var kind);
            list.Add(new CacheEntryInfo(r.GetInt64(0), r.GetString(1), r.GetString(2), kind, r.GetString(4), r.GetString(5),
                DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(6)), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(7))));
        }
        return list;
    }

    public void Delete(long id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM summaries WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Clear()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM summaries; VACUUM;";
        cmd.ExecuteNonQuery();
    }

    public CacheStats Stats()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size_bytes), 0) FROM summaries";
        using var r = cmd.ExecuteReader();
        r.Read();
        return new CacheStats(r.GetInt32(0), r.GetInt64(1));
    }

    /// <summary>期限切れを削除し、件数・容量の上限を超えた分を古い未使用順(LRU)に削除する。</summary>
    public int Prune(TimeSpan ttl, int maxEntries, long maxBytes)
    {
        int removed = 0;
        using var c = Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM summaries WHERE created_utc < $limit";
            cmd.Parameters.AddWithValue("$limit", (_now() - ttl).ToUnixTimeSeconds());
            removed += cmd.ExecuteNonQuery();
        }
        while (true)
        {
            long count, bytes;
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size_bytes), 0) FROM summaries";
                using var r = cmd.ExecuteReader();
                r.Read();
                count = r.GetInt64(0);
                bytes = r.GetInt64(1);
            }
            if (count <= maxEntries && bytes <= maxBytes) break;
            using var del = c.CreateCommand();
            del.CommandText = "DELETE FROM summaries WHERE id IN (SELECT id FROM summaries ORDER BY last_access_utc ASC, id ASC LIMIT $n)";
            del.Parameters.AddWithValue("$n", Math.Max(1, count - maxEntries));
            var n = del.ExecuteNonQuery();
            removed += n;
            if (n == 0) break;
        }
        return removed;
    }
}
