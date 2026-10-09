using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UrlInsight.App.Platform;
using UrlInsight.Core.Diagnostics;
using UIA = Interop.UIAutomationClient;

namespace UrlInsight.App.Services;

/// <summary>
/// Chrome のサイドバーの Gemini(Gemini in Chrome)に、カーソルを重ねたリンクの要約を頼む。
/// Chrome には外から Gemini を操作する公式の仕組みが無いため、利用者の操作と同じ方法で動かす:
/// 1. Gemini のパネルが閉じていれば Alt+G(Chrome の Gemini 呼び出しキー)を送って開く
///    (Alt+G は開閉の切り替えなので、すでに開いているときは送らない)
/// 2. パネル内の入力欄にキーボードの入力先を移し、入力先が本当に Gemini の中かを確かめてから
///    依頼文を1文字ずつ入力して Enter を送る(確かめられなければ何も入力しない。Web ページのフォームなどへ誤って入力しないため)
/// 3. クリップボードは使わない(利用者がコピーした内容を上書きしないため)
/// 素通りで何度も送らないよう、同じ URL は一定時間送らず、送信の間隔もあける。
/// </summary>
internal sealed class GeminiSidebar
{
    private const int ButtonControlTypeId = 50000;
    private const int EditControlTypeId = 50004;
    private const int HyperlinkControlTypeId = 50005;
    private const int ListItemControlTypeId = 50007;
    private const int MenuItemControlTypeId = 50011;
    private const int TabItemControlTypeId = 50019;
    private const int TextControlTypeId = 50020;
    private const ushort VK_G = 0x47;

    /// <summary>同じ URL をもう一度送るまでの時間。</summary>
    private static readonly TimeSpan SameUrlInterval = TimeSpan.FromMinutes(2);
    /// <summary>送信どうしの最短間隔(リンクの上を次々に通ったときに連続で送らないため)。</summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(4);

    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _sent = new(StringComparer.Ordinal);
    private DateTime _lastSentAt = DateTime.MinValue;
    private bool _busy;
    private UIA.IUIAutomation? _uia;

    public event Action<string>? StatusChanged;

    public static string BuildPrompt(string url) => $"次のリンク先のページの内容を、日本語で3〜5文に要約してください: {url}";

