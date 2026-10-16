#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成占位地形瓦片（Tilemap 用）。

**为什么要有它**：关卡要从「代码搭的平地面」改成 Tilemap 可视化刷地形，
而地形贴图美术还没交。没有瓦片就没法刷，也就没法验证平台管线 ——
所以先程序生成一套，把玩法跑通；正式美术到位后**同名覆盖**即可，不用改代码。

生成两类：
  tile_ground  地面主体（上沿一道亮边，方便看清哪面是"站上去的那面"）
  tile_platform 薄平台（半高，用于悬空平台）

瓦片尺寸 = `SIZE`，与 Unity 里 Grid 的 cellSize 一致（本项目 PPU = 1，
所以 64px 的图就是 64×64 世界单位）。

五境差异**主要靠调色**（设计稿原话），所以这里按主题参数化 ——
加一境只要在 `THEMES` 里加一组三个颜色。

## 用法

    python tools/gen_placeholder_tiles.py                 # 全部主题
    python tools/gen_placeholder_tiles.py --theme huo     # 只出某一境

⚠️ 输出目录必须在 `Resources/` 或 `Assets/Art/` 之下，否则
`CloudEyrieTextureImporter` 不生效 → 按 Unity 默认 PPU=100 导入 →
瓦片在世界里只有 0.64 单位大，肉眼几乎看不见。脚本会挡住这种情况。
"""

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 64

THEMES = {
    "mu": {   # 木·青芽境：青绿山谷
        "top": (128, 168, 118),
        "body": (86, 112, 84),
        "edge": (62, 82, 62),
        "out": "Assets/Levels/Mu/Resources/Mu/terrain",
    },
    "huo": {  # 火·赤焰境：朱红岩窟
        "top": (176, 118, 96),
        "body": (124, 78, 68),
        "edge": (86, 52, 48),
        "out": "Assets/Levels/Huo/Resources/Huo/terrain",
    },
    "tu": {   # 土·玄岩境：赭黄砂岩
        "top": (206, 178, 122),
        "body": (156, 130, 92),
        "edge": (112, 92, 64),
        "out": "Assets/Levels/Tu/Resources/Tu/terrain",
    },
    "jin": {  # 金·白锋境：石青剑冢
        "top": (210, 218, 228),
        "body": (150, 160, 176),
        "edge": (104, 114, 132),
        "out": "Assets/Levels/Jin/Resources/Jin/terrain",
    },
    "shui": {  # 水·玄泽境：黛蓝泽地
        "top": (152, 190, 206),
        "body": (100, 138, 158),
        "edge": (66, 96, 114),
        "out": "Assets/Levels/Shui/Resources/Shui/terrain",
    },
    "altar": {  # 中央祭坛：暖金石材（五行光环的台基）
        "top": (216, 196, 148),
        "body": (162, 146, 116),
        "edge": (118, 104, 82),
        "out": "Assets/Levels/Altar/Resources/Altar/terrain",
    },
}


def make_tile(height_ratio, top_ratio, top, body, edge):
    """height_ratio：瓦片里"实心"占多高（1.0 = 满格，0.5 = 半格薄平台）"""
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    solid_h = int(SIZE * height_ratio)
    y0 = SIZE - solid_h          # 从底部往上填：瓦片的"上表面"在 y 小的一侧

    d.rectangle([0, y0, SIZE - 1, SIZE - 1], fill=body)

    # 上沿亮边（看清站上去的那面）
    band = max(2, int(SIZE * top_ratio))
    d.rectangle([0, y0, SIZE - 1, min(SIZE - 1, y0 + band - 1)], fill=top)

    d.rectangle([0, y0, SIZE - 1, SIZE - 1], outline=edge)
    d.line([0, y0, SIZE - 1, y0], fill=edge)

    # 浅浅的错位纹理：让相邻瓦片能看出分界，但不至于花
    shade = (max(0, body[0] - 10), max(0, body[1] - 10), max(0, body[2] - 10), 255)
    for y in range(y0 + band + 2, SIZE - 1, 8):
        d.line([2, y, SIZE - 3, y], fill=shade)

    return img


def main() -> int:
    ap = argparse.ArgumentParser(description="生成占位地形瓦片")
    ap.add_argument("--theme", default=None, help=f"只出某一境（{', '.join(THEMES)}），默认全部")
    args = ap.parse_args()

    names = [args.theme] if args.theme else list(THEMES)
    unknown = [n for n in names if n not in THEMES]

    if unknown:
        print(f"[错误] 不认识的主题：{unknown}", file=sys.stderr)
        return 2

    shapes = {"tile_ground": (1.0, 0.22), "tile_platform": (0.5, 0.34)}

    for theme in names:
        cfg = THEMES[theme]
        out = Path(cfg["out"])

        if "Resources" not in out.parts and "Art" not in out.parts:
            print(f"[错误] 输出目录必须在 Resources/ 或 Art/ 之下：{out}", file=sys.stderr)
            return 2

        out.mkdir(parents=True, exist_ok=True)
        print(f"== {theme}  ->  {out}")

        for name, (h, t) in shapes.items():
            path = out / f"{name}.png"
            make_tile(h, t, cfg["top"], cfg["body"], cfg["edge"]).save(path)
            print(f"   {path.name}  {SIZE}×{SIZE}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
