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

## 用法

    python tools/gen_placeholder_tiles.py            # 写进 Assets/Levels/Mu/Resources/Mu/terrain/
    python tools/gen_placeholder_tiles.py --out DIR  # 换输出目录

⚠️ 输出目录必须在某个 `Resources/` 之下（或 `Assets/Art/` 下），
否则 `CloudEyrieTextureImporter` 不生效 → 按 Unity 默认 PPU=100 导入 →
瓦片在世界里只有 0.64 单位大，肉眼几乎看不见。
"""

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 64

# 木境基调（青绿）。换境时改这三个色即可 —— 设计稿说五境差异主要靠调色
TOP = (128, 168, 118)      # 上沿亮边
BODY = (86, 112, 84)       # 主体
EDGE = (62, 82, 62)        # 描边

DEFAULT_OUT = "Assets/Levels/Mu/Resources/Mu/terrain"


def make_tile(name, height_ratio, top_ratio):
    """height_ratio：瓦片里"实心"占多高（1.0 = 满格，0.5 = 半格薄平台）"""
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    solid_h = int(SIZE * height_ratio)
    top = SIZE - solid_h          # 从底部往上填：瓦片的"上表面"在 y 小的一侧

    # 主体
    d.rectangle([0, top, SIZE - 1, SIZE - 1], fill=BODY)

    # 上沿亮边（看清站上去的那面）
    band = max(2, int(SIZE * top_ratio))
    d.rectangle([0, top, SIZE - 1, min(SIZE - 1, top + band - 1)], fill=TOP)

    # 描边：外框 + 上沿加重
    d.rectangle([0, top, SIZE - 1, SIZE - 1], outline=EDGE)
    d.line([0, top, SIZE - 1, top], fill=EDGE)

    # 浅浅的错位纹理，让相邻瓦片能看出分界，但不至于花
    for y in range(top + band + 2, SIZE - 1, 8):
        d.line([2, y, SIZE - 3, y], fill=(BODY[0] - 8, BODY[1] - 8, BODY[2] - 8, 255))

    return img


def main() -> int:
    ap = argparse.ArgumentParser(description="生成占位地形瓦片")
    ap.add_argument("--out", default=DEFAULT_OUT, help=f"输出目录（默认 {DEFAULT_OUT}）")
    args = ap.parse_args()

    out = Path(args.out)

    if "Resources" not in out.parts and "Art" not in out.parts:
        print(f"[错误] 输出目录必须在 Resources/ 或 Art/ 之下，否则导入器不生效：{out}",
              file=sys.stderr)
        return 2

    out.mkdir(parents=True, exist_ok=True)

    tiles = {
        "tile_ground": (1.0, 0.22),      # 满格地面
        "tile_platform": (0.5, 0.34),    # 半格薄平台
    }

    for name, (h, t) in tiles.items():
        path = out / f"{name}.png"
        make_tile(name, h, t).save(path)
        print(f"  {path}  {SIZE}×{SIZE}")

    print()
    print("生成完毕。在 Unity 里：")
    print("  1) Grid 的 cellSize 设成 (%d, %d)" % (SIZE, SIZE))
    print("  2) 由这些图各建一个 Tile 资产，再用 Tile Palette 刷")
    return 0


if __name__ == "__main__":
    sys.exit(main())
