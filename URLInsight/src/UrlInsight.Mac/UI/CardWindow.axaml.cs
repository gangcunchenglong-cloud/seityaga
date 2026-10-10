using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using UrlInsight.Core;
using UrlInsight.Core.Content;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Platform;

namespace UrlInsight.Mac.UI;

/// <summary>
/// 画面の右側に表示する要約カード(Windows 版の CardWindow と同じ内容・操作)。
/// 前面に出ても、表示しただけではキーボードの入力先を奪わない(ShowActivated=False)。
/// </summary>
public partial class CardWindow : Window
{
    private TaskCompletionSource<ConsentDecision>? _consent;
    private (int X, int Y) _anchor;
    private CardSide _side = CardSide.Right;
    private IReadOnlyList<ScreenBox> _avoid = Array.Empty<ScreenBox>();

    public CardWindow()
    {
        InitializeComponent();
        SetPhaseVisibility(CardPhase.Loading);
        KeyDown += OnKeyDown;

        CloseButton.Click += (_, _) => { CancelConsent(); CloseRequested?.Invoke(); };
        PinToggle.Click += (_, _) => PinChanged?.Invoke(IsPinned);
        CancelButton.Click += (_, _) => CancelRequested?.Invoke();
        ResummarizeButton.Click += (_, _) => ResummarizeRequested?.Invoke();
        RetryButton.Click += (_, _) => ResummarizeRequested?.Invoke();
        OpenButton.Click += (_, _) => OpenRequested?.Invoke();
        CopyButton.Click += (_, _) => CopyRequested?.Invoke();
        IgnoreButton.Click += (_, _) => IgnoreRequested?.Invoke();
        NoticeSettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        ErrorSettingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        ConsentSendButton.Click += (_, _) => Decide(RememberCheck.IsChecked == true ? ConsentDecision.SendAndRemember : ConsentDecision.Send);
        ConsentDeclineButton.Click += (_, _) => Decide(ConsentDecision.Decline);
        // 固定したカードは、上部(種別・ドメインの行)をドラッグして好きな位置へ動かせる
        HeaderPanel.PointerPressed += (_, e) =>
        {
            if (IsPinned && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
    }

    public CardPhase Phase { get; private set; } = CardPhase.Loading;
    public CardState? State { get; private set; }
    public bool IsPinned => PinToggle.IsChecked == true;
    /// <summary>カードの上にカーソルがあるか、キーボードで操作中か(自動で閉じない条件)。</summary>
    public bool IsInUse => IsPointerOver || IsKeyboardFocusWithin;

    public event Action? CloseRequested;
    public event Action? CancelRequested;
    public event Action? ResummarizeRequested;
    public event Action? OpenRequested;
    public event Action? CopyRequested;
    public event Action? IgnoreRequested;
    public event Action? SettingsRequested;
    public event Action<bool>? PinChanged;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (Phase == CardPhase.Consent) Decide(ConsentDecision.Decline);
        CloseRequested?.Invoke();
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
                ErrorSettingsButton.IsVisible = state.Error is ErrorCode.InvalidApiKey or ErrorCode.AiNotConfigured or ErrorCode.RateLimited;
                RetryButton.IsVisible = !ErrorMessages.IsNonRetryable(state.Error);
                FooterText.Text = string.Empty;
                if (state.Card != null && !string.IsNullOrWhiteSpace(state.Card.Description))
                {
                    ResultPanel.IsVisible = true;
                    ShowCardBody(state.Card, showNotice: false);
                }
                break;
        }
        UpdateActions(state);
    }

    private void ShowResult(CardState state)
    {
        var card = state.Card!;
        TitleText.Text = card.Title;
        StatusText.Text = card.HasSummary
            ? (card.IsTestProvider ? "テスト用プロバイダの出力です（AIではありません）" : "要約を作成しました")
            : card.SearchResults.Count > 0 ? "検索結果を表示しています" : "ページ情報のみ表示しています";
        DomainText.Text = string.IsNullOrWhiteSpace(card.SiteName) ? card.Domain : card.SiteName;
        ShowCardBody(card, showNotice: true);

        var parts = new List<string>();
        if (state.FromCache) parts.Add("保存済み要約");
        if (card.HasSummary) parts.Add(Labels.Quality(card.Quality));
        if (!string.IsNullOrEmpty(card.ProviderName)) parts.Add(card.ProviderName);
        parts.Add(card.CreatedAt.ToLocalTime().ToString("M/d HH:mm"));
        FooterText.Text = string.Join(" ・ ", parts);
    }

    private void ShowCardBody(SummaryCard card, bool showNotice)
    {
        SummaryList.ItemsSource = card.SummaryLines;
        PointsList.ItemsSource = card.KeyPoints;
        SummaryLabel.IsVisible = SummaryList.IsVisible = card.SummaryLines.Count > 0;
        PointsLabel.IsVisible = PointsList.IsVisible = card.KeyPoints.Count > 0;
        // 検索ページは、AI の要約の有無にかかわらず上位の検索結果を一覧で見せる(AI 要約ありなら上位5件)
        var results = card.SearchResults.Take(card.HasSummary ? 5 : SearchExtractor.MaxResults).ToList();
        ResultsList.ItemsSource = results;
        ResultsLabel.IsVisible = ResultsList.IsVisible = results.Count > 0;
        bool showDescription = !card.HasSummary && !string.IsNullOrWhiteSpace(card.Description);
        DescriptionLabel.IsVisible = DescriptionText.IsVisible = showDescription;
        DescriptionText.Text = card.Description ?? string.Empty;
        NotesList.ItemsSource = card.Notes;
        NotesList.IsVisible = card.Notes.Count > 0;

        bool notice = showNotice && card.Notice != ErrorCode.None;
        NoticeBox.IsVisible = notice;
        if (notice)
        {
            NoticeTitle.Text = ErrorMessages.Title(card.Notice);
            NoticeHint.Text = card.Notice == ErrorCode.ContentTooShort
                ? "本文を取得できず、タイトル等のメタ情報のみ表示しています。推測による要約は行いません。"
                : ErrorMessages.Hint(card.Notice);
            NoticeSettingsButton.IsVisible = card.Notice == ErrorCode.AiNotConfigured;
        }
    }

