using UrlInsight.Core.Storage;

namespace UrlInsight.Core.Tests;

public class SettingsDefaultsTests
{
    [Fact]
    public void AutoPinIsOnByDefaultAndIsSaved()
    {
        Assert.True(new AppSettings().AutoPinSummaries);

        using var dir = new TempDir();
        var store = new SettingsStore(Path.Combine(dir.Path, "settings.json"));
        // 以前の版で保存した設定(項目なし)でも既定のオンになる
        File.WriteAllText(Path.Combine(dir.Path, "settings.json"), """{"SchemaVersion":1}""");
        Assert.True(store.Load().AutoPinSummaries);

        store.Save(new AppSettings { AutoPinSummaries = false });
        Assert.False(store.Load().AutoPinSummaries);
    }
}
