using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using UrlInsight.Core;
using UrlInsight.Core.Content;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Services;
using UrlInsight.Mac.UI;

namespace UrlInsight.Mac.Tests;

/// <summary>
/// Mac 版の画面を、画面なしモードで実際に描いて確かめる(文字が入りきるか・要素が出ているか)。
/// 描いた画像は URLINSIGHT_SNAPSHOT_DIR を指定したときだけ保存する(目で確認する用)。
/// </summary>
public class RenderTests
{
    private static void Snapshot(Window w, string name)
    {
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("URLINSIGHT_SNAPSHOT_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            frame!.Save(Path.Combine(dir, name + ".png"));
        }
    }

    private static SummaryCard SampleCard() => new()
    {
        Url = "https://news.example.jp/articles/2026/10/subsidy",
        Domain = "news.example.jp",
        Kind = PageKind.Web,
        Title = "新しい補助金制度が来年4月から始まる",
        SiteName = "Example ニュース",
        SummaryLines = new() { "国の新しい補助金制度が2027年4月から始まり、全国の一般家庭が対象になる。", "申請はオンラインでも受け付け、自治体の説明会も各地で予定されている。" },
        KeyPoints = new() { "対象: 全国の一般家庭", "開始: 2027年4月", "申請: オンライン可" },
        Quality = SourceQuality.FullText,
        ProviderName = "Anthropic (Claude)",
        Notes = new() { "本文の冒頭と末尾の一部のみを使って要約しました" },
    };

    [AvaloniaFact]
    public void CardShowsTheSummary()
    {
        var w = new CardWindow();
        var card = SampleCard();
        w.ApplyState(new CardState { Phase = CardPhase.Result, Url = card.Url, Domain = card.Domain, Kind = card.Kind, Title = card.Title, Card = card });
        w.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("要約を作成しました", w.FindControl<TextBlock>("StatusText")!.Text);
        Assert.True(w.FindControl<Control>("ResultPanel")!.IsVisible);
        Assert.False(w.FindControl<Control>("LoadingPanel")!.IsVisible);
        Assert.True(w.Bounds.Height > 200);
        Snapshot(w, "card-result");
    }

    [AvaloniaFact]
    public void CardShowsTheConsentQuestion()
    {
        var w = new CardWindow();
        w.ApplyState(new CardState { Phase = CardPhase.Consent, Url = "https://example.com/", Domain = "example.com" });
        var task = w.AskConsentAsync(new ConsentInfo("Anthropic (Claude)", "api.anthropic.com", "https://example.com/", PageKind.Web, 8200, ErrorCode.None, true), default);
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Control>("ConsentPanel")!.IsVisible);
        Snapshot(w, "card-consent");

        w.FindControl<Button>("ConsentDeclineButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ConsentDecision.Decline, task.Result);
    }

    [AvaloniaFact]
    public void CardShowsErrors()
    {
        var w = new CardWindow();
        w.ApplyState(new CardState { Phase = CardPhase.Error, Url = "https://example.com/", Domain = "example.com", Error = ErrorCode.InvalidApiKey });
        w.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ErrorMessages.Title(ErrorCode.InvalidApiKey), w.FindControl<TextBlock>("ErrorTitle")!.Text);
        Assert.True(w.FindControl<Control>("ErrorSettingsButton")!.IsVisible);
        Snapshot(w, "card-error");
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _d = new();
        public void Save(string name, string secret) => _d[name] = secret;
        public string? TryGet(string name) => _d.TryGetValue(name, out var v) ? v : null;
        public void Delete(string name) => _d.Remove(name);
        public bool Exists(string name) => _d.ContainsKey(name);
    }

    [AvaloniaFact]
    public void MainAndSettingsWindowsRender()
    {
        var root = Path.Combine(Path.GetTempPath(), "urlinsight-mac-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();
            using var services = new MacServices(paths, new MemorySecrets());
            var coordinator = new HoverCoordinator(services);
            var main = new MainWindow(services, coordinator) { AccessibilityTrusted = () => false };
            main.Show();
            main.RefreshAll();
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.FindControl<Control>("SetupBox")!.IsVisible);
            Assert.True(main.FindControl<Control>("AccessibilityButton")!.IsVisible);
            Snapshot(main, "main");

            var settings = new SettingsWindow(services);
            settings.Show();
            Dispatcher.UIThread.RunJobs();
            Snapshot(settings, "settings-general");
            settings.SelectTab("ai");
            Dispatcher.UIThread.RunJobs();
            Snapshot(settings, "settings-ai");
            main.AllowClose = true;
            main.Close();
            settings.Close();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