    private void UpdateActions(CardState state)
    {
        bool hasUrl = !string.IsNullOrEmpty(state.Url) && state.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        bool busy = state.Phase is CardPhase.Loading or CardPhase.Summarizing or CardPhase.Consent;
        OpenButton.IsEnabled = hasUrl;
        IgnoreButton.IsEnabled = hasUrl;
        ResummarizeButton.IsVisible = state.Phase == CardPhase.Result;
        CopyButton.IsVisible = state.Phase == CardPhase.Result;
        ActionsPanel.IsVisible = !busy || hasUrl;
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
        KindBadge.Background = Brush(bg);
        KindText.Foreground = Brush(fg);
    }

    internal static IBrush? Brush(string key)
        => Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var v) == true ? v as IBrush : null;

    private void SetPhaseVisibility(CardPhase phase)
    {
        LoadingPanel.IsVisible = phase is CardPhase.Loading or CardPhase.Summarizing;
        ConsentPanel.IsVisible = phase == CardPhase.Consent;
        ResultPanel.IsVisible = phase == CardPhase.Result;
        ErrorPanel.IsVisible = phase == CardPhase.Error;
    }

    // ---------- 送信前確認 ----------

    public Task<ConsentDecision> AskConsentAsync(ConsentInfo info, CancellationToken ct)
    {
        _consent?.TrySetResult(ConsentDecision.Decline);
        var tcs = new TaskCompletionSource<ConsentDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _consent = tcs;
        ct.Register(() => tcs.TrySetCanceled());

        var lines = new List<string>
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
        RememberCheck.IsVisible = info.ShowRememberOption;
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

    // ---------- 配置 ----------

    /// <summary>画面上の位置と大きさ(Avalonia の画面座標)。</summary>
    internal ScreenBox ScreenRect
    {
        get
        {
            var scale = DesktopScaling;
            return new ScreenBox(Position.X, Position.Y, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        }
    }

    private Avalonia.Platform.Screen? ScreenAt(int x, int y)
        => Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? Screens.Primary;

    private static ScreenBox Box(PixelRect r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>カーソルのある画面の右側(設定により左側)に表示する。</summary>
    internal void ShowNearCursor(CardSide side, IReadOnlyList<ScreenBox>? avoid = null)
    {
        _side = side;
        _avoid = avoid ?? Array.Empty<ScreenBox>();
        // macOS のカーソル位置(ポイント)を Avalonia の画面座標に直す
        var pt = MacNative.CursorPosition();
        _anchor = ((int)Math.Round(pt.X * DesktopScaling), (int)Math.Round(pt.Y * DesktopScaling));
        var screen = ScreenAt(_anchor.X, _anchor.Y);
        if (screen != null) MaxHeight = Math.Max(260, screen.WorkingArea.Height * 0.8 / DesktopScaling);

        if (!IsVisible)
        {
            if (screen != null) Position = new PixelPoint(screen.WorkingArea.Right - (int)(430 * DesktopScaling), screen.WorkingArea.Y + 8);
            Show();
        }
        // 表示して大きさが決まってから位置を合わせる
        PlaceAtAnchor();
        Dispatcher.UIThread.Post(PlaceAtAnchor, DispatcherPriority.Loaded);
    }

    /// <summary>保存した位置に表示する(固定カードの復元用)。画面外になる場合は画面の中に収める。</summary>
    internal void ShowAt(int left, int top)
    {
        var screen = ScreenAt(left + 10, top + 10);
        if (screen != null) MaxHeight = Math.Max(260, screen.WorkingArea.Height * 0.8 / DesktopScaling);
        Position = new PixelPoint(left, top);
        if (!IsVisible) Show();
        Dispatcher.UIThread.Post(() =>
        {
            var s = ScreenAt(left + 10, top + 10);
            if (s == null) return;
            var r = ScreenRect;
            var (x, y) = CardPlacement.ClampInto(left, top, Box(s.WorkingArea), r.Width, r.Height, DesktopScaling);
            if (x != left || y != top) Position = new PixelPoint(x, y);
        }, DispatcherPriority.Loaded);
    }

    private void PlaceAtAnchor()
    {
        if (!IsVisible) return;
        var screen = ScreenAt(_anchor.X, _anchor.Y);
        if (screen == null) return;
        var r = ScreenRect;
        var (x, y) = CardPlacement.NearAnchor(_anchor.X, _anchor.Y, Box(screen.WorkingArea), r.Width, r.Height, _side, _avoid, DesktopScaling);
        Position = new PixelPoint(x, y);
    }

    /// <summary>内容が変わって高さが伸びた場合に、画面の下端からはみ出さないよう上へずらす。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ClientSizeProperty || !IsVisible) return;
        var r = ScreenRect;
        var screen = ScreenAt(r.Left + r.Width / 2, r.Top + 10);
        if (screen == null) return;
        var work = screen.WorkingArea;
        int margin = (int)(8 * DesktopScaling);
        if (r.Bottom > work.Bottom - margin)
            Position = new PixelPoint(r.Left, Math.Max(work.Y + margin, work.Bottom - margin - r.Height));
    }

    public void HideCard()
    {
        CancelConsent();
        Hide();
        State = null;
    }
}
