using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using UrlInsight.App.Platform;
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
/// - 送信確認中・カード操作中は自動で閉じない
/// - 要約ができたカード(設定 AutoPinSummaries)と「固定」を押したカードは独立したカードになり、別のリンクにカーソルを重ねても消えない
///   (以後のホバーは新しいカードに表示する。固定したカードは自分の × か固定解除で閉じる)
/// </summary>
internal sealed class HoverCoordinator
{
    private static readonly TimeSpan HideGrace = TimeSpan.FromMilliseconds(700);

    /// <summary>カード1枚分の状態(ウィンドウ・表示中の URL・実行中の要約処理)。</summary>
    private sealed class CardSession
    {
        public CardSession(CardWindow window) => Window = window;
        public CardWindow Window { get; }
        public string? RequestId { get; set; }
        public string? Url { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        /// <summary>最後に表示できた要約(固定カードの保存に使う。再要約の途中でも前の内容を保存できるように)。</summary>
        public SummaryCard? LastCard { get; set; }
        /// <summary>カーソルを重ねた時点で自動で固定し、まだ結果が出ていないカード(エラーになったら自動で閉じる)。</summary>
        public bool AutoPinnedPending { get; set; }
    }

    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _hideTimer;
    /// <summary>ホバーに合わせて内容が切り替わるカード。</summary>
    private CardSession _active;
    /// <summary>固定されて独立したカード。</summary>
    private readonly List<CardSession> _pinned = new();
    private bool _hoverEnded;
    private bool _manual;
    /// <summary>固定カードの保存先(再起動後に復元する)。</summary>
    private readonly PinnedCardStore _store;
    /// <summary>移動中など短時間に何度も変わるため、少し待ってからまとめて保存する。</summary>
    private readonly DispatcherTimer _saveTimer;
    /// <summary>終了処理中は保存しない(終了時にカードを閉じても、保存した固定カードを消さないため)。</summary>
    private bool _savingSuspended;

