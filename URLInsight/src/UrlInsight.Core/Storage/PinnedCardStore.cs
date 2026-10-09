using System.Text.Json;
using System.Text.Json.Serialization;
using UrlInsight.Core.Pipeline;

namespace UrlInsight.Core.Storage;

/// <summary>固定したカード1枚分(内容と画面上の位置。位置は物理ピクセル)。</summary>
public sealed class PinnedCardEntry
{
    public SummaryCard Card { get; set; } = new();
    public int Left { get; set; }
    public int Top { get; set; }
}

/// <summary>
/// 固定したカードを保存し、アプリの再起動後に元の位置へ戻すための保存先(%LOCALAPPDATA%\URLInsight\pinned-cards.json)。
/// API キーなどの秘密情報は含まない(要約キャッシュと同じ程度の内容)。
/// </summary>
public sealed class PinnedCardStore
{
    public const int MaxCards = 50;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _lock = new();

    public PinnedCardStore(string path) => _path = path;

    public List<PinnedCardEntry> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return new();
            try
            {
                var list = JsonSerializer.Deserialize<List<PinnedCardEntry>>(File.ReadAllText(_path), Json) ?? new();
                // 壊れた項目(URL の無いもの)は読み飛ばす
                return list.Where(e => e?.Card != null && !string.IsNullOrWhiteSpace(e.Card.Url)).Take(MaxCards).ToList();
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                // 壊れたファイルは退避して、固定カードなしで起動する
                try { File.Copy(_path, _path + ".broken", overwrite: true); } catch (IOException) { }
                return new();
            }
        }
    }

    public void Save(IEnumerable<PinnedCardEntry> entries)
    {
        lock (_lock)
        {
            var list = entries.Take(MaxCards).ToList();
            if (list.Count == 0)
            {
                if (File.Exists(_path)) File.Delete(_path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, Json));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}
