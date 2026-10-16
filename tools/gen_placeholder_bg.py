#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成占位背景长卷（9600×1080，与美术交付的规格一致）。

**为什么要有它**：美术只交了木/火/土三境的背景长卷，金境与水境没有。
没有背景的话那两境要么是空屏、要么借别的境的图（会让人以为摆错了）。
这里按主题程序生成一张同规格的占位卷，正式美术到位后**同名覆盖**即可。

画的是「三层山脊 + 雾带」—— 结构上模仿美术那三张（远山淡、近山实、底部一条水面），
所以摆进退视差系统之后层次关系是对的，不会因为换了占位就"看起来不像一关"。

## 用法

    python tools/gen_placeholder_bg.py            # 全部缺素材的主题
    python tools/gen_placeholder_bg.py --theme jin
"""

import argparse
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

W, H = 9600, 1080

THEMES = {
    "jin": {   # 金·白锋境：银白剑冢、寒光
        "sky": (228, 234, 240),
        "far": (186, 198, 212),
        "mid": (146, 160, 178),
        "near": (104, 118, 138),
        "water": (198, 210, 222),
        "out": "Assets/Levels/Jin/Resources/Jin/bg",
        "name": "jin_far",
    },
    "shui": {  # 水·玄泽境：黛蓝水泽、水幕
        "sky": (222, 232, 238),
        "far": (168, 194, 208),
        "mid": (126, 158, 178),
        "near": (86, 118, 142),
        "water": (150, 186, 204),
        "out": "Assets/Levels/Shui/Resources/Shui/bg",
        "name": "shui_far",
    },
}


def ridge(draw, base_y, amp, color, seed, step=8):
    """画一条起伏的山脊。用几个正弦叠加，避免规整得像折线。"""
    rnd = random.Random(seed)
    p1, p2, p3 = rnd.uniform(0.6, 1.4), rnd.uniform(1.8, 3.2), rnd.uniform(4.0, 7.0)
    ph = [rnd.uniform(0, math.tau) for _ in range(3)]

    pts = []
    for x in range(0, W + step, step):
        t = x / W * math.tau
        y = base_y - amp * (0.55 * math.sin(t * p1 + ph[0])
                            + 0.30 * math.sin(t * p2 + ph[1])
                            + 0.15 * math.sin(t * p3 + ph[2]))
        pts.append((x, y))

    pts += [(W, H), (0, H)]
    draw.polygon(pts, fill=color)


def build(cfg):
    img = Image.new("RGB", (W, H), cfg["sky"])
    d = ImageDraw.Draw(img)

    # 三层山脊：越近越低、越实
    ridge(d, 700, 150, cfg["far"], seed=11)
    ridge(d, 820, 130, cfg["mid"], seed=22)
    ridge(d, 930, 110, cfg["near"], seed=33)

    # 底部一条水面（美术那三张的底边都是一条亮带）
    d.rectangle([0, H - 120, W, H], fill=cfg["water"])

    # 雾带：横着拖一道模糊的白，把远近层次揉开
    fog = Image.new("L", (W, H), 0)
    fd = ImageDraw.Draw(fog)
    for i in range(6):
        y = int(H * (0.35 + i * 0.09))
        fd.rectangle([0, y, W, y + 60], fill=70)
    fog = fog.filter(ImageFilter.GaussianBlur(40))
    img = Image.composite(Image.new("RGB", (W, H), (255, 255, 255)), img, fog)

    return img


def main() -> int:
    ap = argparse.ArgumentParser(description="生成占位背景长卷")
    ap.add_argument("--theme", default=None, help=f"只出某一境（{', '.join(THEMES)}），默认全部")
    args = ap.parse_args()

    names = [args.theme] if args.theme else list(THEMES)
    unknown = [n for n in names if n not in THEMES]

    if unknown:
        print(f"[错误] 不认识的主题：{unknown}", file=sys.stderr)
        return 2

    for theme in names:
        cfg = THEMES[theme]
        out = Path(cfg["out"])

        if "Resources" not in out.parts and "Art" not in out.parts:
            print(f"[错误] 输出目录必须在 Resources/ 或 Art/ 之下：{out}", file=sys.stderr)
            return 2

        out.mkdir(parents=True, exist_ok=True)
        path = out / f"{cfg['name']}.png"
        build(cfg).save(path)
        print(f"  {path}  {W}×{H}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
