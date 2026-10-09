using System;
using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace UrlInsight.App.UI;

/// <summary>通知領域(トレイ)アイコンとメニュー。</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _pauseItem;

    public TrayIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("URL Insight を開く", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("URLを貼り付けて要約…", null, (_, _) => ManualRequested?.Invoke());
        _pauseItem = new WinForms.ToolStripMenuItem("一時停止", null, (_, _) => PauseToggleRequested?.Invoke());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("固定したカードをすべて閉じる", null, (_, _) => ClosePinnedRequested?.Invoke());
        menu.Items.Add("設定", null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke());

        _icon = new WinForms.NotifyIcon
        {
            Icon = new Icon(stream, WinForms.SystemInformation.SmallIconSize),
            Text = "URL Insight",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    public event Action? OpenRequested;
    public event Action? ManualRequested;
    public event Action? PauseToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ClosePinnedRequested;
    public event Action? ExitRequested;

    public void SetPaused(bool paused)
    {
        _pauseItem.Text = paused ? "再開" : "一時停止";
        _icon.Text = paused ? "URL Insight（一時停止中）" : "URL Insight";
    }

    public void ShowBalloon(string title, string text)
        => _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
