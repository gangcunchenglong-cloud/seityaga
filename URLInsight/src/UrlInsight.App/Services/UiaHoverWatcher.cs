using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using UrlInsight.App.Platform;
using UrlInsight.Core.Diagnostics;
using UrlInsight.Core.Net;
using UrlInsight.Core.Storage;

namespace UrlInsight.App.Services;

/// <summary>
/// 拡張機能なしのホバー検出。Windows の UI オートメーション(読み上げソフト等が使う仕組み)で、
/// カーソルが一定時間止まった位置の要素を調べ、リンクなら飛び先 URL を、URL の文字列ならその URL を取り出す。
/// - カーソルが止まったときに1回だけ調べる(動いている間は何もしない。常時の画面読み取りはしない)
/// - 自分自身のウィンドウ、パスワード欄は対象外
/// - 読み取った内容はログに残さない
/// UI スレッドを止めないよう専用スレッドで動く。
/// </summary>
internal sealed class UiaHoverWatcher : IDisposable
{
    private const int PollMs = 100;
    private const int StillTolerancePx = 3;
    private const int LeaveDistancePx = 14;

    private readonly Func<AppSettings> _settings;
    private readonly int _ownPid = Environment.ProcessId;
    private Thread? _thread;
    private volatile bool _stop;

    public UiaHoverWatcher(Func<AppSettings> settings) => _settings = settings;

    /// <summary>URL を検出した(url, リンク文字列)。専用スレッドから呼ばれる。</summary>
    public event Action<string, string?>? LinkHovered;
    /// <summary>検出した位置からカーソルが離れた。</summary>
    public event Action? HoverEnded;

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "URLInsight UIA hover" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Loop()
    {
        var last = new NativeMethods.POINT();
        var firedAt = new NativeMethods.POINT();
        long stillSince = Environment.TickCount64;
        bool queried = false, active = false;

        while (!_stop)
        {
            Thread.Sleep(PollMs);
            try
            {
                var s = _settings();
                if (!s.UseUiAutomationHover || s.Paused)
                {
                    if (active) { active = false; HoverEnded?.Invoke(); }
                    continue;
                }
                if (!NativeMethods.GetCursorPos(out var pt)) continue;

                long now = Environment.TickCount64;
                if (Math.Abs(pt.X - last.X) > StillTolerancePx || Math.Abs(pt.Y - last.Y) > StillTolerancePx)
                {
                    last = pt;
                    stillSince = now;
                    queried = false;
                    if (active && (Math.Abs(pt.X - firedAt.X) > LeaveDistancePx || Math.Abs(pt.Y - firedAt.Y) > LeaveDistancePx))
                    {
                        active = false;
                        HoverEnded?.Invoke();
                    }
                    continue;
                }

                // 止まってから待ち時間が経ったら、その位置で1回だけ調べる
                if (queried || now - stillSince < s.HoverDelayMs) continue;
                queried = true;

                var hit = FindUrlAt(pt);
                if (hit is { } h)
                {
                    active = true;
                    firedAt = pt;
                    LinkHovered?.Invoke(h.Url, h.Text);
                }
            }
            catch (Exception ex)
            {
                // 1回の失敗で監視を止めない
                AppLog.Debug($"uia hover: {ex.GetType().Name}");
            }
        }
    }

    private (string Url, string? Text)? FindUrlAt(NativeMethods.POINT pt)
    {
        try
        {
            var element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
            if (element == null) return null;
            var info = element.Current;
            if (info.ProcessId == _ownPid || info.IsPassword) return null;

            // 1) リンク要素(ブラウザのリンクは子のテキスト要素の親がリンク)
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? current = element;
            for (int depth = 0; depth < 5 && current != null; depth++)
            {
                var c = current.Current;
                if (c.ControlType == ControlType.Hyperlink)
                {
                    string? value = null;
                    if (current.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                        value = ((ValuePattern)vp).Current.Value;
                    var url = LinkTextDetector.FromLinkValue(value) ?? LinkTextDetector.FindSingleUrl(c.Name);
                    return url == null ? null : (url, string.IsNullOrWhiteSpace(c.Name) ? null : c.Name);
                }
                // URL がそのまま書かれた短いテキスト要素
                if (depth == 0 && (c.ControlType == ControlType.Text || c.ControlType == ControlType.ListItem ||
                                   c.ControlType == ControlType.DataItem || c.ControlType == ControlType.TreeItem))
                {
                    var url = LinkTextDetector.FindSingleUrl(c.Name);
                    if (url != null) return (url, null);
                }
                current = walker.GetParent(current);
            }

            // 2) 文書中に書かれた URL 文字列(メモ帳・ブラウザの本文など、TextPattern に対応したもの)
            return FindUrlInText(element, pt);
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        catch (ArgumentException) { }
        return null;
    }

    private static (string Url, string? Text)? FindUrlInText(AutomationElement element, NativeMethods.POINT pt)
    {
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? current = element;
        for (int depth = 0; depth < 6 && current != null; depth++)
        {
            if (current.TryGetCurrentPattern(TextPattern.Pattern, out var p))
            {
                var tp = (TextPattern)p;
                var at = tp.RangeFromPoint(new System.Windows.Point(pt.X, pt.Y));
                var line = at.Clone();
                line.ExpandToEnclosingUnit(TextUnit.Line);
                var lineText = line.GetText(2000);
                var before = line.Clone();
                before.MoveEndpointByRange(TextPatternRangeEndpoint.End, at, TextPatternRangeEndpoint.Start);
                int offset = before.GetText(2000).Length;
                var url = LinkTextDetector.UrlAtOffset(lineText, offset);
                return url == null ? null : (url, null);
            }
            current = walker.GetParent(current);
        }
        return null;
    }

    public void Dispose() => _stop = true;
}
