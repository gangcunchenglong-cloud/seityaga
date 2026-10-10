using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace UrlInsight.Mac.UI;

/// <summary>確認・お知らせの小さなダイアログ(Avalonia には標準のメッセージボックスが無いため)。</summary>
internal static class Dialog
{
    /// <summary>はい/いいえ を聞く。親ウィンドウが無いときは単独で表示する。</summary>
    public static Task<bool> Confirm(Window? owner, string message, string yes = "はい", string no = "いいえ")
        => Show(owner, message, yes, no);

    public static Task Info(Window? owner, string message) => Show(owner, message, "OK", null);

    private static async Task<bool> Show(Window? owner, string message, string yes, string? no)
    {
        var w = new Window
        {
            Title = "URL Insight",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = CardWindow.Brush("CardBrush"),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var ok = new Button { Content = yes, Classes = { "primary" }, IsDefault = true };
        ok.Click += (_, _) => w.Close(true);
        if (no != null)
        {
            var cancel = new Button { Content = no, Classes = { "secondary" }, IsCancel = true, Margin = new Thickness(0) };
            cancel.Click += (_, _) => w.Close(false);
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(ok);
        w.Content = new StackPanel
        {
            Margin = new Thickness(22, 20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                buttons,
            },
        };
        if (owner != null && owner.IsVisible) return await w.ShowDialog<bool>(owner);
        var tcs = new TaskCompletionSource<bool>();
        w.Closed += (_, _) => tcs.TrySetResult(false);
        ok.Click += (_, _) => tcs.TrySetResult(true);
        w.Show();
        return await tcs.Task;
    }
}
