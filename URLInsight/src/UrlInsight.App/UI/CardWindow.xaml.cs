using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UrlInsight.App.Platform;
using UrlInsight.Core;
using UrlInsight.Core.Content;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;

namespace UrlInsight.App.UI;

/// <summary>
/// 画面右側に表示する要約カード。フォーカスを奪わない(WS_EX_NOACTIVATE)ように表示し、
/// クリックされたときだけアクティブ化してキーボード操作(Tab / Esc)を可能にする。
/// 位置はカーソルのあるモニターの作業領域・DPI に合わせて物理ピクセルで計算する。
/// </summary>
public partial class CardWindow : Window
{
    private IntPtr _hwnd;
    private TaskCompletionSource<ConsentDecision>? _consent;
    private NativeMethods.POINT _anchor;
    private CardSide _side = CardSide.Right;

    public CardWindow()
    {
        InitializeComponent();
        SetPhaseVisibility(CardPhase.Loading);
        PreviewMouseDown += (_, _) => ActivateForKeyboard();
        KeyDown += OnKeyDown;
        SizeChanged += (_, _) => { if (IsVisible) ClampIntoWorkArea(); };
        Deactivated += (_, _) => SetNoActivate(true);
    }

    public CardPhase Phase { get; private set; } = CardPhase.Loading;
    public CardState? State { get; private set; }
    public bool IsPinned => PinToggle.IsChecked == true;

