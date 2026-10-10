using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using UrlInsight.Core;
using UrlInsight.Core.Content;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Services;

namespace UrlInsight.Mac.UI;

/// <summary>メイン画面(動作状況・最近の要約・URL の手動入力)。閉じてもメニューバーに残る。</summary>
public partial class MainWindow : Window
{
    private readonly MacServices _services = null!;
    private readonly HoverCoordinator _coordinator = null!;

    /// <summary>XAML のプレビュー用(使わない)。</summary>
    public MainWindow() => InitializeComponent();

    internal MainWindow(MacServices services, HoverCoordinator coordinator)
    {
        _services = services;
        _coordinator = coordinator;
        InitializeComponent();
        Activated += (_, _) => RefreshAll();
        SettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        SetupSettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        AccessibilityButton.Click += (_, _) => OpenAccessibilitySettings();
        RefreshButton.Click += (_, _) => RefreshAll();
        SummarizeButton.Click += (_, _) => SummarizeInput();
        UrlInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) SummarizeInput(); };
        PauseButton.Click += (_, _) =>
        {
            _services.UpdateSettings(s => s.Paused = !s.Paused);
            RefreshStatus();
        };
        RefreshAll();
    }

    /// <summary>true のときは閉じる操作で隠さず、本当に閉じる(アプリ終了時)。</summary>
    public bool AllowClose { get; set; }
    /// <summary>アクセシビリティの許可があるか(ホバー検出の状態表示用)。</summary>
    public Func<bool> AccessibilityTrusted { get; set; } = () => true;
    public event Action? SettingsRequested;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            // 閉じるボタンではメニューバーに格納する(終了はメニューバーの「終了」から)
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    public void RefreshAll()
    {
        RefreshStatus();
        RefreshRecent();
    }

    public void RefreshStatus()
    {
        var s = _services.Settings;
        bool paused = s.Paused;
        RunDot.Fill = CardWindow.Brush(paused ? "MutedBrush" : "OkBrush");
        RunText.Text = paused ? "一時停止中" : "稼働中";
        RunText.Foreground = CardWindow.Brush(paused ? "MutedBrush" : "OkFgBrush");
        PauseButton.Content = paused ? "再開" : "一時停止";

        bool trusted = AccessibilityTrusted();
        bool hover = s.UseUiAutomationHover && trusted;
        HoverDot.Fill = CardWindow.Brush(hover ? "OkBrush" : "NgBrush");
        HoverText.Text = !trusted ? "アクセシビリティの許可が必要です（下の案内を見てください）"
            : s.UseUiAutomationHover ? "リンクやURLにカーソルを重ねると要約します"
            : "ホバー検出がオフです（設定 → 一般）";

        var (ok, text) = _services.DescribeProvider();
        AiDot.Fill = CardWindow.Brush(ok ? "OkBrush" : "MutedBrush");
        AiText.Text = text;

        var steps = new List<string>();
        if (!trusted)
            steps.Add("・「アクセシビリティの設定を開く」を押し、一覧の URL Insight をオンにしてください（カーソル下のリンクを読むために必要です）。オンにしたあと、反映まで数秒かかります");
        if (!ok) steps.Add("・設定 → AI でプロバイダとAPIキーを設定すると要約が出ます（未設定でもページのタイトル・説明は表示します）");
        SetupBox.IsVisible = steps.Count > 0;
        AccessibilityButton.IsVisible = !trusted;
        SetupText.Text = string.Join("\n", steps);
    }

    /// <summary>ホバー検出の状況(うまく動かないときの原因確認用)。</summary>
    public void SetHoverStatus(string text) => HoverDiagText.Text = $"最後の検出（{DateTime.Now:HH:mm:ss}）: {text}";

    public void RefreshRecent()
    {
        var cache = _services.Cache;
        var items = cache == null ? Array.Empty<RecentItem>() : cache.Recent(20).Select(e => new RecentItem(e)).ToArray();
        RecentList.ItemsSource = items;
        RecentEmpty.IsVisible = items.Length == 0;
    }

    public void FocusUrlInput()
    {
        UrlInput.Focus();
        UrlInput.SelectAll();
    }

    private void SummarizeInput()
    {
        var text = (UrlInput.Text ?? string.Empty).Trim();
        if (!UrlPolicy.TryNormalize(text, out _, out var error))
        {
            UrlError.Text = string.IsNullOrEmpty(text) ? "URLを入力してください" : ErrorMessages.Title(error) + "（http/https のURLを入力してください）";
            UrlError.IsVisible = true;
            return;
        }
        UrlError.IsVisible = false;
        _coordinator.StartManual(text);
    }

    private void Recent_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: long id } && _services.Cache?.GetById(id) is { } card)
            _coordinator.ShowCached(card);
    }

    /// <summary>システム設定の「プライバシーとセキュリティ → アクセシビリティ」を開く。</summary>
    internal static void OpenAccessibilitySettings()
    {
        try
        {
            var psi = new ProcessStartInfo("open") { UseShellExecute = false };
            psi.ArgumentList.Add("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility");
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AppLog.Error("open accessibility settings failed", ex);
        }
    }

    /// <summary>最近の要約一覧の1行。</summary>
    public sealed class RecentItem
    {
        public RecentItem(CacheEntryInfo e)
        {
            Id = e.Id;
            Title = string.IsNullOrWhiteSpace(e.Title) ? e.Domain : e.Title;
            FirstLine = e.FirstLine;
            KindLabel = e.Kind switch { PageKind.Pdf => "PDF", PageKind.YouTube => "YouTube", PageKind.Search => "検索", _ => "WEB" };
            (BadgeBg, BadgeFg) = e.Kind switch
            {
                PageKind.Pdf => (CardWindow.Brush("PdfBgBrush"), CardWindow.Brush("PdfFgBrush")),
                PageKind.YouTube => (CardWindow.Brush("YtBgBrush"), CardWindow.Brush("YtFgBrush")),
                _ => (CardWindow.Brush("BadgeBgBrush"), CardWindow.Brush("BadgeFgBrush")),
            };
            Meta = $"{Relative(e.CreatedAt)} ・ {e.Domain} ・ キャッシュ済み";
        }

        public long Id { get; }
        public string Title { get; }
        public string FirstLine { get; }
        public string KindLabel { get; }
        public IBrush? BadgeBg { get; }
        public IBrush? BadgeFg { get; }
        public string Meta { get; }

        private static string Relative(DateTimeOffset t)
        {
            var d = DateTimeOffset.UtcNow - t;
            if (d.TotalMinutes < 1) return "たった今";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}分前";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours}時間前";
            if (d.TotalDays < 2) return "昨日";
            return t.ToLocalTime().ToString("M/d");
        }
    }
}
