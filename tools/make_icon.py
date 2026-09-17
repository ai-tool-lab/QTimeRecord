"""img/appicon.png から QTimeRecord.App/Resources/AppIcon.ico を作る。

    python tools/make_icon.py

元画像は透過を持たない（白地に角丸のタイル）。そのまま .ico にすると、
タスクバーやデスクトップに白い四角が出るため、タイルの外側だけを透過にする。

外側の判定は四隅からの塗りつぶしで行う。明るさだけで抜くと、
時計の文字盤（白に近い）まで透けてしまう。
"""

from collections import deque
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "img" / "appicon.png"
TARGET = ROOT / "QTimeRecord.App" / "Resources" / "AppIcon.ico"

# 背景は 250 以上、タイルの縁は 227〜240 程度。その間で切る。
BACKGROUND_MIN = 246

# Windows がエクスプローラー・タスクバー・タイトルバーで使う大きさ（高DPIを含む）。
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]


def cut_out_background(image: Image.Image) -> Image.Image:
    rgb = image.convert("RGB")
    width, height = rgb.size
    pixels = rgb.load()

    outside = bytearray(width * height)
    queue = deque()

    def visit(x: int, y: int) -> None:
        index = y * width + x
        if outside[index] or min(pixels[x, y]) < BACKGROUND_MIN:
            return
        outside[index] = 1
        queue.append((x, y))

    for x in range(width):
        visit(x, 0)
        visit(x, height - 1)
    for y in range(height):
        visit(0, y)
        visit(width - 1, y)

    while queue:
        x, y = queue.popleft()
        if x > 0:
            visit(x - 1, y)
        if x < width - 1:
            visit(x + 1, y)
        if y > 0:
            visit(x, y - 1)
        if y < height - 1:
            visit(x, y + 1)

    rgba = rgb.convert("RGBA")
    alpha = Image.frombytes("L", (width, height), bytes(0 if o else 255 for o in outside))
    rgba.putalpha(alpha)
    return rgba


def crop_square(image: Image.Image) -> Image.Image:
    left, top, right, bottom = image.getbbox()
    side = max(right - left, bottom - top)

    # 縁いっぱいに描くと、小さいサイズでほかのアイコンより大きく見える。わずかに余白を残す。
    side = int(side * 1.04)
    cx, cy = (left + right) // 2, (top + bottom) // 2

    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(image, (side // 2 - cx, side // 2 - cy))
    return canvas


def main() -> None:
    icon = crop_square(cut_out_background(Image.open(SOURCE)))

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    icon.resize((256, 256), Image.Resampling.LANCZOS).save(
        TARGET, format="ICO", sizes=[(s, s) for s in SIZES])

    print(f"{TARGET.relative_to(ROOT)}  {len(SIZES)} sizes")


if __name__ == "__main__":
    main()
