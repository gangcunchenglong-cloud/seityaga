using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UrlInsight.App.Platform;
using UrlInsight.Core.Diagnostics;
using UIA = Interop.UIAutomationClient;

namespace UrlInsight.App.Services;

/// <summary>Chrome のサイドバーで動く AI(依頼先)の情報。</summary>
internal sealed record SidePanelAi(
    string Id,
    // 画面に出す名前(例: 「Claude in Chrome」)。
    string DisplayName,
    // パネルやツールバーのボタンの名前に含まれる語。
    string Keyword,
    // パネルを開くショートカット(Ctrl/Alt + 英字)。
    ushort ShortcutModifier,
    ushort ShortcutKey,
    string ShortcutLabel,
    // true ならツールバーのボタンを先に試し、だめならショートカットを送る。
    bool ButtonFirst,
    // パネルを開けなかったときの案内。
    string SetupHint,
    Func<string, string> BuildPrompt)
{
    private static string OneLine(string url) => url.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>
    /// Claude in Chrome(Anthropic の Chrome 拡張機能)。ツールバーの「Claude」ボタン(拡張機能のアイコン)を押してサイドパネルを開く。
    /// ボタンがツールバーに固定されていないときは、Claude in Chrome の既定のショートカット Ctrl+E を送る。
    /// Claude in Chrome はタブを開いて読めるため、依頼文でリンク先を開くよう頼む。
    /// </summary>
    public static readonly SidePanelAi Claude = new(
        "claude", "Claude in Chrome", "Claude",
        NativeMethods.VK_CONTROL, 0x45 /* E */, "Ctrl+E", ButtonFirst: true,
        "Claude in Chrome のパネルを開けませんでした（Chrome に拡張機能「Claude」を入れてログインし、拡張機能のボタンをツールバーに固定してください）",
        url => $"次のリンク先のページを開いて内容を読み、日本語で3〜5文に要約してください: {OneLine(url)}");

    /// <summary>Gemini in Chrome。Alt+G(Chrome の Gemini 呼び出しキー)で開き、だめならツールバーの Gemini ボタンを押す。</summary>
    public static readonly SidePanelAi Gemini = new(
        "gemini", "Gemini", "Gemini",
        NativeMethods.VK_MENU, 0x47 /* G */, "Alt+G", ButtonFirst: false,
        "Gemini を開けませんでした（Chrome の設定で「Gemini in Chrome」と Alt+G のショートカットが有効か確認してください）",
        url => $"次のリンク先のページの内容を、日本語で3〜5文に要約してください: {OneLine(url)}");
}

/// <summary>
/// カードのボタンから、Chrome のサイドバーの AI(Claude in Chrome / Gemini in Chrome)に要約を頼む。
/// Chrome には外からサイドバーの AI を操作する公式の仕組みが無いため、利用者の操作と同じ方法で動かす:
/// 1. 起動中の Chrome のウィンドウを前面に出す(最小化されていれば元に戻す)
/// 2. AI のパネルが閉じていれば、ツールバーのボタンを押すかショートカットを送って開く
///    (ショートカットは開閉の切り替えなので、すでに開いているときは送らない)
/// 3. パネル内の入力欄にキーボードの入力先を移し、入力先が本当にパネルの中かを確かめてから
///    依頼文を1文字ずつ入力して Enter を送る(確かめられなければ何も入力しない。Web ページのフォームなどへ誤って入力しないため)
/// クリップボードは使わない(利用者がコピーした内容を上書きしないため)。
/// ボタンを押したときだけ動く(カーソルを重ねただけでは動かない)。
/// </summary>
internal sealed class ChromeSidePanelAi
{
    private const int ButtonControlTypeId = 50000;
    private const int EditControlTypeId = 50004;
    private const int HyperlinkControlTypeId = 50005;
    private const int ListItemControlTypeId = 50007;
    private const int MenuItemControlTypeId = 50011;
    private const int TabItemControlTypeId = 50019;
    private const int TextControlTypeId = 50020;
    private const int DocumentControlTypeId = 50030;
    private const int InvokePatternId = 10000;

