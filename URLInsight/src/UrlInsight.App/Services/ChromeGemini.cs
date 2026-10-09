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

/// <summary>
/// カードの「Chrome の Gemini で要約」ボタンから、Chrome のサイドバーの Gemini(Gemini in Chrome)に要約を頼む。
/// Chrome には外から Gemini を操作する公式の仕組みが無いため、利用者の操作と同じ方法で動かす:
/// 1. 起動中の Chrome のウィンドウを前面に出す(最小化されていれば元に戻す)
/// 2. Gemini のパネルが閉じていれば Alt+G(Chrome の Gemini 呼び出しキー)を送って開く。
///    開かなければツールバーの Gemini ボタンを押す(Alt+G は開閉の切り替えなので、すでに開いているときは送らない)
/// 3. パネル内の入力欄にキーボードの入力先を移し、入力先が本当に Gemini の中かを確かめてから
///    依頼文を1文字ずつ入力して Enter を送る(確かめられなければ何も入力しない。Web ページのフォームなどへ誤って入力しないため)
/// クリップボードは使わない(利用者がコピーした内容を上書きしないため)。
/// ボタンを押したときだけ動く(カーソルを重ねただけでは動かない)。
/// </summary>
internal sealed class ChromeGemini
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
    private const ushort VK_G = 0x47;

    private int _busy;
    private UIA.IUIAutomation? _uia;

    /// <summary>依頼文(改行を入れると途中で送信されるため1行にする)。</summary>
    public static string BuildPrompt(string url)
        => $"次のリンク先のページの内容を、日本語で3〜5文に要約してください: {url.Replace('\r', ' ').Replace('\n', ' ').Trim()}";

    /// <summary>
    /// URL の要約を Chrome の Gemini に頼む(UI スレッドから呼ぶ。操作は別スレッドで行う)。
    /// 結果の説明は status に渡す(どのスレッドから呼ばれるかは決まっていない)。
    /// </summary>
    public void Request(string url, Action<string> status)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            status("Gemini に送信中です。少し待ってください");
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
        status("Chrome の Gemini を開いています…");

        _ = Task.Run(() =>
        {
            try
            {
                status(Run(url, chrome));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
            {
                AppLog.Warn($"gemini: failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
                status("Gemini の操作に失敗しました（ログを確認してください）");
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
    private string Run(string url, IntPtr root)
    {
        // 前面に出るまで少し待つ。出なければキー入力を送らない(別のアプリに入力してしまうため)
        for (int i = 0; i < 10 && !IsForeground(root); i++) Thread.Sleep(100);
        if (!IsForeground(root)) return "Chrome を前面に出せませんでした。Chrome を手前に表示してからもう一度押してください";

        var uia = Uia();
        var window = uia.ElementFromHandle(root);
        var panel = FindPanel(window) ?? OpenPanel(window);
        if (panel == null)
        {
            AppLog.Info("gemini: panel not found");
            return "Gemini を開けませんでした（Chrome の設定で「Gemini in Chrome」と Alt+G のショートカットが有効か確認してください）";
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
            return "Gemini の入力欄を確認できなかったため、送信しませんでした";
        }

        var keys = new List<NativeMethods.INPUT>();
        foreach (var ch in BuildPrompt(url))
        {
            keys.Add(NativeMethods.Unicode(ch, false));
            keys.Add(NativeMethods.Unicode(ch, true));
        }
        if (!NativeMethods.Send(keys.ToArray())) return "文字を入力できませんでした（管理者権限で動いている Chrome には送れません）";
        Thread.Sleep(120);
        NativeMethods.Send(NativeMethods.Key(NativeMethods.VK_RETURN, false), NativeMethods.Key(NativeMethods.VK_RETURN, true));
        AppLog.Info("gemini: prompt sent");
        return "Chrome の Gemini に要約を頼みました（Chrome のサイドバーに表示されます）";
    }

    /// <summary>Alt+G でパネルを開く。開かなければツールバーの Gemini ボタンを押す。</summary>
    private UIA.IUIAutomationElement? OpenPanel(UIA.IUIAutomationElement window)
    {
        if (NativeMethods.Send(NativeMethods.Key(NativeMethods.VK_MENU, false), NativeMethods.Key(VK_G, false),
                NativeMethods.Key(VK_G, true), NativeMethods.Key(NativeMethods.VK_MENU, true)))
        {
            var panel = WaitForPanel(window);
            if (panel != null) return panel;
        }
        // Alt+G が無効・変更されている場合に備えて、ツールバーのボタンを押す(Web ページ内の同名ボタンは押さない)
        var button = Find(window, e =>
        {
            if (e.CurrentControlType != ButtonControlTypeId) return false;
            var name = SafeName(e);
            return name != null && name.Contains("Gemini", StringComparison.OrdinalIgnoreCase) && !IsInsideDocument(e);
        }, maxNodes: 1500);
        if (button?.GetCurrentPattern(InvokePatternId) is UIA.IUIAutomationInvokePattern invoke)
        {
            AppLog.Info("gemini: Alt+G did not open the panel; pressing toolbar button");
            invoke.Invoke();
            return WaitForPanel(window);
        }
        return null;
    }

    private UIA.IUIAutomationElement? WaitForPanel(UIA.IUIAutomationElement window)
    {
        for (int i = 0; i < 16; i++)
        {
            Thread.Sleep(250);
            var panel = FindPanel(window);
            if (panel != null) return panel;
        }
        return null;
    }

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
