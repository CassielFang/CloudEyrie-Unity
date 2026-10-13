#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成机关占位贴图（木境先做弹跳花）。

与 `gen_placeholder_tiles.py` 同一个理由：美术没交，但机关要能先跑起来验证玩法。
正式美术**同名覆盖**即可，不用改代码。

## 用法

    python tools/gen_placeholder_props.py                 # 写进 Assets/Levels/Mu/Resources/Mu/props/
    python tools/gen_placeholder_props.py --out DIR

⚠️ 输出目录必须在 `Resources/` 或 `Assets/Art/` 之下（否则导入器不生效，PPU 变成 100）。
"""

import argparse
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw

# 设计稿「木境特色机关」给的弹跳花配色
PETAL = (232, 168, 200)     # #e8a8c8
PETAL_DARK = (198, 132, 166)
CORE = (255, 236, 210)
STEM = (96, 140, 96)

W, H = 128, 160
DEFAULT_OUT = "Assets/Levels/Mu/Resources/Mu/props"


def make_bounce_flower():
    """
    弹跳花：一根茎 + 花冠。

    ⚠️ **花冠必须画在上半部分**、茎在下半部分 —— 因为 `BounceFlower` 靠
    **碰撞体**判定踩踏，而美术的锚点在节点中心。将来换成正式贴图时，
    「花冠中心」与「节点原点」的相对位置一变，踩踏判定就会跟着偏，
    所以换图时要一起核对碰撞体的 offset。
    """
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx = W // 2
    head_cy = 48          # 花冠中心
    head_r = 42

    # 茎：从花冠底部到画布底部，略微弯曲
    for i in range(head_cy, H - 4):
        t = (i - head_cy) / max(1, H - 4 - head_cy)
        x = cx + math.sin(t * 2.2) * 6
        d.line([x - 3, i, x + 3, i], fill=STEM)

    # 花瓣：一圈椭圆
    petals = 8
    for i in range(petals):
        a = 2 * math.pi * i / petals
        px = cx + math.cos(a) * head_r * 0.62
        py = head_cy + math.sin(a) * head_r * 0.62
        rx = head_r * 0.46
        d.ellipse([px - rx, py - rx, px + rx, py + rx], fill=PETAL, outline=PETAL_DARK)

    # 花心
    d.ellipse([cx - 20, head_cy - 20, cx + 20, head_cy + 20], fill=CORE, outline=PETAL_DARK)
    d.ellipse([cx - 8, head_cy - 8, cx + 8, head_cy + 8], fill=PETAL_DARK)

    return img


PROPS = {
    "bounce_flower": make_bounce_flower,
}


def main() -> int:
    ap = argparse.ArgumentParser(description="生成机关占位贴图")
    ap.add_argument("--out", default=DEFAULT_OUT, help=f"输出目录（默认 {DEFAULT_OUT}）")
    args = ap.parse_args()

    out = Path(args.out)

    if "Resources" not in out.parts and "Art" not in out.parts:
        print(f"[错误] 输出目录必须在 Resources/ 或 Art/ 之下：{out}", file=sys.stderr)
        return 2

    out.mkdir(parents=True, exist_ok=True)

    for name, fn in PROPS.items():
        path = out / f"{name}.png"
        img = fn()
        img.save(path)
        print(f"  {path}  {img.size[0]}×{img.size[1]}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