    public event Action? CloseRequested;
    public event Action? CancelRequested;
    public event Action? ResummarizeRequested;
    public event Action? OpenRequested;
    public event Action? CopyRequested;
    public event Action? IgnoreRequested;
    public event Action? SettingsRequested;
    public event Action<bool>? PinChanged;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        SetNoActivate(true);
    }

    private void SetNoActivate(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        ex = on ? ex | NativeMethods.WS_EX_NOACTIVATE : ex & ~NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>カードを操作し始めたらキーボード操作できるようにする。</summary>
    public void ActivateForKeyboard()
    {
        if (IsActive) return;
        SetNoActivate(false);
        Activate();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (Phase == CardPhase.Consent) Decide(ConsentDecision.Decline);
            CloseRequested?.Invoke();
        }
    }

    // ---------- 表示状態 ----------

    public void SetPinned(bool pinned) => PinToggle.IsChecked = pinned;

    public void ApplyState(CardState state)
    {
        State = state;
        Phase = state.Phase;
        DomainText.Text = state.Domain;
        TitleText.Text = string.IsNullOrWhiteSpace(state.Title) ? state.Domain : state.Title;
        SetKind(state.Kind);
        SetPhaseVisibility(state.Phase);

        switch (state.Phase)
        {
            case CardPhase.Loading:
            case CardPhase.Summarizing:
                StatusText.Text = state.StatusText ?? "処理中…";
                FooterText.Text = string.Empty;
                break;
            case CardPhase.Consent:
                StatusText.Text = "送信内容を確認してください";
                FooterText.Text = string.Empty;
                break;
            case CardPhase.Result:
                ShowResult(state);
                break;
            case CardPhase.Error:
                StatusText.Text = "要約できませんでした";
                ErrorTitle.Text = ErrorMessages.Title(state.Error);
                ErrorHint.Text = ErrorMessages.Hint(state.Error);
                ErrorCorrelation.Text = state.CorrelationId != null ? $"エラーID: {state.CorrelationId}（ログ検索用）" : string.Empty;
                ErrorSettingsButton.Visibility = state.Error is ErrorCode.InvalidApiKey or ErrorCode.AiNotConfigured or ErrorCode.RateLimited
                    ? Visibility.Visible : Visibility.Collapsed;
                RetryButton.Visibility = ErrorMessages.IsNonRetryable(state.Error) ? Visibility.Collapsed : Visibility.Visible;
                FooterText.Text = string.Empty;
                // 取得済みのメタ情報があれば併せて表示
                if (state.Card != null && !string.IsNullOrWhiteSpace(state.Card.Description))
                {
                    ResultPanel.Visibility = Visibility.Visible;
                    ShowCardBody(state.Card, showNotice: false);
                }
                break;
        }
        UpdateActions(state);
        AutomationPropertiesHelper.Announce(this, $"{TitleText.Text} {StatusText.Text}");
    }

    private void ShowResult(CardState state)
    {
        var card = state.Card!;
        TitleText.Text = card.Title;
        var site = string.IsNullOrWhiteSpace(card.SiteName) ? card.Domain : card.SiteName;
        StatusText.Text = card.HasSummary
            ? (card.IsTestProvider ? "テスト用プロバイダの出力です（AIではありません）" : "要約を作成しました")
            : "ページ情報のみ表示しています";
        DomainText.Text = site;
        ShowCardBody(card, showNotice: true);

        var when = card.CreatedAt.ToLocalTime();
        var parts = new System.Collections.Generic.List<string>();
        if (state.FromCache) parts.Add("保存済み要約");
        if (card.HasSummary) parts.Add(Labels.Quality(card.Quality));
        if (!string.IsNullOrEmpty(card.ProviderName)) parts.Add(card.ProviderName);
        parts.Add(when.ToString("M/d HH:mm"));
        FooterText.Text = string.Join(" ・ ", parts);
    }

    private void ShowCardBody(SummaryCard card, bool showNotice)
    {
        SummaryList.ItemsSource = card.SummaryLines;
        PointsList.ItemsSource = card.KeyPoints;
        SummaryLabel.Visibility = Vis(card.SummaryLines.Count > 0);
        SummaryList.Visibility = Vis(card.SummaryLines.Count > 0);
        PointsLabel.Visibility = Vis(card.KeyPoints.Count > 0);
        PointsList.Visibility = Vis(card.KeyPoints.Count > 0);
        bool showDescription = !card.HasSummary && !string.IsNullOrWhiteSpace(card.Description);
        DescriptionLabel.Visibility = Vis(showDescription);
        DescriptionText.Visibility = Vis(showDescription);
        DescriptionText.Text = card.Description ?? string.Empty;
        NotesList.ItemsSource = card.Notes;
        NotesList.Visibility = Vis(card.Notes.Count > 0);

        bool notice = showNotice && card.Notice != ErrorCode.None;
        NoticeBox.Visibility = Vis(notice);
        if (notice)
        {
            NoticeTitle.Text = ErrorMessages.Title(card.Notice);
            NoticeHint.Text = card.Notice == ErrorCode.ContentTooShort
                ? "本文を取得できず、タイトル等のメタ情報のみ表示しています。推測による要約は行いません。"
                : ErrorMessages.Hint(card.Notice);
            NoticeSettingsButton.Visibility = Vis(card.Notice == ErrorCode.AiNotConfigured);
        }
    }

    private void UpdateActions(CardState state)
    {
        bool hasUrl = !string.IsNullOrEmpty(state.Url) && state.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        bool busy = state.Phase is CardPhase.Loading or CardPhase.Summarizing or CardPhase.Consent;
        OpenButton.IsEnabled = hasUrl;
        IgnoreButton.IsEnabled = hasUrl;
        ResummarizeButton.Visibility = Vis(state.Phase == CardPhase.Result);
        CopyButton.Visibility = Vis(state.Phase == CardPhase.Result);
        ActionsPanel.Visibility = Vis(!busy || hasUrl);
    }

    private void SetKind(PageKind? kind)
    {
        var k = kind ?? PageKind.Web;
        KindText.Text = kind == null ? "確認中" : Labels.Kind(k);
        (string bg, string fg) = k switch
        {
            PageKind.Pdf => ("PdfBgBrush", "PdfFgBrush"),
            PageKind.YouTube => ("YtBgBrush", "YtFgBrush"),
            _ => ("BadgeBgBrush", "BadgeFgBrush"),
        };
        KindBadge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, bg);
        KindText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, fg);
    }

    private void SetPhaseVisibility(CardPhase phase)
    {
        LoadingPanel.Visibility = Vis(phase is CardPhase.Loading or CardPhase.Summarizing);
        ConsentPanel.Visibility = Vis(phase == CardPhase.Consent);
        ResultPanel.Visibility = Vis(phase == CardPhase.Result);
        ErrorPanel.Visibility = Vis(phase == CardPhase.Error);
    }

    private static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    // ---------- 送信前確認 ----------

    public Task<ConsentDecision> AskConsentAsync(ConsentInfo info, CancellationToken ct)
    {
        _consent?.TrySetResult(ConsentDecision.Decline);
        var tcs = new TaskCompletionSource<ConsentDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _consent = tcs;
        ct.Register(() => tcs.TrySetCanceled());

        var lines = new System.Collections.Generic.List<string>
        {
            $"送信先: {info.ProviderName}（{info.EndpointHost}）",
            $"送信内容: ページのURL・タイトル・本文 約{info.CharCount:N0}文字",
        };
        if (info.Kind == PageKind.Pdf) lines.Add("PDFの本文には機密情報が含まれる場合があります。");
        if (info.Warning == ErrorCode.YouTubeNoCaptions)
            lines.Insert(0, "字幕を利用できないため、動画の説明欄のみで要約します。");
        lines.Add("費用・利用枠は提供元の条件に従います。");
        ConsentText.Text = string.Join("\n", lines);
        RememberCheck.IsChecked = false;
        RememberCheck.Visibility = Vis(info.ShowRememberOption);
        ConsentSendButton.Content = info.Warning == ErrorCode.YouTubeNoCaptions ? "説明欄のみで続ける" : "送信して要約";
        return tcs.Task;
    }

    private void Decide(ConsentDecision decision)
    {
        var tcs = _consent;
        _consent = null;
        tcs?.TrySetResult(decision);
    }

    public void CancelConsent() => Decide(ConsentDecision.Decline);

    private void ConsentSend_Click(object sender, RoutedEventArgs e)
        => Decide(RememberCheck.IsChecked == true ? ConsentDecision.SendAndRemember : ConsentDecision.Send);

    private void ConsentDecline_Click(object sender, RoutedEventArgs e) => Decide(ConsentDecision.Decline);

    // ---------- ボタン ----------

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CancelConsent();
        CloseRequested?.Invoke();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void Resummarize_Click(object sender, RoutedEventArgs e) => ResummarizeRequested?.Invoke();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void Ignore_Click(object sender, RoutedEventArgs e) => IgnoreRequested?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Pin_Click(object sender, RoutedEventArgs e) => PinChanged?.Invoke(IsPinned);

    // ---------- 配置(物理ピクセル) ----------

    /// <summary>カーソルのあるモニターの右側(設定により左側)に表示する。</summary>
    public void ShowNearCursor(CardSide side)
    {
        _side = side;
        NativeMethods.GetCursorPos(out _anchor);
        var (work, scale) = NativeMethods.MonitorAt(_anchor);
        MaxHeight = Math.Max(260, work.Height * 0.8 / scale);

        if (!IsVisible)
        {
            Show();
        }
        // まず対象モニターへ移動して DPI を合わせてから、実サイズで位置を決める
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, work.Left + 4, work.Top + 4, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        MaxHeight = Math.Max(260, work.Height * 0.8 / scale);
        UpdateLayout();
        PlaceAtAnchor();
    }

    private void PlaceAtAnchor()
    {
        if (_hwnd == IntPtr.Zero) return;
        var (work, scale) = NativeMethods.MonitorAt(_anchor);
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        int w = rect.Width, h = rect.Height;
        int margin = (int)(8 * scale);
        int gap = (int)(28 * scale);

        int x = _side == CardSide.Right ? work.Right - w - margin : work.Left + margin;
        // カーソルとカードが重なる場合は、カーソルの反対側へずらす
        if (_side == CardSide.Right && _anchor.X >= x - gap / 2)
            x = Math.Max(work.Left + margin, _anchor.X - w - gap);
        else if (_side == CardSide.Left && _anchor.X <= x + w + gap / 2)
            x = Math.Min(work.Right - w - margin, _anchor.X + gap);

        int y = _anchor.Y - h / 4;
        y = Math.Max(work.Top + margin, Math.Min(y, work.Bottom - h - margin));
        if (h > work.Height - 2 * margin) y = work.Top + margin;

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>内容が変わって高さが伸びた場合に、作業領域の下端からはみ出さないよう上へずらす。</summary>
    private void ClampIntoWorkArea()
    {
        if (_hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var center = new NativeMethods.POINT { X = rect.Left + rect.Width / 2, Y = rect.Top + 10 };
        var (work, scale) = NativeMethods.MonitorAt(center);
        int margin = (int)(8 * scale);
        int y = rect.Top;
        if (rect.Bottom > work.Bottom - margin) y = Math.Max(work.Top + margin, work.Bottom - margin - rect.Height);
        if (y != rect.Top)
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, rect.Left, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
    }

    public void HideCard()
    {
        CancelConsent();
        SetNoActivate(true);
        Hide();
        State = null;
    }
}

/// <summary>読み上げソフトへ内容の変化を知らせる。</summary>
internal static class AutomationPropertiesHelper
{
    public static void Announce(Window window, string text)
    {
        System.Windows.Automation.AutomationProperties.SetHelpText(window, text);
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(window)
                   ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(window);
        peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
