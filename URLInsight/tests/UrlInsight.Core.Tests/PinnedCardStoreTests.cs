using UrlInsight.Core.Content;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Tests;

/// <summary>固定カードを再起動後に復元するための保存のテスト。</summary>
public class PinnedCardStoreTests
{
    private static PinnedCardEntry Entry(string url, int left, int top) => new()
    {
        Card = new SummaryCard
        {
            Url = url, Domain = "example.com", Kind = PageKind.Search, Title = "「東京 天気」の検索結果",
            SummaryLines = { "要約1", "要約2" }, KeyPoints = { "点1" }, Notes = { "注意" },
            SearchResults = { new SearchResultItem { Title = "結果", Url = "https://r.example/", Domain = "r.example", Snippet = "抜粋" } },
            ProviderName = "Fake", CreatedAt = new DateTimeOffset(2026, 10, 9, 1, 2, 3, TimeSpan.Zero),
        },
        Left = left,
        Top = top,
    };

    [Fact]
    public void SavesAndRestoresCardsWithPositions()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "pinned-cards.json");
        new PinnedCardStore(path).Save(new[] { Entry("https://a.example/", 100, 200), Entry("https://b.example/", -1800, 50) });

        var loaded = new PinnedCardStore(path).Load();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("https://a.example/", loaded[0].Card.Url);
        Assert.Equal((100, 200), (loaded[0].Left, loaded[0].Top));
        // 左側のモニター(負の座標)の位置もそのまま保存する
        Assert.Equal((-1800, 50), (loaded[1].Left, loaded[1].Top));
        var card = loaded[0].Card;
        Assert.Equal(PageKind.Search, card.Kind);
        Assert.Equal(new[] { "要約1", "要約2" }, card.SummaryLines);
        Assert.Equal("抜粋", Assert.Single(card.SearchResults).Snippet);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 1, 2, 3, TimeSpan.Zero), card.CreatedAt);
    }

    [Fact]
    public void SavingNoCardsRemovesTheFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "pinned-cards.json");
        var store = new PinnedCardStore(path);
        store.Save(new[] { Entry("https://a.example/", 0, 0) });
        Assert.True(File.Exists(path));
        store.Save(Array.Empty<PinnedCardEntry>());
        Assert.False(File.Exists(path));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void BrokenFileStartsWithoutCardsAndIsKeptAside()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "pinned-cards.json");
        File.WriteAllText(path, "{ broken");
        Assert.Empty(new PinnedCardStore(path).Load());
        Assert.True(File.Exists(path + ".broken"));

        File.WriteAllText(path, """[{"Card":{"Url":""},"Left":0,"Top":0},{"Card":{"Url":"https://ok.example/"},"Left":1,"Top":2}]""");
        Assert.Equal("https://ok.example/", Assert.Single(new PinnedCardStore(path).Load()).Card.Url);
    }

    [Fact]
    public void KeepsAtMostTheLimit()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "pinned-cards.json");
        var store = new PinnedCardStore(path);
        store.Save(Enumerable.Range(0, PinnedCardStore.MaxCards + 5).Select(i => Entry($"https://e{i}.example/", i, i)));
        Assert.Equal(PinnedCardStore.MaxCards, store.Load().Count);
    }
}
