using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UrlInsight.App.Platform;
using UrlInsight.Core.AI;
using UrlInsight.Core.Diagnostics;
using UIA = Interop.UIAutomationClient;

namespace UrlInsight.App.Services;

/// <summary>ブラウザごとのサイドバー AI の違い(パネルの名前・呼び出しキー)。</summary>
internal sealed record SidebarProfile(string Process, string DisplayName, string PanelKeyword, ushort[] Modifiers, ushort Key, string KeyLabel)
{
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_OEM_PERIOD = 0xBE;

    /// <summary>Chrome の Gemini(Gemini in Chrome)。Alt+G で開閉する。</summary>
    public static readonly SidebarProfile ChromeGemini = new("chrome", "Gemini", "Gemini", new[] { VK_MENU }, 0x47, "Alt+G");

    /// <summary>Edge の Copilot。Ctrl+Shift+.(ピリオド)で開閉する。</summary>
    public static readonly SidebarProfile EdgeCopilot = new("msedge", "Copilot", "Copilot", new[] { VK_CONTROL, VK_SHIFT }, VK_OEM_PERIOD, "Ctrl+Shift+.");
}

/// <summary>
/// ブラウザのサイドバーの AI(Chrome の Gemini / Edge の Copilot)に、カーソルを重ねたリンクの要約を頼む。
/// ブラウザには外からサイドバーの AI を操作する公式の仕組みが無いため、利用者の操作と同じ方法で動かす:
/// 1. パネルが閉じていれば呼び出しキー(Chrome: Alt+G、Edge: Ctrl+Shift+.)を送って開く。
///    開かなければツールバーの AI ボタンを押す(呼び出しキーは開閉の切り替えなので、すでに開いているときは送らない)
/// 2. パネル内の入力欄にキーボードの入力先を移し、入力先が本当にパネルの中かを確かめてから
///    依頼文を1文字ずつ入力して Enter を送る(確かめられなければ何も入力しない。Web ページのフォームなどへ誤って入力しないため)
/// 3. クリップボードは使わない(利用者がコピーした内容を上書きしないため)
/// 複数のリンクに続けてカーソルを重ねたときは、送信中・送信直後のものをためておき、次の送信で
/// 「次の N 件のリンク先をそれぞれ要約」とまとめて頼む(回答の途中に次の質問を送ると回答が中断されることがあるため)。
/// 同じ URL は一定時間送らない。
/// </summary>
internal sealed class SidebarAi
{
    private const int ButtonControlTypeId = 50000;
    private const int EditControlTypeId = 50004;
    private const int HyperlinkControlTypeId = 50005;
    private const int ListItemControlTypeId = 50007;
    private const int MenuItemControlTypeId = 50011;
    private const int TabItemControlTypeId = 50019;
    private const int TextControlTypeId = 50020;
    private const int InvokePatternId = 10000;

    /// <summary>1回の依頼にまとめるリンクの最大数。</summary>
    public const int MaxBatch = 5;
    /// <summary>同じ URL をもう一度送るまでの時間。</summary>
    private static readonly TimeSpan SameUrlInterval = TimeSpan.FromMinutes(2);
    /// <summary>最初のリンクのあと、続けて重ねたリンクをまとめるために少し待つ時間。</summary>
    private static readonly TimeSpan GatherDelay = TimeSpan.FromMilliseconds(800);
    /// <summary>送信のあと、AI が回答を始めるまで次の送信を待つ時間(この間に重ねたリンクは次にまとめて送る)。</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(6);

    private readonly SidebarProfile _profile;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _sent = new(StringComparer.Ordinal);
    private readonly List<(string Url, NativeMethods.POINT Cursor)> _pending = new();
    private IntPtr _root;
    private bool _running;
    private UIA.IUIAutomation? _uia;

    public SidebarAi(SidebarProfile profile) => _profile = profile;

    public event Action<string>? StatusChanged;

