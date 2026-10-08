using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UrlInsight.App.Platform;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Storage;
using UIA = Interop.UIAutomationClient;

namespace UrlInsight.App.Services;

/// <summary>
/// 拡張機能なしのホバー検出。Windows の UI オートメーション(読み上げソフト等が使う仕組み)の COM 版(UIA3)で、
/// カーソルが一定時間止まった位置の要素を調べ、リンクなら飛び先 URL を、URL の文字列ならその URL を取り出す。
/// .NET 標準の managed 版(System.Windows.Automation)は Chrome 等のページ内容を読めないことが多いため使わない。
/// - カーソルが止まったときだけ調べる(動いている間は何もしない。常時の画面読み取りはしない)
/// - 自分自身のウィンドウ、パスワード欄は対象外
/// - URL や読み取った文字列はログに残さない(要素の種類・アプリ名・結果のみ)
/// UI スレッドを止めないよう専用スレッドで動く。
/// </summary>
internal sealed class UiaHoverWatcher : IDisposable
{
    private const int PollMs = 100;
    private const int StillTolerancePx = 3;
    private const int LeaveDistancePx = 14;
    private const int RetryDelayMs = 350;
    private const int MaxAttempts = 2;

    // UI Automation の ID(UIAutomationClient.h)
    private const int HyperlinkControlTypeId = 50005;
    private const int ListItemControlTypeId = 50007;
    private const int TextControlTypeId = 50020;
    private const int TreeItemControlTypeId = 50024;
    private const int DataItemControlTypeId = 50029;
    private const int ValuePatternId = 10002;
    private const int TextPatternId = 10014;

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
    };

    private readonly Func<AppSettings> _settings;
    private readonly int _ownPid = Environment.ProcessId;
    private readonly Dictionary<int, string> _processNames = new();
    private UIA.IUIAutomation? _uia;
    private Thread? _thread;
    private volatile bool _stop;
    private string? _lastLogged;

    public UiaHoverWatcher(Func<AppSettings> settings) => _settings = settings;

    /// <summary>URL を検出した(url, リンク文字列)。専用スレッドから呼ばれる。</summary>
    public event Action<string, string?>? LinkHovered;
    /// <summary>検出した位置からカーソルが離れた。</summary>
    public event Action? HoverEnded;
    /// <summary>動作状況(画面表示用の日本語。URL は含まない)。</summary>
    public event Action<string>? StatusChanged;

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "URLInsight UIA hover" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private bool Initialize()
    {
        try
        {
            _uia = new UIA.CUIAutomation8();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            try { _uia = new UIA.CUIAutomation(); }
            catch (Exception ex2)
            {
                AppLog.Error("uia hover: UI Automation を初期化できません", ex2);
                StatusChanged?.Invoke("UIオートメーションを初期化できませんでした（ログを確認してください）");
                return false;
            }
            AppLog.Warn($"uia hover: CUIAutomation8 unavailable ({ex.GetType().Name}); using CUIAutomation");
        }
        AppLog.Info("uia hover: watcher started (UIA3)");
        StatusChanged?.Invoke("待機中: リンクやURLにカーソルを重ねて少し止めてください");
        return true;
    }

    private void Loop()
    {
        if (!Initialize()) return;

        var last = new NativeMethods.POINT();
        var firedAt = new NativeMethods.POINT();
        long stillSince = Environment.TickCount64;
        long nextAttemptAt = 0;
        int attempts = 0;
        bool active = false, wasEnabled = true;

        while (!_stop)
        {
            Thread.Sleep(PollMs);
            try
            {
                var s = _settings();
                bool enabled = s.UseUiAutomationHover && !s.Paused;
                if (!enabled)
                {
                    if (active) { active = false; HoverEnded?.Invoke(); }
                    if (wasEnabled) StatusChanged?.Invoke(s.Paused ? "一時停止中" : "オフ（設定 → 一般）");
                    wasEnabled = false;
                    continue;
                }
                if (!wasEnabled) StatusChanged?.Invoke("待機中: リンクやURLにカーソルを重ねて少し止めてください");
                wasEnabled = true;

                if (!NativeMethods.GetCursorPos(out var pt)) continue;
                long now = Environment.TickCount64;

                if (Math.Abs(pt.X - last.X) > StillTolerancePx || Math.Abs(pt.Y - last.Y) > StillTolerancePx)
                {
                    last = pt;
                    stillSince = now;
                    attempts = 0;
                    nextAttemptAt = now + s.HoverDelayMs;
                    if (active && (Math.Abs(pt.X - firedAt.X) > LeaveDistancePx || Math.Abs(pt.Y - firedAt.Y) > LeaveDistancePx))
                    {
                        active = false;
                        HoverEnded?.Invoke();
                    }
                    continue;
                }

                // 止まってから待ち時間が経ったら調べる。見つからなければ少し待って1回だけ再試行
                // (Chrome は最初の問い合わせで読み上げ用の情報を作り始めるため、1回目は空のことがある)
                if (attempts >= MaxAttempts || now < nextAttemptAt || now - stillSince < s.HoverDelayMs) continue;
                attempts++;

                var result = FindUrlAt(pt);
                if (result.Url != null)
                {
                    attempts = MaxAttempts;
                    active = true;
                    firedAt = pt;
                    Report($"{result.Process}: {result.What}を検出しました", $"found proc={result.Process} kind={result.What}");
                    LinkHovered?.Invoke(result.Url, result.LinkText);
                }
                else
                {
                    nextAttemptAt = now + RetryDelayMs;
                    if (attempts >= MaxAttempts)
                        Report(result.Process == null ? result.What : $"{result.Process}: {result.What}",
                               $"none proc={result.Process} reason={result.What}");
                }
            }
            catch (Exception ex)
            {
                // 1回の失敗で監視を止めない
                Report($"読み取りエラー（{ex.GetType().Name}）", $"error {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>画面表示とログ(内容が変わったときだけ)。</summary>
    private void Report(string display, string log)
    {
        StatusChanged?.Invoke(display);
        if (log != _lastLogged)
        {
            _lastLogged = log;
            AppLog.Info("uia hover: " + log);
        }
    }

    private readonly record struct Hit(string? Url, string? LinkText, string? Process, string What);

    private Hit FindUrlAt(NativeMethods.POINT pt)
    {
        var uia = _uia!;
        UIA.IUIAutomationElement? element;
        try
        {
            element = uia.ElementFromPoint(new UIA.tagPOINT { x = pt.X, y = pt.Y });
        }
        catch (COMException ex)
        {
            return new Hit(null, null, null, $"要素を取得できません（0x{ex.HResult:X8}）");
        }
        if (element == null) return new Hit(null, null, null, "要素がありません");

        int pid = element.CurrentProcessId;
        if (pid == _ownPid) return new Hit(null, null, "URL Insight", "自分のウィンドウ（対象外）");
        var proc = ProcessName(pid);
        if (element.CurrentIsPassword != 0) return new Hit(null, null, proc, "パスワード欄（対象外）");

        // 1) リンク要素(ブラウザのリンクは、子のテキスト要素の親がリンク)
        var walker = uia.ControlViewWalker;
        var current = element;
        string firstType = ControlTypeName(element.CurrentControlType);
        for (int depth = 0; depth < 6 && current != null; depth++)
        {
            int type = current.CurrentControlType;
            if (type == HyperlinkControlTypeId)
            {
                string? value = null;
                try
                {
                    if (current.GetCurrentPattern(ValuePatternId) is UIA.IUIAutomationValuePattern vp) value = vp.CurrentValue;
                }
                catch (COMException) { }
                var name = SafeName(current);
                var url = LinkTextDetector.FromLinkValue(value) ?? LinkTextDetector.FindSingleUrl(name);
                return url != null
                    ? new Hit(url, string.IsNullOrWhiteSpace(name) ? null : name, proc, "リンク")
                    : new Hit(null, null, proc, "リンクですが、要約できるURLではありません");
            }
            if (depth == 0 && type is TextControlTypeId or ListItemControlTypeId or DataItemControlTypeId or TreeItemControlTypeId)
            {
                var url = LinkTextDetector.FindSingleUrl(SafeName(current));
                if (url != null) return new Hit(url, null, proc, "URLの文字列");
            }
            try { current = walker.GetParentElement(current); }
            catch (COMException) { break; }
        }

        // 2) 文書中に書かれた URL 文字列(メモ帳・ブラウザの本文など、TextPattern に対応したもの)
        var textUrl = FindUrlInText(element, walker, pt);
        if (textUrl != null) return new Hit(textUrl, null, proc, "URLの文字列");

        var hint = Browsers.Contains(proc ?? "") ? "リンクではない場所" : "リンク/URLではない要素";
        return new Hit(null, null, proc, $"{hint}（{firstType}）");
    }

    private static string? FindUrlInText(UIA.IUIAutomationElement element, UIA.IUIAutomationTreeWalker walker, NativeMethods.POINT pt)
    {
        var current = element;
        for (int depth = 0; depth < 8 && current != null; depth++)
        {
            try
            {
                if (current.GetCurrentPattern(TextPatternId) is UIA.IUIAutomationTextPattern tp)
                {
                    var at = tp.RangeFromPoint(new UIA.tagPOINT { x = pt.X, y = pt.Y });
                    if (at == null) return null;
                    var line = at.Clone();
                    line.ExpandToEnclosingUnit(UIA.TextUnit.TextUnit_Line);
                    var lineText = line.GetText(2000);
                    var before = line.Clone();
                    before.MoveEndpointByRange(UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, at,
                        UIA.TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
                    int offset = before.GetText(2000)?.Length ?? 0;
                    return LinkTextDetector.UrlAtOffset(lineText, offset);
                }
                current = walker.GetParentElement(current);
            }
            catch (COMException)
            {
                return null;
            }
        }
        return null;
    }

    private static string? SafeName(UIA.IUIAutomationElement e)
    {
        try { return e.CurrentName; }
        catch (COMException) { return null; }
    }

    private string? ProcessName(int pid)
    {
        if (_processNames.TryGetValue(pid, out var n)) return n;
        try { n = Process.GetProcessById(pid).ProcessName; }
        catch (Exception) { n = $"pid {pid}"; }
        if (_processNames.Count > 200) _processNames.Clear();
        _processNames[pid] = n;
        return n;
    }

    private static string ControlTypeName(int id) => id switch
    {
        50000 => "ボタン", 50004 => "入力欄", 50005 => "リンク", 50020 => "テキスト", 50025 => "カスタム",
        50030 => "ドキュメント", 50032 => "ウィンドウ", 50033 => "ペイン", 50026 => "グループ", 50006 => "画像",
        _ => $"種類{id}",
    };

    public void Dispose() => _stop = true;
}
