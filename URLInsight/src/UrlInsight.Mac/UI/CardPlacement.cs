using UrlInsight.Core.Storage;

namespace UrlInsight.Mac.UI;

/// <summary>画面上の長方形(Avalonia の画面座標)。</summary>
internal readonly record struct ScreenBox(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool Overlaps(ScreenBox o) => Left < o.Right && o.Left < Right && Top < o.Bottom && o.Top < Bottom && o.Width > 0;
}

/// <summary>
/// カードを置く位置の計算(Windows 版の CardWindow.PlaceAtAnchor と同じ決め方)。
/// 画面の右側(設定で左側)に置き、カーソルと重なるならカーソルの反対側へ、固定済みのカードと重なるなら画面の中央寄りへずらす。
/// </summary>
internal static class CardPlacement
{
    public static (int X, int Y) NearAnchor(int anchorX, int anchorY, ScreenBox work, int width, int height, CardSide side,
        IReadOnlyList<ScreenBox> avoid, double scale)
    {
        int margin = (int)(8 * scale);
        int gap = (int)(28 * scale);

        int x = side == CardSide.Right ? work.Right - width - margin : work.Left + margin;
        if (side == CardSide.Right && anchorX >= x - gap / 2)
            x = Math.Max(work.Left + margin, anchorX - width - gap);
        else if (side == CardSide.Left && anchorX <= x + width + gap / 2)
            x = Math.Min(work.Right - width - margin, anchorX + gap);

        int y = anchorY - height / 4;
        y = Math.Max(work.Top + margin, Math.Min(y, work.Bottom - height - margin));
        if (height > work.Height - 2 * margin) y = work.Top + margin;

        for (int i = 0; i < avoid.Count + 1; i++)
        {
            var me = new ScreenBox(x, y, width, height);
            var hit = avoid.FirstOrDefault(r => me.Overlaps(r));
            if (hit.Width == 0) break;
            int nx = side == CardSide.Right ? hit.Left - width - margin : hit.Right + margin;
            if (nx < work.Left + margin || nx + width > work.Right - margin) break;
            x = nx;
        }
        return (x, y);
    }

    /// <summary>保存した位置を、画面の中に収まるように直す(モニター構成が変わった場合など)。</summary>
    public static (int X, int Y) ClampInto(int left, int top, ScreenBox work, int width, int height, double scale)
    {
        int margin = (int)(8 * scale);
        int x = Math.Max(work.Left + margin, Math.Min(left, work.Right - width - margin));
        int y = Math.Max(work.Top + margin, Math.Min(top, work.Bottom - height - margin));
        return (x, y);
    }
}
