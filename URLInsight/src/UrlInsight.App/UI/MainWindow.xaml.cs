using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UrlInsight.App.Services;
using UrlInsight.Core;
using UrlInsight.Core.Content;
using UrlInsight.Core.Net;
using UrlInsight.Core.Storage;

namespace UrlInsight.App.UI;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly HoverCoordinator _coordinator;

    internal MainWindow(AppServices services, HoverCoordinator coordinator)
    {
        _services = services;
        _coordinator = coordinator;
        InitializeComponent();
        Activated += (_, _) => RefreshAll();
        RefreshAll();
    }

    /// <summary>true のときは閉じる操作でトレイへ格納せず、本当に閉じる(アプリ終了時)。</summary>
    public bool AllowClose { get; set; }
    public event Action? SettingsRequested;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            // 閉じるボタンは既定でトレイへ格納
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
        RunDot.SetResourceReference(Shape.FillProperty, paused ? "MutedBrush" : "OkBrush");
        RunText.Text = paused ? "一時停止中" : "稼働中";
        RunText.SetResourceReference(TextBlock.ForegroundProperty, paused ? "MutedBrush" : "OkFgBrush");
        PauseButton.Content = paused ? "再開" : "一時停止";

        bool connected = _services.Bridge.BrowserConnected;
        bool registered = _services.NativeHostRegistered;
        bool uia = s.UseUiAutomationHover;
        bool working = connected || uia;
        ExtDot.SetResourceReference(Shape.FillProperty, working ? "OkBrush" : "NgBrush");
        ExtBadge.SetResourceReference(Border.BackgroundProperty, working ? "OkBgBrush" : "NgBgBrush");
        ExtBadgeText.SetResourceReference(TextBlock.ForegroundProperty, working ? "OkFgBrush" : "NgBrush");
        ExtBadgeText.Text = connected ? "拡張と接続済み" : uia ? "動作中" : "未接続";
        ExtText.Text = connected
            ? "Chrome拡張と接続しています" + (_services.Bridge.ExtensionVersion is string v ? $"（拡張 v{v}）" : "")
            : uia ? "リンクやURLにカーソルを重ねると要約します（Chrome拡張なしで動作中）"
            : registered ? ErrorMessages.Title(ErrorCode.ExtensionNotConnected) + "。Chromeで拡張を有効にしてください"
                         : "ホバー検出がオフです（設定 → 一般）";

        var (ok, text) = _services.DescribeProvider();
        AiDot.SetResourceReference(Shape.FillProperty, ok ? "OkBrush" : "MutedBrush");
        AiText.Text = text;

        var steps = new System.Collections.Generic.List<string>();
        if (!working && !registered) steps.Add("・設定 → ブラウザ拡張 で「ホストを登録」を押す");
        if (!working) steps.Add("・Chromeで拡張機能を読み込むか、設定 → 一般 で「拡張機能なしでもホバーを検出する」をオンにする");
        if (!ok) steps.Add("・設定 → AI でプロバイダとAPIキーを設定すると要約が出ます（未設定でもページのタイトル・説明は表示します）");
        SetupBox.Visibility = steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetupText.Text = string.Join("\n", steps);
    }

    public void RefreshRecent()
    {
        var cache = _services.Cache;
        var items = cache == null ? Array.Empty<RecentItem>() : cache.Recent(20).Select(e => new RecentItem(e)).ToArray();
        RecentList.ItemsSource = items;
        RecentEmpty.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void FocusUrlInput()
    {
        UrlInput.Focus();
        UrlInput.SelectAll();
    }

    private void Summarize_Click(object sender, RoutedEventArgs e) => SummarizeInput();

    private void UrlInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SummarizeInput();
    }

    private void SummarizeInput()
    {
        var text = UrlInput.Text.Trim();
        if (!UrlPolicy.TryNormalize(text, out _, out var error))
        {
            UrlError.Text = string.IsNullOrEmpty(text) ? "URLを入力してください" : ErrorMessages.Title(error) + "（http/https のURLを入力してください）";
            UrlError.Visibility = Visibility.Visible;
            return;
        }
        UrlError.Visibility = Visibility.Collapsed;
        _coordinator.StartManual(text);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _services.UpdateSettings(s => s.Paused = !s.Paused);
        RefreshStatus();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: long id } && _services.Cache?.GetById(id) is { } card)
            _coordinator.ShowCached(card);
    }

    /// <summary>最近の要約一覧の1行。</summary>
    public sealed class RecentItem
    {
        public RecentItem(CacheEntryInfo e)
        {
            Id = e.Id;
            Title = string.IsNullOrWhiteSpace(e.Title) ? e.Domain : e.Title;
            FirstLine = e.FirstLine;
            KindLabel = e.Kind switch { PageKind.Pdf => "PDF", PageKind.YouTube => "YouTube", _ => "WEB" };
            var app = Application.Current;
            (BadgeBg, BadgeFg) = e.Kind switch
            {
                PageKind.Pdf => ((Brush)app.FindResource("PdfBgBrush"), (Brush)app.FindResource("PdfFgBrush")),
                PageKind.YouTube => ((Brush)app.FindResource("YtBgBrush"), (Brush)app.FindResource("YtFgBrush")),
                _ => ((Brush)app.FindResource("BadgeBgBrush"), (Brush)app.FindResource("BadgeFgBrush")),
            };
            Meta = $"{Relative(e.CreatedAt)} ・ {e.Domain} ・ キャッシュ済み";
        }

        public long Id { get; }
        public string Title { get; }
        public string FirstLine { get; }
        public string KindLabel { get; }
        public Brush BadgeBg { get; }
        public Brush BadgeFg { get; }
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