    private int _busy;
    private UIA.IUIAutomation? _uia;

    /// <summary>
    /// URL の要約を Chrome のサイドバーの AI に頼む(UI スレッドから呼ぶ。操作は別スレッドで行う)。
    /// 結果の説明は status に渡す(どのスレッドから呼ばれるかは決まっていない)。
    /// </summary>
    public void Request(SidePanelAi ai, string url, Action<string> status)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            status("Chrome の AI に送信中です。少し待ってください");
            return;
        }
        // 前面に出す操作は、利用者がボタンを押した直後(このアプリが前面にあるうち)に行う
        var chrome = FindChromeWindow();
        if (chrome == IntPtr.Zero)
        {
            Interlocked.Exchange(ref _busy, 0);
            status("Chrome が起動していません。Chrome を開いてからもう一度押してください");
            return;
        }
        if (NativeMethods.IsIconic(chrome)) NativeMethods.ShowWindow(chrome, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(chrome);
        status($"{ai.DisplayName} を開いています…");

        _ = Task.Run(() =>
        {
            try
            {
                status(Run(ai, url, chrome));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
            {
                AppLog.Warn($"{ai.Id}: failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
                status($"{ai.DisplayName} の操作に失敗しました（ログを確認してください）");
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    /// <summary>表示中の Chrome のウィンドウ(いちばん手前のもの)。</summary>
    private static IntPtr FindChromeWindow()
    {
        var handles = new HashSet<IntPtr>();
        foreach (var p in Process.GetProcessesByName("chrome"))
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero) handles.Add(p.MainWindowHandle);
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return handles.FirstOrDefault();
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

    /// <returns>結果の説明。</returns>
    private string Run(SidePanelAi ai, string url, IntPtr root)
    {
        // 前面に出るまで少し待つ。出なければキー入力を送らない(別のアプリに入力してしまうため)
        for (int i = 0; i < 10 && !IsForeground(root); i++) Thread.Sleep(100);
        if (!IsForeground(root)) return "Chrome を前面に出せませんでした。Chrome を手前に表示してからもう一度押してください";

        var uia = Uia();
        var window = uia.ElementFromHandle(root);
        var panel = FindPanel(ai, window) ?? OpenPanel(ai, window);
        if (panel == null)
        {
            AppLog.Info($"{ai.Id}: panel not found");
            return ai.SetupHint;
        }

        // 開いた直後は入力欄の準備に少し時間がかかる
        UIA.IUIAutomationElement? input = null;
        for (int i = 0; i < 16 && input == null; i++)
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

        // 入力先がパネルの中であることを確かめてから入力する
        if (!FocusIsInside(uia, panel) || !IsForeground(root))
        {
            AppLog.Info($"{ai.Id}: focus not in panel (input found: {input != null})");
            return $"{ai.DisplayName} の入力欄を確認できなかったため、送信しませんでした（ログインしているか確認してください）";
        }

        var keys = new List<NativeMethods.INPUT>();
        foreach (var ch in ai.BuildPrompt(url))
        {
            keys.Add(NativeMethods.Unicode(ch, false));
            keys.Add(NativeMethods.Unicode(ch, true));
        }
        if (!NativeMethods.Send(keys.ToArray())) return "文字を入力できませんでした（管理者権限で動いている Chrome には送れません）";
        Thread.Sleep(120);
        NativeMethods.Send(NativeMethods.Key(NativeMethods.VK_RETURN, false), NativeMethods.Key(NativeMethods.VK_RETURN, true));
        AppLog.Info($"{ai.Id}: prompt sent");
        return $"{ai.DisplayName} に要約を頼みました（Chrome のサイドバーに表示されます）";
    }

    /// <summary>ツールバーのボタンかショートカットでパネルを開く(順番は依頼先ごとに決める)。</summary>
    private UIA.IUIAutomationElement? OpenPanel(SidePanelAi ai, UIA.IUIAutomationElement window)
    {
        if (ai.ButtonFirst)
            return OpenByButton(ai, window) ?? OpenByShortcut(ai, window);
        return OpenByShortcut(ai, window) ?? OpenByButton(ai, window);
    }

    private UIA.IUIAutomationElement? OpenByShortcut(SidePanelAi ai, UIA.IUIAutomationElement window)
    {
        if (!NativeMethods.Send(NativeMethods.Key(ai.ShortcutModifier, false), NativeMethods.Key(ai.ShortcutKey, false),
                NativeMethods.Key(ai.ShortcutKey, true), NativeMethods.Key(ai.ShortcutModifier, true)))
            return null;
        AppLog.Info($"{ai.Id}: sent {ai.ShortcutLabel}");
        return WaitForPanel(ai, window);
    }

    /// <summary>ツールバーの、名前に依頼先の語を含むボタンを押す(Web ページ内の同名ボタンは押さない)。</summary>
    private UIA.IUIAutomationElement? OpenByButton(SidePanelAi ai, UIA.IUIAutomationElement window)
    {
        var button = Find(window, e =>
        {
            if (e.CurrentControlType != ButtonControlTypeId) return false;
            var name = SafeName(e);
            return name != null && name.Contains(ai.Keyword, StringComparison.OrdinalIgnoreCase) && !IsInsideDocument(e);
        }, maxNodes: 1500);
        if (button?.GetCurrentPattern(InvokePatternId) is not UIA.IUIAutomationInvokePattern invoke) return null;
        AppLog.Info($"{ai.Id}: pressing toolbar button");
        invoke.Invoke();
        return WaitForPanel(ai, window);
    }

    private UIA.IUIAutomationElement? WaitForPanel(SidePanelAi ai, UIA.IUIAutomationElement window)
    {
        for (int i = 0; i < 16; i++)
        {
            Thread.Sleep(250);
            var panel = FindPanel(ai, window);
            if (panel != null) return panel;
        }
        return null;
    }

    /// <summary>
    /// Chrome のウィンドウ内から、名前に依頼先の語(「Claude」「Gemini」)を含むパネル(ボタン・タブ・リンク等は除く)を探す。
    /// 表示中のタブのページ本体(claude.ai や gemini.google.com を開いているときなど)は、
    /// サイドバーではないので除く(ウィンドウの幅の6割を超える Document はページ本体とみなす)。
    /// </summary>
    private UIA.IUIAutomationElement? FindPanel(SidePanelAi ai, UIA.IUIAutomationElement window)
    {
        var w = window.CurrentBoundingRectangle;
        int windowWidth = Math.Max(1, w.right - w.left);
        return Find(window, e =>
        {
            int type = e.CurrentControlType;
            if (type is ButtonControlTypeId or EditControlTypeId or HyperlinkControlTypeId or ListItemControlTypeId
                or MenuItemControlTypeId or TabItemControlTypeId or TextControlTypeId) return false;
            var name = SafeName(e);
            if (name == null || !name.Contains(ai.Keyword, StringComparison.OrdinalIgnoreCase)) return false;
            if (type == DocumentControlTypeId)
            {
                var r = e.CurrentBoundingRectangle;
                if (r.right - r.left > windowWidth * 0.6) return false;
            }
            return true;
        }, maxNodes: 4000);
    }

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

    private bool IsInsideDocument(UIA.IUIAutomationElement e)
    {
        var walker = Uia().ControlViewWalker;
        var current = e;
        for (int depth = 0; depth < 30 && current != null; depth++)
        {
            try
            {
                if (current.CurrentControlType == DocumentControlTypeId) return true;
                current = walker.GetParentElement(current);
            }
            catch (COMException) { return true; }
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