    public HoverCoordinator(AppServices services, Dispatcher dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = HideGrace };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); TryAutoHide(); };
        _store = new PinnedCardStore(Path.Combine(services.Paths.Root, "pinned-cards.json"));
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SavePinnedNow(); };
        _active = CreateSession();
    }

    /// <summary>前回保存した固定カードを、保存した位置に表示し直す(起動時に1回呼ぶ)。</summary>
    public void RestorePinned()
    {
        List<PinnedCardEntry> entries;
        try
        {
            entries = _store.Load();
        }
        catch (Exception ex)
        {
            AppLog.Error("pinned cards load failed", ex);
            return;
        }
        foreach (var e in entries)
        {
            try
            {
                var card = e.Card;
                var s = CreateSession();
                s.LastCard = card;
                s.Url = card.Url;
                s.RequestId = NewId();
                s.Window.SetPinned(true);
                s.Window.ApplyState(new CardState
                {
                    Phase = CardPhase.Result, Url = card.Url, Domain = card.Domain, Kind = card.Kind, Title = card.Title, Card = card,
                    FromCache = true,
                });
                _pinned.Add(s);
                s.Window.ShowAt(e.Left, e.Top);
            }
            catch (Exception ex)
            {
                AppLog.Error("pinned card restore failed", ex);
            }
        }
        if (entries.Count > 0) AppLog.Info($"pinned cards restored: {entries.Count}");
    }

    private void ScheduleSave()
    {
        if (_savingSuspended) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SavePinnedNow()
    {
        if (_savingSuspended) return;
        try
        {
            _store.Save(_pinned.Where(p => p.LastCard != null && p.Window.IsVisible).Select(p =>
            {
                var rect = p.Window.ScreenRect;
                return new PinnedCardEntry { Card = p.LastCard!, Left = rect.Left, Top = rect.Top };
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("pinned cards save failed", ex);
        }
    }

    public event Action? SettingsRequested;
    /// <summary>新しい要約結果ができた(最近の要約一覧の更新用)。</summary>
    public event Action? ResultProduced;

    private CardSession CreateSession()
    {
        var s = new CardSession(new CardWindow { Left = -10000, Top = -10000 });
        var w = s.Window;
        w.MouseEnter += (_, _) => { if (s == _active) _hideTimer.Stop(); };
        w.MouseLeave += (_, _) => { if (s == _active && _hoverEnded) ScheduleHide(); };
        w.CloseRequested += () => Close(s);
        w.CancelRequested += () => Close(s);
        w.ResummarizeRequested += () =>
        {
            if (s.Url != null) Start(s, new SummaryRequest(s.Url, NewId(), ForceRefresh: true), manual: true);
        };
        w.OpenRequested += () => OpenInBrowser(s.Url);
        w.CopyRequested += () => CopyToClipboard(s);
        w.IgnoreRequested += () => Ignore(s);
        w.SettingsRequested += () => SettingsRequested?.Invoke();
        w.PinChanged += pinned => OnPinChanged(s, pinned);
        // 固定カードを動かしたら、新しい位置を保存する
        w.LocationChanged += (_, _) => { if (s != _active && _pinned.Contains(s)) ScheduleSave(); };
        return s;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>ブリッジからのメッセージ(どのスレッドからでも可)。</summary>
    public void OnBridgeMessage(BrowserMessage message) => _dispatcher.BeginInvoke(() => Handle(message));

    private string? _localRequestId;

    /// <summary>拡張なしのホバー検出(UI オートメーション)からの通知。どのスレッドからでも可。</summary>
    public void OnLocalHover(string url, string? linkText) => _dispatcher.BeginInvoke(() =>
    {
        _localRequestId = NewId();
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
                if (m.RequestId == _active.RequestId && !_manual)
                {
                    _hoverEnded = true;
                    ScheduleHide();
                }
                break;
            case "dismiss":
                // 固定したカードは閉じない
                if (_active.Window.Phase != CardPhase.Consent) Hide();
                break;
        }
    }

    private void OnHoverLink(BrowserMessage m)
    {
        var settings = _services.Settings;
        if (settings.Paused || m.Url == null) return;
        if (UrlPolicy.TryNormalize(m.Url, out var normalized, out _) && _services.IsIgnored(normalized!)) return;

        var card = _active.Window;
        // 送信確認中のカードは、別のリンクを通過しただけでは消さない
        if (card.IsVisible && card.Phase == CardPhase.Consent && card.IsMouseOver) return;

        // 同じリンクに戻ってきた場合は再取得しない
        if (card.IsVisible && normalized != null && normalized.ToString() == _active.Url && card.Phase != CardPhase.Error)
        {
            _active.RequestId = m.RequestId;
            _hoverEnded = false;
            _hideTimer.Stop();
            return;
        }
        // 固定したカードに表示中のリンクなら、新しいカードは出さない(同じ内容が2枚並ばないように)
        if (normalized != null && _pinned.Any(p => p.Url == normalized.ToString())) return;

        Start(_active, new SummaryRequest(m.Url, m.RequestId!, LinkText: m.LinkText), manual: false);
    }

    /// <summary>手動入力・履歴から。</summary>
    public void StartManual(string url, bool force = false)
        => Start(_active, new SummaryRequest(url, NewId(), ForceRefresh: force), manual: true);

    /// <summary>キャッシュ済みの要約(最近の要約一覧から)を表示する。</summary>
    public void ShowCached(SummaryCard card)
    {
        Cancel(_active);
        _active.RequestId = NewId();
        _active.Url = card.Url;
        _active.LastCard = card;
        _manual = true;
        _hoverEnded = false;
        _active.Window.SetPinned(false);
        _active.Window.ApplyState(new CardState
        {
            Phase = CardPhase.Result, Url = card.Url, Domain = card.Domain, Kind = card.Kind, Title = card.Title, Card = card, FromCache = true,
        });
        ShowActive();
    }

    private void Start(CardSession s, SummaryRequest request, bool manual)
    {
        Cancel(s);
        var cts = new CancellationTokenSource();
        s.Cts = cts;
        s.RequestId = request.RequestId;
        s.Url = UrlPolicy.TryNormalize(request.Url, out var n, out _) ? n!.ToString() : request.Url;

        bool active = s == _active;
        if (active)
        {
            _hideTimer.Stop();
            _manual = manual;
            _hoverEnded = false;
            s.Window.SetPinned(false);
        }

        var ui = new UiAdapter(this, s, request.RequestId);
        s.Window.ApplyState(new CardState
        {
            Phase = CardPhase.Loading, Url = s.Url, Domain = n != null ? UrlPolicy.DisplayDomain(n) : string.Empty,
            Title = request.LinkText, StatusText = "ページ情報を確認中…",
        });
        // 固定したカードの再要約は、その場で内容だけを更新する
        if (active) ShowActive();

        // カーソルを重ねたカードは、その時点で自動で固定する(設定でオフにできる)。
        // 要約ができる前にカーソルが離れても消えず、要約はこのカードの中で最後まで作られて表示される
        if (active && !manual && _services.Settings.AutoPinSummaries)
        {
            s.AutoPinnedPending = true;
            s.Window.SetPinned(true);
            Detach(s);
        }

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

    /// <summary>ホバー用のカードを、固定したカードと重ならない位置に表示する。</summary>
    private void ShowActive()
    {
        var avoid = _pinned.Where(p => p.Window.IsVisible).Select(p => p.Window.ScreenRect).ToList();
        _active.Window.ShowNearCursor(_services.Settings.CardSide, avoid);
    }

    private void OnPinChanged(CardSession s, bool pinned)
    {
        if (s != _active)
        {
            // 固定を外したカードは閉じる
            if (!pinned) Close(s);
            return;
        }
        if (!pinned)
        {
            if (_hoverEnded) ScheduleHide();
            return;
        }
        if (!s.Window.IsVisible) return;
        Detach(s);
    }

    /// <summary>
    /// ホバー用のカードを固定カードとして独立させ、以後のホバーは新しいカードに表示する。
    /// 要約の途中で固定した場合も、処理はそのまま固定したカードに結果を出す。
    /// </summary>
    private void Detach(CardSession s)
    {
        _hideTimer.Stop();
        _pinned.Add(s);
        _active = CreateSession();
        _hoverEnded = false;
        _manual = false;
        AppLog.Info($"card pinned (pinned cards: {_pinned.Count})");
        ScheduleSave();
    }

    private static void Cancel(CardSession s)
    {
        s.Window.CancelConsent();
        var old = s.Cts;
        s.Cts = null;
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
    {
        var card = _active.Window;
        return card.IsVisible && !_manual && !card.IsPinned && _services.Settings.DisplayMode != DisplayMode.Pinned
               && card.Phase != CardPhase.Consent && !card.IsMouseOver && !card.IsKeyboardFocusWithin;
    }

    private void TryAutoHide()
    {
        if (CanAutoHide()) Hide();
    }

    /// <summary>ホバー用のカードを隠す(固定したカードはそのまま)。</summary>
    public void Hide() => Close(_active);

    /// <summary>固定したカードをすべて閉じる(トレイメニューから)。どのスレッドからでも可。</summary>
    public void ClosePinned() => _dispatcher.BeginInvoke(() =>
    {
        foreach (var s in _pinned.ToList()) Close(s);
    });

    /// <summary>アプリ終了時: 固定したカードも含めてすべて閉じ、実行中の処理を止める。</summary>
    public void CloseAll()
    {
        // 直前の移動などで保存待ちのものを書き込んでから、保存を止めてカードを閉じる
        if (_saveTimer.IsEnabled)
        {
            _saveTimer.Stop();
            SavePinnedNow();
        }
        _savingSuspended = true;
        foreach (var s in _pinned.ToList()) Close(s);
        Close(_active);
    }

    private void Close(CardSession s)
    {
        Cancel(s);
        s.RequestId = null;
        s.Url = null;
        if (s == _active)
        {
            _hideTimer.Stop();
            s.Window.HideCard();
            return;
        }
        _pinned.Remove(s);
        s.Window.HideCard();
        s.Window.Close();
        ScheduleSave();
    }

    private void Apply(CardSession s, string requestId, CardState state)
    {
        // 古い要求の結果は反映しない(stale response rejection)
        if (requestId != s.RequestId) return;
        s.Window.ApplyState(state);
        if (state.Phase == CardPhase.Result && state.Card != null)
        {
            s.LastCard = state.Card;
            s.AutoPinnedPending = false;
            // 固定カードの再要約や、固定後に届いた結果も保存する
            if (s != _active && _pinned.Contains(s)) ScheduleSave();
        }
        if (state.Phase == CardPhase.Result && state.Card?.HasSummary == true && !state.FromCache) ResultProduced?.Invoke();

        // 自動で固定したカードがエラーになった場合は、画面に残り続けないよう少し後に閉じる
        if (state.Phase == CardPhase.Error && s.AutoPinnedPending && s != _active) CloseLater(s);

        // 要約(または検索結果)ができたカードは自動で固定する(設定でオフにできる)。
        // ページ情報だけ・エラーのカードは、これまでどおりカーソルが離れると閉じる
        if (s == _active && s.Window.IsVisible && _services.Settings.AutoPinSummaries && state.Phase == CardPhase.Result
            && state.Card is { } card && (card.HasSummary || card.SearchResults.Count > 0))
        {
            s.Window.SetPinned(true);
            Detach(s);
            return;
        }
        if (s == _active && _hoverEnded && state.Phase != CardPhase.Consent) ScheduleHide();
    }

    private static readonly TimeSpan ErrorCardLifetime = TimeSpan.FromSeconds(5);

    /// <summary>エラーのカードを数秒後に閉じる(カードの上にカーソルがあるあいだは待つ)。</summary>
    private void CloseLater(CardSession s)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = ErrorCardLifetime };
        timer.Tick += (_, _) =>
        {
            if (!_pinned.Contains(s) || !s.AutoPinnedPending)
            {
                timer.Stop();
                return;
            }
            if (s.Window.IsMouseOver || s.Window.IsKeyboardFocusWithin) return;
            timer.Stop();
            Close(s);
        };
        timer.Start();
    }

    private Task<ConsentDecision> AskConsent(CardSession s, string requestId, ConsentInfo info, CancellationToken ct)
    {
        if (requestId != s.RequestId) return Task.FromResult(ConsentDecision.Decline);
        if (s == _active) _hideTimer.Stop();
        return s.Window.AskConsentAsync(info, ct);
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

    private static void CopyToClipboard(CardSession s)
    {
        var card = s.Window.State?.Card;
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

    private void Ignore(CardSession s)
    {
        if (s.Url == null) return;
        var url = s.Url;
        _services.UpdateSettings(settings =>
        {
            if (!settings.IgnoredUrls.Contains(url)) settings.IgnoredUrls = settings.IgnoredUrls.Append(url).ToList();
        });
        Close(s);
    }

    /// <summary>パイプライン(バックグラウンド)から UI への橋渡し。結果は要求を出したカードにだけ反映する。</summary>
    private sealed class UiAdapter : IPipelineUi
    {
        private readonly HoverCoordinator _owner;
        private readonly CardSession _session;
        private readonly string _requestId;

        public UiAdapter(HoverCoordinator owner, CardSession session, string requestId)
        {
            _owner = owner;
            _session = session;
            _requestId = requestId;
        }

        public void Show(CardState state) => _owner._dispatcher.BeginInvoke(() => _owner.Apply(_session, _requestId, state));

        public Task<ConsentDecision> RequestConsentAsync(ConsentInfo info, CancellationToken ct)
            => _owner._dispatcher.InvokeAsync(() => _owner.AskConsent(_session, _requestId, info, ct)).Task.Unwrap();
    }
}
