using System.Diagnostics;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Storage;
using UrlInsight.Mac.Platform;

namespace UrlInsight.Mac.Services;

/// <summary>
/// macOS のホバー検出。アクセシビリティ機能(AX。VoiceOver などが使う仕組み)で、
/// カーソルが一定時間止まった位置の画面要素を調べ、リンクなら飛び先 URL を、URL の文字列ならその URL を取り出す
/// (Windows 版の UiaHoverWatcher と同じ考え方)。
/// - カーソルが止まったときと1秒ごとにだけ調べる(常時の画面読み取りはしない)
/// - 自分自身のウィンドウ、パスワード欄は対象外
/// - Chrome など Chromium 系のブラウザは、そのままではページ内の要素を AX に出さないため、
///   アプリに AXManualAccessibility(支援技術の利用を知らせる設定)を1回だけ送ってから調べる
/// - URL や読み取った文字列はログに残さない
/// UI スレッドを止めないよう専用スレッドで動く。
/// </summary>
internal sealed class AxHoverWatcher : IDisposable
{
    private const int PollMs = 100;
    private const double StillTolerance = 3;
    private const double LeaveDistance = 14;
    private const int ScanIntervalMs = 1000;
    private const int ContextChars = 1000;
    /// <summary>これより長い文書は、カーソル位置の文字列の切り出しをしない(重くなるため)。</summary>
    private const int MaxDocumentChars = 2_000_000;

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Google Chrome", "Google Chrome Beta", "Google Chrome Canary", "Chromium", "Safari", "Safari Technology Preview",
        "Microsoft Edge", "Brave Browser", "Arc", "Vivaldi", "Opera", "firefox", "Firefox",
    };

    private readonly Func<AppSettings> _settings;
    private readonly int _ownPid = Environment.ProcessId;
    private readonly Dictionary<int, string> _processNames = new();
    private readonly HashSet<int> _accessibilityEnabled = new();
    private IntPtr _systemWide;
    private Thread? _thread;
    private volatile bool _stop;
    private string? _lastLogged;

    public AxHoverWatcher(Func<AppSettings> settings) => _settings = settings;

    /// <summary>URL を検出した(url, リンク文字列)。専用スレッドから呼ばれる。</summary>
    public event Action<string, string?>? LinkHovered;
    /// <summary>検出した位置からカーソルが離れた。</summary>
    public event Action? HoverEnded;
    /// <summary>動作状況(画面表示用の日本語。URL は含まない)。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>アクセシビリティの許可があるか(無いとリンクを読めない)。</summary>
    public bool Trusted { get; private set; }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "URLInsight AX hover" };
        _thread.Start();
    }

    private void Loop()
    {
        _systemWide = MacNative.AXUIElementCreateSystemWide();
        // 応答しないアプリで止まらないよう、問い合わせの待ち時間を短くする
        MacNative.AXUIElementSetMessagingTimeout(_systemWide, 0.5f);
        AppLog.Info("ax hover: watcher started");

        var last = new MacNative.CGPoint();
        var firedAt = new MacNative.CGPoint();
        long stillSince = Environment.TickCount64;
        long lastScan = 0, lastTrustCheck = 0;
        bool scannedHere = false, wasEnabled = true;
        string? activeUrl = null;

        while (!_stop)
        {
            Thread.Sleep(PollMs);
            try
            {
                long now = Environment.TickCount64;
                // 許可は利用者がシステム設定で後から与えるので、ときどき確かめ直す
                if (!Trusted && now - lastTrustCheck > 2000)
                {
                    lastTrustCheck = now;
                    Trusted = MacNative.IsAccessibilityTrusted(prompt: false);
                    if (!Trusted)
                    {
                        Report("アクセシビリティの許可がありません（システム設定 → プライバシーとセキュリティ → アクセシビリティ で URL Insight をオンにしてください）",
                            "not trusted");
                        continue;
                    }
                    Report("待機中: リンクやURLにカーソルを重ねて少し止めてください", "trusted");
                }
                if (!Trusted) continue;

                var s = _settings();
                bool enabled = s.UseUiAutomationHover && !s.Paused;
                if (!enabled)
                {
                    if (activeUrl != null) { activeUrl = null; HoverEnded?.Invoke(); }
                    if (wasEnabled) StatusChanged?.Invoke(s.Paused ? "一時停止中" : "オフ（設定 → 一般）");
                    wasEnabled = false;
                    continue;
                }
                if (!wasEnabled) StatusChanged?.Invoke("待機中: リンクやURLにカーソルを重ねてください");
                wasEnabled = true;

                var pt = MacNative.CursorPosition();
                if (Math.Abs(pt.X - last.X) > StillTolerance || Math.Abs(pt.Y - last.Y) > StillTolerance)
                {
                    last = pt;
                    stillSince = now;
                    scannedHere = false;
                    if (activeUrl != null && (Math.Abs(pt.X - firedAt.X) > LeaveDistance || Math.Abs(pt.Y - firedAt.Y) > LeaveDistance))
                    {
                        activeUrl = null;
                        HoverEnded?.Invoke();
                    }
                }

                // カーソルが止まってから待ち時間が経ったとき、それとは別に1秒ごとに調べる(Windows 版と同じ)
                bool stillReady = !scannedHere && now - stillSince >= s.HoverDelayMs;
                bool periodic = now - lastScan >= ScanIntervalMs;
                if (!stillReady && !periodic) continue;
                lastScan = now;
                scannedHere = true;

                var result = FindUrlAt(pt);
                if (result.Url != null)
                {
                    firedAt = pt;
                    Report($"{result.Process}: {result.What}を検出 → {Shorten(result.Url)}", $"found proc={result.Process} kind={result.What}");
                    if (result.Url != activeUrl)
                    {
                        activeUrl = result.Url;
                        LinkHovered?.Invoke(result.Url, result.LinkText);
                    }
                }
                else
                {
                    Report(result.Process == null ? result.What : $"{result.Process}: {result.What}",
                           $"none proc={result.Process} reason={result.What}");
                    if (activeUrl != null && !result.OwnWindow)
                    {
                        activeUrl = null;
                        HoverEnded?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                // 1回の失敗で監視を止めない
                Report($"読み取りエラー（{ex.GetType().Name}）", $"error {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static string Shorten(string url) => url.Length <= 90 ? url : url[..87] + "...";

    private void Report(string display, string log)
    {
        StatusChanged?.Invoke(display);
        if (log != _lastLogged)
        {
            _lastLogged = log;
            AppLog.Info("ax hover: " + log);
        }
    }

    internal readonly record struct Hit(string? Url, string? LinkText, string? Process, string What, bool OwnWindow = false);

    private IntPtr ElementAt(MacNative.CGPoint pt)
        => MacNative.AXUIElementCopyElementAtPosition(_systemWide, (float)pt.X, (float)pt.Y, out var e) == MacNative.AXErrorSuccess ? e : IntPtr.Zero;

    private Hit FindUrlAt(MacNative.CGPoint pt)
    {
        var element = ElementAt(pt);
        if (element == IntPtr.Zero) return new Hit(null, null, null, "要素を取得できません");

        MacNative.AXUIElementGetPid(element, out int pid);
        if (pid == _ownPid)
        {
            MacNative.CFRelease(element);
            return new Hit(null, null, "URL Insight", "自分のウィンドウ（対象外）", OwnWindow: true);
        }
        var proc = ProcessName(pid);

        // Chromium 系はページ内の要素を出してもらうよう1回だけ頼み、もう一度調べる
        if (_accessibilityEnabled.Add(pid))
        {
            var app = MacNative.AXUIElementCreateApplication(pid);
            if (app != IntPtr.Zero)
            {
                bool ok = MacNative.SetBool(app, "AXManualAccessibility", true);
                MacNative.CFRelease(app);
                if (ok)
                {
                    AppLog.Info($"ax hover: enabled AXManualAccessibility for {proc}");
                    Thread.Sleep(150);
                    var again = ElementAt(pt);
                    if (again != IntPtr.Zero)
                    {
                        MacNative.CFRelease(element);
                        element = again;
                    }
                }
            }
            if (_accessibilityEnabled.Count > 500) _accessibilityEnabled.Clear();
        }

        var held = new List<IntPtr> { element };
        try
        {
            bool browser = Browsers.Contains(proc ?? "");
            if (MacNative.GetString(element, "AXSubrole") == "AXSecureTextField") return new Hit(null, null, proc, "パスワード欄（対象外）");
            string firstRole = MacNative.GetString(element, "AXRole") ?? "不明";

            var current = element;
            for (int depth = 0; depth < 8 && current != IntPtr.Zero; depth++)
            {
                var role = MacNative.GetString(current, "AXRole");
                var hit = Inspect(current, role, depth, browser, proc);
                if (hit != null) return hit.Value;
                var parent = MacNative.GetElement(current, "AXParent");
                if (parent == IntPtr.Zero) break;
                held.Add(parent);
                current = parent;
            }

            // 文書中に書かれた URL 文字列(テキストエディット・メモなど、文字位置を返せるもの)
            for (int i = 0; i < Math.Min(held.Count, 4); i++)
            {
                var url = UrlInTextAt(held[i], pt);
                if (url != null) return new Hit(url, null, proc, "URLの文字列");
            }

            var hint = browser ? "リンクではない場所" : "リンク/URLではない要素";
            return new Hit(null, null, proc, $"{hint}（{RoleName(firstRole)}）");
        }
        finally
        {
            foreach (var e in held) MacNative.CFRelease(e);
        }
    }

    private static Hit? Inspect(IntPtr e, string? role, int depth, bool browser, string? proc)
    {
        switch (role)
        {
            case "AXLink":
            {
                var target = MacNative.GetString(e, "AXURL");
                var title = FirstNonEmpty(MacNative.GetString(e, "AXTitle"), MacNative.GetString(e, "AXDescription"));
                var url = LinkTextDetector.FromLinkValue(target) ?? LinkTextDetector.FindSingleUrl(title);
                return url != null
                    ? new Hit(url, title, proc, "リンク")
                    : new Hit(null, null, proc, "リンクですが、要約できるURLではありません");
            }
            case "AXTextField" or "AXComboBox" or "AXSearchField" when depth == 0 || (browser && depth <= 2):
            {
                // アドレスバーなどの入力欄。ブラウザでは「https://」が省かれた表示(example.com/path)も URL として扱う
                var value = MacNative.GetString(e, "AXValue");
                var bar = browser ? LinkTextDetector.FromAddressBar(value) : null;
                if (bar != null) return new Hit(bar, null, proc, "アドレスバーのURL");
                var url = LinkTextDetector.FromLinkValue(value) ?? LinkTextDetector.FindSingleUrl(value);
                return url != null ? new Hit(url, null, proc, "入力欄のURL") : null;
            }
            case "AXStaticText" or "AXCell" or "AXRow" or "AXButton" or "AXMenuItem" when depth == 0:
            {
                var text = FirstNonEmpty(MacNative.GetString(e, "AXValue"), MacNative.GetString(e, "AXTitle"), MacNative.GetString(e, "AXDescription"));
                var url = LinkTextDetector.FindSingleUrl(text);
                return url != null ? new Hit(url, null, proc, "URLの文字列") : null;
            }
            default:
                return null;
        }
    }

    /// <summary>カーソル位置の文字の前後をまとめて取り、カーソルに重なる URL を丸ごと切り出す(折り返された長い URL も取れる)。</summary>
    private static string? UrlInTextAt(IntPtr element, MacNative.CGPoint pt)
    {
        var index = MacNative.CharIndexAt(element, pt);
        if (index is not int i || i < 0) return null;
        var value = MacNative.GetString(element, "AXValue");
        if (string.IsNullOrEmpty(value) || value.Length > MaxDocumentChars || i > value.Length) return null;
        int start = Math.Max(0, i - ContextChars);
        int end = Math.Min(value.Length, i + ContextChars);
        return LinkTextDetector.UrlAtOffset(value[start..end], i - start);
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private string? ProcessName(int pid)
    {
        if (_processNames.TryGetValue(pid, out var n)) return n;
        try { n = Process.GetProcessById(pid).ProcessName; }
        catch (Exception) { n = $"pid {pid}"; }
        if (_processNames.Count > 200) _processNames.Clear();
        _processNames[pid] = n;
        return n;
    }

    private static string RoleName(string role) => role switch
    {
        "AXButton" => "ボタン", "AXTextField" => "入力欄", "AXTextArea" => "文書", "AXLink" => "リンク", "AXStaticText" => "テキスト",
        "AXWebArea" => "ページ", "AXWindow" => "ウィンドウ", "AXGroup" => "グループ", "AXImage" => "画像", "AXScrollArea" => "スクロール領域",
        "AXMenuBar" or "AXMenuBarItem" => "メニューバー", "AXDockItem" or "AXList" => "Dock・一覧",
        _ => role,
    };

    public void Dispose()
    {
        _stop = true;
    }
}
