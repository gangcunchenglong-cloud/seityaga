"""アイコン生成スクリプト(UIサンプルSVGのロゴ: 青紫グラデーションの角丸四角 + 白い矢印)。

使い方: python3 tools/make_icons.py
出力:
  browser-extension/icons/icon{16,32,48,128}.png
  src/UrlInsight.App/Assets/app.ico
必要: Pillow
"""
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SIZE = 512


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def render() -> Image.Image:
    start, end = (0x34, 0x78, 0xF6), (0x65, 0x58, 0xE8)
    grad = Image.new("RGBA", (SIZE, SIZE))
    px = grad.load()
    for y in range(SIZE):
        for x in range(SIZE):
            t = (x + y) / (2 * (SIZE - 1))
            px[x, y] = (*lerp(start, end, t), 255)

    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, SIZE - 1, SIZE - 1], radius=int(SIZE * 0.3), fill=255)
    icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    icon.paste(grad, (0, 0), mask)

    # SVG の path: 左側の半円弧 + 右向きの「>」
    d = ImageDraw.Draw(icon)
    w = int(SIZE * 0.075)
    s = SIZE / 38.0  # 元SVGの 38x38 座標系
    ox, oy = 0, 0

    def p(x, y):
        return (ox + x * s, oy + y * s)

    # 弧: 中心(18,19) 半径 9 の左半分(元SVGの弧を小さく整えたもの)
    cx, cy, r = 18 * s, 19 * s, 9 * s
    d.arc([cx - r, cy - r, cx + r, cy + r], start=90, end=270, fill="white", width=w)
    # 右向きの矢印
    pts = [p(21, 12), p(28, 19), p(21, 26)]
    d.line(pts, fill="white", width=w, joint="curve")
    for q in (pts[0], pts[2], (cx, cy - r), (cx, cy + r)):
        d.ellipse([q[0] - w / 2, q[1] - w / 2, q[0] + w / 2, q[1] + w / 2], fill="white")
    return icon


def main():
    icon = render()
    out = ROOT / "browser-extension" / "icons"
    out.mkdir(parents=True, exist_ok=True)
    for size in (16, 32, 48, 128):
        icon.resize((size, size), Image.LANCZOS).save(out / f"icon{size}.png")
    ico = ROOT / "src" / "UrlInsight.App" / "Assets" / "app.ico"
    ico.parent.mkdir(parents=True, exist_ok=True)
    # 古いAPI(System.Drawing.Icon 等)でも確実に読めるよう、各サイズを BMP 形式で格納する
    icon.save(ico, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
              bitmap_format="bmp")
    icon.resize((256, 256), Image.LANCZOS).save(ico.parent / "app256.png")
    print("icons written")


if __name__ == "__main__":
    main()
