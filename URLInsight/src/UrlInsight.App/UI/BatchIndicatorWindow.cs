using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UrlInsight.App.Platform;

namespace UrlInsight.App.UI;

/// <summary>
/// Ctrl を押しながらリンクに重ねている間、カーソルの近くに出す小さな表示(集めたリンクの件数と一覧)。
/// クリックを素通りさせ、キーボードの入力先も奪わない(Chrome などでの操作を邪魔しないため)。
/// Ctrl を離して Gemini に送ったあとは、結果を数秒表示してから消える。
/// </summary>
internal sealed class BatchIndicatorWindow : Window
{
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _items = new();
    private readonly TextBlock _hint = new() { FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private IntPtr _hwnd;

    public BatchIndicatorWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 380;
        Left = -10000;
        Top = -10000;
        SetResourceReference(FontFamilyProperty, "UiFont");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(6),
            Child = new StackPanel { Children = { _title, _items, _hint } },
        };
        border.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AccentSolidBrush");
        Content = border;
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        long ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>集めているリンクを表示する(Ctrl を押している間)。</summary>
    public void ShowCollecting(IReadOnlyList<string> labels, bool overflowed)
    {
        _hideTimer.Stop();
        _title.Text = labels.Count == 0
            ? "Gemini にまとめて送る: リンクにカーソルを重ねてください"
            : $"Gemini にまとめて送る: {labels.Count} 件";
        _items.Children.Clear();
        for (int i = 0; i < labels.Count; i++)
            _items.Children.Add(new TextBlock
            {
                Text = $"{i + 1}. {labels[i]}",
                FontSize = 12.5,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        _hint.Text = (overflowed ? "これ以上は追加できません（上限に達しました）。" : string.Empty)
                     + "Ctrl を離すと Chrome の Gemini に送ります。ほかのキーやクリックで取り消します。";
        ShowNearCursor();
    }

    /// <summary>結果(送信した・取り消した)を数秒表示してから消す。</summary>
    public void ShowResult(string text)
    {
        _title.Text = text;
        _items.Children.Clear();
        _hint.Text = string.Empty;
        ShowNearCursor();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void HideNow()
    {
        _hideTimer.Stop();
        Hide();
    }

    private void ShowNearCursor()
    {
        if (!IsVisible) Show();
        UpdateLayout();
        if (_hwnd == IntPtr.Zero || !NativeMethods.GetCursorPos(out var pt)) return;
        var (work, scale) = NativeMethods.MonitorAt(pt);
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        int offset = (int)(18 * scale);
        int x = pt.X + offset, y = pt.Y + offset;
        if (x + rect.Width > work.Right) x = pt.X - rect.Width - offset;
        if (y + rect.Height > work.Bottom) y = pt.Y - rect.Height - offset;
        x = Math.Max(work.Left, x);
        y = Math.Max(work.Top, y);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