    /// <summary>
    /// カーソル位置のリンクの要約を Gemini に頼む(どのスレッドからでも可。処理は別スレッドで行う)。
    /// cursor はリンクが見つかったときのカーソル位置(物理ピクセル)。
    /// </summary>
    public void Request(string url, NativeMethods.POINT cursor)
    {
        var root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(cursor), NativeMethods.GA_ROOT);
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (_busy || now - _lastSentAt < MinInterval) return;
            if (_sent.TryGetValue(url, out var at) && now - at < SameUrlInterval) return;
            _busy = true;
        }
        _ = Task.Run(() =>
        {
            try
            {
                if (Run(url, root, cursor))
                {
                    lock (_lock)
                    {
                        _lastSentAt = DateTime.UtcNow;
                        _sent[url] = _lastSentAt;
                        if (_sent.Count > 200) _sent.Clear();
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
            {
                AppLog.Warn($"gemini: failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
                StatusChanged?.Invoke("Gemini: 操作に失敗しました（ログを確認してください）");
            }
            finally
            {
                lock (_lock) _busy = false;
            }
        });
    }

    private UIA.IUIAutomation Uia()
    {
        if (_uia != null) return _uia;
        try { _uia = new UIA.CUIAutomation8(); }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { _uia = new UIA.CUIAutomation(); }
        return _uia;
    }

    private static bool IsForeground(IntPtr root)
        => root != IntPtr.Zero && NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GA_ROOT) == root;

    /// <returns>Gemini に送信できたら true。</returns>
    private bool Run(string url, IntPtr root, NativeMethods.POINT cursor)
    {
        // 利用者が別のアプリを操作中のときにキー入力を送らない
        if (!IsForeground(root))
        {
            StatusChanged?.Invoke("Gemini: Chrome が前面にないため送りませんでした");
            return false;
        }
        var uia = Uia();
        var window = uia.ElementFromHandle(root);
        var panel = FindPanel(window);

        // Gemini の回答の中のリンクにカーソルを重ねたときは、続けて頼まない(連鎖して送り続けないため)
        if (panel != null && Contains(panel.CurrentBoundingRectangle, cursor)) return false;

        if (panel == null)
        {
            StatusChanged?.Invoke("Gemini: サイドバーを開いています…");
            if (!NativeMethods.Send(NativeMethods.Key(NativeMethods.VK_MENU, false), NativeMethods.Key(VK_G, false),
                    NativeMethods.Key(VK_G, true), NativeMethods.Key(NativeMethods.VK_MENU, true)))
            {
                StatusChanged?.Invoke("Gemini: キー入力を送れませんでした（管理者権限で動いている Chrome には送れません）");
                return false;
            }
            for (int i = 0; i < 24 && panel == null; i++)
            {
                Thread.Sleep(250);
                panel = FindPanel(window);
            }
            if (panel == null)
            {
                AppLog.Info("gemini: panel not found after Alt+G");
                StatusChanged?.Invoke("Gemini: サイドバーを開けませんでした（Chrome の設定で「Gemini in Chrome」と Alt+G のショートカットが有効か確認してください）");
                return false;
            }
        }

        // 開いた直後は入力欄の準備に少し時間がかかる
        UIA.IUIAutomationElement? input = null;
        for (int i = 0; i < 12 && input == null; i++)
        {
            input = FindInput(panel);
            if (input == null) Thread.Sleep(250);
        }
        if (input != null)
        {
            try { input.SetFocus(); }
            catch (COMException) { }
            Thread.Sleep(150);
        }

        // 入力先が Gemini の中であることを確かめてから入力する
        if (!FocusIsInside(uia, panel) || !IsForeground(root))
        {
            AppLog.Info($"gemini: focus not in panel (input found: {input != null})");
            StatusChanged?.Invoke("Gemini: 入力欄を確認できなかったため、送信しませんでした");
            return false;
        }

        var prompt = BuildPrompt(url);
        var keys = new List<NativeMethods.INPUT>(prompt.Length * 2);
        foreach (var ch in prompt)
        {
            keys.Add(NativeMethods.Unicode(ch, false));
            keys.Add(NativeMethods.Unicode(ch, true));
        }
        if (!NativeMethods.Send(keys.ToArray()))
        {
            StatusChanged?.Invoke("Gemini: 文字を入力できませんでした");
            return false;
        }
        Thread.Sleep(120);
        NativeMethods.Send(NativeMethods.Key(NativeMethods.VK_RETURN, false), NativeMethods.Key(NativeMethods.VK_RETURN, true));
        AppLog.Info("gemini: prompt sent");
        StatusChanged?.Invoke("Gemini: 要約を頼みました（Chrome のサイドバーに表示されます）");
        return true;
    }

    private static bool Contains(UIA.tagRECT r, NativeMethods.POINT p) => p.X >= r.left && p.X < r.right && p.Y >= r.top && p.Y < r.bottom;

    /// <summary>Chrome のウィンドウ内から、名前に「Gemini」を含むパネル(ボタン・タブ・リンク等は除く)を探す。</summary>
    private UIA.IUIAutomationElement? FindPanel(UIA.IUIAutomationElement window)
        => Find(window, e =>
        {
            int type = e.CurrentControlType;
            if (type is ButtonControlTypeId or EditControlTypeId or HyperlinkControlTypeId or ListItemControlTypeId
                or MenuItemControlTypeId or TabItemControlTypeId or TextControlTypeId) return false;
            var name = SafeName(e);
            return name != null && name.Contains("Gemini", StringComparison.OrdinalIgnoreCase);
        }, maxNodes: 4000);

    /// <summary>パネル内の入力欄(キーボードで操作できる入力欄のうち、いちばん下にあるもの)。</summary>
    private UIA.IUIAutomationElement? FindInput(UIA.IUIAutomationElement panel)
    {
        UIA.IUIAutomationElement? best = null;
        int bestBottom = int.MinValue;
        Find(panel, e =>
        {
            if (e.CurrentControlType != EditControlTypeId || e.CurrentIsKeyboardFocusable == 0 || e.CurrentIsEnabled == 0
                || e.CurrentIsOffscreen != 0) return false;
            var r = e.CurrentBoundingRectangle;
            if (r.bottom > bestBottom)
            {
                bestBottom = r.bottom;
                best = e;
            }
            return false;
        }, maxNodes: 3000);
        return best;
    }

    private bool FocusIsInside(UIA.IUIAutomation uia, UIA.IUIAutomationElement panel)
    {
        var walker = uia.ControlViewWalker;
        var current = uia.GetFocusedElement();
        for (int depth = 0; depth < 60 && current != null; depth++)
        {
            if (uia.CompareElements(current, panel) != 0) return true;
            try { current = walker.GetParentElement(current); }
            catch (COMException) { return false; }
        }
        return false;
    }

    /// <summary>子孫を幅優先でたどり、条件に合う最初の要素を返す(大きなページで止まらないよう件数に上限)。</summary>
    private UIA.IUIAutomationElement? Find(UIA.IUIAutomationElement root, Func<UIA.IUIAutomationElement, bool> match, int maxNodes)
    {
        var walker = Uia().ControlViewWalker;
        var queue = new Queue<UIA.IUIAutomationElement>();
        queue.Enqueue(root);
        int count = 0;
        while (queue.Count > 0 && count++ < maxNodes)
        {
            var e = queue.Dequeue();
            try
            {
                if (count > 1 && match(e)) return e;
                var child = walker.GetFirstChildElement(e);
                while (child != null)
                {
                    queue.Enqueue(child);
                    child = walker.GetNextSiblingElement(child);
                }
            }
            catch (COMException)
            {
                // 途中で閉じられた要素などは読み飛ばす
            }
        }
        return null;
    }

    private static string? SafeName(UIA.IUIAutomationElement e)
    {
        try { return e.CurrentName; }
        catch (COMException) { return null; }
    }
}