    private void Status(string text) => StatusChanged?.Invoke($"{_profile.DisplayName}: {text}");

    /// <summary>
    /// カーソル位置のリンクの要約を頼む(どのスレッドからでも可。処理は別スレッドで行う)。
    /// cursor はリンクが見つかったときのカーソル位置(物理ピクセル)。
    /// </summary>
    public void Request(string url, NativeMethods.POINT cursor)
    {
        var root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(cursor), NativeMethods.GA_ROOT);
        lock (_lock)
        {
            if (_sent.TryGetValue(url, out var at) && DateTime.UtcNow - at < SameUrlInterval) return;
            if (_pending.Any(p => p.Url == url)) return;
            // 別のウィンドウに移ったら、前のウィンドウ向けにためていたものは捨てる
            if (root != _root) _pending.Clear();
            _root = root;
            _pending.Add((url, cursor));
            // ためすぎたら古いものから捨てる(最近重ねたリンクを優先)
            while (_pending.Count > MaxBatch) _pending.RemoveAt(0);
            if (_pending.Count > 1) Status($"{_pending.Count}件のリンクをまとめて頼む準備をしています…");
            if (_running) return;
            _running = true;
        }
        _ = Task.Run(Worker);
    }

    private void Worker()
    {
        while (true)
        {
            Thread.Sleep(GatherDelay);
            List<(string Url, NativeMethods.POINT Cursor)> batch;
            IntPtr root;
            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    _running = false;
                    return;
                }
                batch = _pending.ToList();
                _pending.Clear();
                root = _root;
            }
            try
            {
                var sentUrls = Run(batch, root);
                if (sentUrls.Count > 0)
                {
                    lock (_lock)
                    {
                        if (_sent.Count > 300) _sent.Clear();
                        foreach (var u in sentUrls) _sent[u] = DateTime.UtcNow;
                    }
                    Thread.Sleep(Cooldown);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
            {
                AppLog.Warn($"sidebar ai ({_profile.Process}): failed ({ex.GetType().Name} 0x{ex.HResult:X8})");
                Status("操作に失敗しました（ログを確認してください）");
            }
        }
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

    /// <returns>送信できたリンク(送れなかったときは空)。</returns>
    private List<string> Run(List<(string Url, NativeMethods.POINT Cursor)> batch, IntPtr root)
    {
        var none = new List<string>();
        // 利用者が別のアプリを操作中のときにキー入力を送らない
        if (!IsForeground(root))
        {
            Status("ブラウザが前面にないため送りませんでした");
            return none;
        }
        var uia = Uia();
        var window = uia.ElementFromHandle(root);
        var panel = FindPanel(window);

        // AI の回答の中のリンクにカーソルを重ねたものは頼まない(連鎖して送り続けないため)
        if (panel != null)
        {
            var r = panel.CurrentBoundingRectangle;
            batch = batch.Where(b => !Contains(r, b.Cursor)).ToList();
            if (batch.Count == 0) return none;
        }

        if (panel == null)
        {
            Status("サイドバーを開いています…");
            panel = OpenPanel(window);
            if (panel == null)
            {
                AppLog.Info($"sidebar ai ({_profile.Process}): panel not found");
                Status(_profile == SidebarProfile.EdgeCopilot
                    ? "サイドバーを開けませんでした（Edge の設定 →「Copilot と AI」でツールバーの Copilot ボタンと Ctrl+Shift+. が有効か確認してください）"
                    : "サイドバーを開けませんでした（Chrome の設定で「Gemini in Chrome」と Alt+G のショートカットが有効か確認してください）");
                return none;
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

        // 入力先がパネルの中であることを確かめてから入力する
        if (!FocusIsInside(uia, panel) || !IsForeground(root))
        {
            AppLog.Info($"sidebar ai ({_profile.Process}): focus not in panel (input found: {input != null})");
            Status("入力欄を確認できなかったため、送信しませんでした");
            return none;
        }

        var urls = batch.Select(b => b.Url).ToList();
        var prompt = SidebarPrompt.Build(urls);
        var keys = new List<NativeMethods.INPUT>(prompt.Length * 2);
        foreach (var ch in prompt)
        {
            keys.Add(NativeMethods.Unicode(ch, false));
            keys.Add(NativeMethods.Unicode(ch, true));
        }
        if (!NativeMethods.Send(keys.ToArray()))
        {
            Status("文字を入力できませんでした（管理者権限で動いているブラウザには送れません）");
            return none;
        }
        Thread.Sleep(120);
        NativeMethods.Send(NativeMethods.Key(NativeMethods.VK_RETURN, false), NativeMethods.Key(NativeMethods.VK_RETURN, true));
        AppLog.Info($"sidebar ai ({_profile.Process}): prompt sent ({urls.Count} links)");
        Status(urls.Count == 1 ? "要約を頼みました（サイドバーに表示されます）" : $"{urls.Count}件のリンクの要約をまとめて頼みました（サイドバーに表示されます）");
        return urls;
    }

    /// <summary>呼び出しキーでパネルを開く。開かなければツールバーの AI ボタンを押す。</summary>
    private UIA.IUIAutomationElement? OpenPanel(UIA.IUIAutomationElement window)
    {
        var press = new List<NativeMethods.INPUT>();
        foreach (var m in _profile.Modifiers) press.Add(NativeMethods.Key(m, false));
        press.Add(NativeMethods.Key(_profile.Key, false));
        press.Add(NativeMethods.Key(_profile.Key, true));
        foreach (var m in _profile.Modifiers.Reverse()) press.Add(NativeMethods.Key(m, true));
        if (NativeMethods.Send(press.ToArray()))
        {
            var panel = WaitForPanel(window, 16);
            if (panel != null) return panel;
        }

        // 呼び出しキーが無効・変更されている場合に備えて、ツールバーのボタンを押す
        var button = Find(window, e =>
        {
            if (e.CurrentControlType != ButtonControlTypeId) return false;
            var name = SafeName(e);
            // Web ページの中の同名のボタンは押さない(ブラウザのツールバーのボタンだけ)
            return name != null && name.Contains(_profile.PanelKeyword, StringComparison.OrdinalIgnoreCase) && !IsInsideDocument(e);
        }, maxNodes: 1500);
        if (button?.GetCurrentPattern(InvokePatternId) is UIA.IUIAutomationInvokePattern invoke)
        {
            AppLog.Info($"sidebar ai ({_profile.Process}): {_profile.KeyLabel} did not open the panel; pressing toolbar button");
            invoke.Invoke();
            return WaitForPanel(window, 16);
        }
        return null;
    }

    private const int DocumentControlTypeId = 50030;

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

    private UIA.IUIAutomationElement? WaitForPanel(UIA.IUIAutomationElement window, int tries)
    {
        for (int i = 0; i < tries; i++)
        {
            Thread.Sleep(250);
            var panel = FindPanel(window);
            if (panel != null) return panel;
        }
        return null;
    }

    private static bool Contains(UIA.tagRECT r, NativeMethods.POINT p) => p.X >= r.left && p.X < r.right && p.Y >= r.top && p.Y < r.bottom;

    /// <summary>ブラウザのウィンドウ内から、名前に AI の名前(Gemini / Copilot)を含むパネル(ボタン・タブ・リンク等は除く)を探す。</summary>
    private UIA.IUIAutomationElement? FindPanel(UIA.IUIAutomationElement window)
        => Find(window, e =>
        {
            int type = e.CurrentControlType;
            if (type is ButtonControlTypeId or EditControlTypeId or HyperlinkControlTypeId or ListItemControlTypeId
                or MenuItemControlTypeId or TabItemControlTypeId or TextControlTypeId) return false;
            var name = SafeName(e);
            return name != null && name.Contains(_profile.PanelKeyword, StringComparison.OrdinalIgnoreCase);
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
