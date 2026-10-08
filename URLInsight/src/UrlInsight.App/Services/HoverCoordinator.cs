using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UrlInsight.App.UI;
using UrlInsight.Core;
using UrlInsight.Core.Bridge;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Pipeline;
using UrlInsight.Core.Storage;

namespace UrlInsight.App.Services;

/// <summary>
/// ホバーイベントとカード表示の制御(UIスレッド上で動く)。
/// - 新しいリンクに移ったら古い処理をキャンセルし、古い結果は画面に反映しない(requestId で判定)
/// - リンクから離れても短い猶予の間はカードを残し、カード上へカーソルを移せるようにする
/// - 固定モード・送信確認中・カード操作中は自動で閉じない
/// </summary>
internal sealed class HoverCoordinator
{
    private static readonly TimeSpan HideGrace = TimeSpan.FromMilliseconds(700);

    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly CardWindow _card;
    private readonly DispatcherTimer _hideTimer;
    private CancellationTokenSource? _cts;
    private string? _currentId;
    private string? _currentUrl;
    private bool _hoverEnded;
    private bool _manual;

    public HoverCoordinator(AppServices services, Dispatcher dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;
        _card = new CardWindow { Left = -10000, Top = -10000 };
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = HideGrace };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); TryAutoHide(); };

        _card.MouseEnter += (_, _) => _hideTimer.Stop();
        _card.MouseLeave += (_, _) => { if (_hoverEnded) ScheduleHide(); };
        _card.CloseRequested += Hide;
        _card.CancelRequested += Hide;
        _card.ResummarizeRequested += () => { if (_currentUrl != null) StartManual(_currentUrl, force: true); };
        _card.OpenRequested += () => OpenInBrowser(_currentUrl);
        _card.CopyRequested += CopyToClipboard;
        _card.IgnoreRequested += IgnoreCurrent;
        _card.SettingsRequested += () => SettingsRequested?.Invoke();
        _card.PinChanged += pinned => { if (!pinned && _hoverEnded) ScheduleHide(); };
    }

    public event Action? SettingsRequested;
    /// <summary>新しい要約結果ができた(最近の要約一覧の更新用)。</summary>
    public event Action? ResultProduced;

    /// <summary>ブリッジからのメッセージ(どのスレッドからでも可)。</summary>
    public void OnBridgeMessage(BrowserMessage message) => _dispatcher.BeginInvoke(() => Handle(message));

    private string? _localRequestId;

    /// <summary>拡張なしのホバー検出(UI オートメーション)からの通知。どのスレッドからでも可。</summary>
    public void OnLocalHover(string url, string? linkText) => _dispatcher.BeginInvoke(() =>
    {
        _localRequestId = Guid.NewGuid().ToString("N");
        Handle(new BrowserMessage { Type = "hoverLink", RequestId = _localRequestId, Url = url, LinkText = linkText });
    });

    public void OnLocalHoverEnd() => _dispatcher.BeginInvoke(() =>
    {
        if (_localRequestId != null) Handle(new BrowserMessage { Type = "hoverEnd", RequestId = _localRequestId });
    });

    private void Handle(BrowserMessage m)
    {
        switch (m.Type)
        {
            case "hoverLink":
                OnHoverLink(m);
                break;
            case "hoverEnd":
                if (m.RequestId == _currentId && !_manual)
                {
                    _hoverEnded = true;
                    ScheduleHide();
                }
                break;
            case "dismiss":
                if (_card.Phase != CardPhase.Consent) Hide();
                break;
        }
    }

    private void OnHoverLink(BrowserMessage m)
    {
        var settings = _services.Settings;
        if (settings.Paused || m.Url == null) return;
        if (UrlPolicy.TryNormalize(m.Url, out var normalized, out _) && _services.IsIgnored(normalized!)) return;

        // 送信確認中のカードは、別のリンクを通過しただけでは消さない
        if (_card.IsVisible && _card.Phase == CardPhase.Consent && _card.IsMouseOver) return;

        // 同じリンクに戻ってきた場合は再取得しない
        if (_card.IsVisible && normalized != null && normalized.ToString() == _currentUrl && _card.Phase != CardPhase.Error)
        {
            _currentId = m.RequestId;
            _hoverEnded = false;
            _hideTimer.Stop();
            return;
        }
        Start(new SummaryRequest(m.Url, m.RequestId!, LinkText: m.LinkText), manual: false);
    }

    /// <summary>手動入力・再要約・履歴から。</summary>
    public void StartManual(string url, bool force = false)
        => Start(new SummaryRequest(url, Guid.NewGuid().ToString("N"), ForceRefresh: force), manual: true);

    /// <summary>キャッシュ済みの要約(最近の要約一覧から)を表示する。</summary>
    public void ShowCached(SummaryCard card)
    {
        CancelCurrent();
        _currentId = Guid.NewGuid().ToString("N");
        _currentUrl = card.Url;
        _manual = true;
        _hoverEnded = false;
        _card.SetPinned(false);
        _card.ApplyState(new CardState
        {
            Phase = CardPhase.Result, Url = card.Url, Domain = card.Domain, Kind = card.Kind, Title = card.Title, Card = card, FromCache = true,
        });
        _card.ShowNearCursor(_services.Settings.CardSide);
    }

    private void Start(SummaryRequest request, bool manual)
    {
        CancelCurrent();
        _hideTimer.Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _currentId = request.RequestId;
        _manual = manual;
        _hoverEnded = false;
        _currentUrl = UrlPolicy.TryNormalize(request.Url, out var n, out _) ? n!.ToString() : request.Url;
        _card.SetPinned(_services.Settings.DisplayMode == DisplayMode.Pinned);

        var ui = new UiAdapter(this, request.RequestId);
        _card.ApplyState(new CardState
        {
            Phase = CardPhase.Loading, Url = _currentUrl, Domain = n != null ? UrlPolicy.DisplayDomain(n) : string.Empty,
            Title = request.LinkText, StatusText = "ページ情報を確認中…",
        });
        _card.ShowNearCursor(_services.Settings.CardSide);

        _ = Task.Run(async () =>
        {
            try
            {
                await _services.Pipeline.RunAsync(request, ui, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Error("pipeline crashed", ex);
                ui.Show(new CardState { Phase = CardPhase.Error, Url = request.Url, Domain = string.Empty, Error = ErrorCode.Internal });
            }
        });
    }

    private void CancelCurrent()
    {
        _card.CancelConsent();
        var old = _cts;
        _cts = null;
        if (old != null)
        {
            old.Cancel();
            old.Dispose();
        }
    }

    private void ScheduleHide()
    {
        _hideTimer.Stop();
        if (CanAutoHide()) _hideTimer.Start();
    }

    private bool CanAutoHide()
        => _card.IsVisible && !_manual && !_card.IsPinned && _services.Settings.DisplayMode != DisplayMode.Pinned
           && _card.Phase != CardPhase.Consent && !_card.IsMouseOver && !_card.IsKeyboardFocusWithin;

    private void TryAutoHide()
    {
        if (CanAutoHide()) Hide();
    }

    public void Hide()
    {
        _hideTimer.Stop();
        CancelCurrent();
        _currentId = null;
        _currentUrl = null;
        _card.HideCard();
    }

    private void Apply(string requestId, CardState state)
    {
        // 古い要求の結果は反映しない(stale response rejection)
        if (requestId != _currentId) return;
        _card.ApplyState(state);
        if (state.Phase == CardPhase.Result && state.Card?.HasSummary == true && !state.FromCache) ResultProduced?.Invoke();
        if (_hoverEnded && state.Phase != CardPhase.Consent) ScheduleHide();
    }

    private Task<ConsentDecision> AskConsent(string requestId, ConsentInfo info, CancellationToken ct)
    {
        if (requestId != _currentId) return Task.FromResult(ConsentDecision.Decline);
        _hideTimer.Stop();
        return _card.AskConsentAsync(info, ct);
    }

    // ---------- カードの操作 ----------

    public static void OpenInBrowser(string? url)
    {
        // http/https として検証できた URL だけを既定のブラウザで開く
        if (!UrlPolicy.TryNormalize(url, out var normalized, out _)) return;
        try
        {
            Process.Start(new ProcessStartInfo(normalized!.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("open url failed", ex);
        }
    }

    private void CopyToClipboard()
    {
        var card = _card.State?.Card;
        if (card == null) return;
        var text = card.Title + Environment.NewLine + string.Join(Environment.NewLine, card.SummaryLines);
        if (card.KeyPoints.Count > 0)
            text += Environment.NewLine + string.Join(Environment.NewLine, card.KeyPoints.Select(p => "・" + p));
        if (card.SearchResults.Count > 0)
            text += Environment.NewLine + string.Join(Environment.NewLine, card.SearchResults.Select((r, i) => $"{i + 1}. {r.Title} {r.Url}"));
        text += Environment.NewLine + card.Url;
        try { Clipboard.SetText(text); }
        catch (Exception ex) { AppLog.Warn($"clipboard failed {ex.GetType().Name}"); }
    }

    private void IgnoreCurrent()
    {
        if (_currentUrl == null) return;
        var url = _currentUrl;
        _services.UpdateSettings(s =>
        {
            if (!s.IgnoredUrls.Contains(url)) s.IgnoredUrls = s.IgnoredUrls.Append(url).ToList();
        });
        Hide();
    }

    /// <summary>パイプライン(バックグラウンド)から UI への橋渡し。</summary>
    private sealed class UiAdapter : IPipelineUi
    {
        private readonly HoverCoordinator _owner;
        private readonly string _requestId;

        public UiAdapter(HoverCoordinator owner, string requestId)
        {
            _owner = owner;
            _requestId = requestId;
        }

        public void Show(CardState state) => _owner._dispatcher.BeginInvoke(() => _owner.Apply(_requestId, state));

        public Task<ConsentDecision> RequestConsentAsync(ConsentInfo info, CancellationToken ct)
            => _owner._dispatcher.InvokeAsync(() => _owner.AskConsent(_requestId, info, ct)).Task.Unwrap();
    }
}
