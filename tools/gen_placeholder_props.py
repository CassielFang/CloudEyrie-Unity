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

# 摆荡藤台：木色 + 青苔，与瓦片地形的青绿同族
WOOD = (168, 132, 92)
WOOD_DARK = (118, 90, 60)
WOOD_LIGHT = (198, 166, 124)
WOOD_LINE = (146, 112, 78)
MOSS = (122, 164, 112)

# 近战山魈：深色小兽，与青绿的场景拉开对比（敌人要一眼能认出来）
BEAST = (74, 62, 82)
BEAST_DARK = (48, 40, 58)

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


PLANK_W, PLANK_H = 192, 96


def make_platform_board():
    """
    摆荡藤台：一块木板。**不画吊绳** —— 平台是整体平移的，画上去的绳子会跟着一起
    飘走，反而穿帮。绳子留给正式美术用单独一层处理（或者干脆不画，靠上下文的藤蔓暗示）。
    """
    img = Image.new("RGBA", (PLANK_W, PLANK_H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # 板子只占下面一条，上面留白给"吊挂"的空间感
    top = PLANK_H - 34

    d.rectangle([4, top, PLANK_W - 5, PLANK_H - 3], fill=WOOD, outline=WOOD_DARK, width=2)
    d.line([4, top + 1, PLANK_W - 5, top + 1], fill=WOOD_LIGHT)      # 受光的上沿

    # 木纹：几条横向的浅线
    for y in range(top + 8, PLANK_H - 6, 7):
        d.line([10, y, PLANK_W - 11, y], fill=WOOD_LINE)

    # 两端各一撮青苔（木境的绿意，也让"哪面能站"更好读）
    for x0 in (4, PLANK_W - 26):
        d.ellipse([x0, top - 7, x0 + 22, top + 7], fill=MOSS)

    return img


MINION_W, MINION_H = 128, 130


def make_minion_melee():
    """
    近战山魈的占位立绘：深色小兽 + 两只角 + 一只眼。

    刻意**不用**弹跳花那张图当占位 —— 摆出来的近战怪长得像花，看截图会以为是摆错了。
    正式美术到位后同名覆盖即可。

    ⚠️ 朝向：朝右（与青禾一致）。`EnemyAI` 的 `artNaturalFacing` 要跟着对，
    它靠翻转节点做左右，判错会让小怪背对着人走。
    """
    img = Image.new("RGBA", (MINION_W, MINION_H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    cx = MINION_W // 2
    body_top, body_bot = 34, MINION_H - 6

    # 身体：一个立起来的椭圆
    d.ellipse([cx - 44, body_top, cx + 44, body_bot], fill=BEAST, outline=BEAST_DARK)

    # 两只角
    for sx in (-1, 1):
        d.polygon([(cx + sx * 26, body_top + 6),
                   (cx + sx * 44, 6),
                   (cx + sx * 14, body_top + 16)], fill=BEAST_DARK)

    # 前伸的一条腿（朝右，暗示朝向）
    d.polygon([(cx + 18, body_bot - 26), (cx + 56, body_bot - 6),
               (cx + 54, body_bot + 2), (cx + 12, body_bot - 6)], fill=BEAST_DARK)

    # 眼（也偏右，进一步读朝向）
    d.ellipse([cx + 2, body_top + 22, cx + 26, body_top + 46], fill=(250, 240, 220))
    d.ellipse([cx + 12, body_top + 30, cx + 22, body_top + 42], fill=(40, 34, 46))

    return img


PROPS = {
    "bounce_flower": make_bounce_flower,
    "platform_board": make_platform_board,
    "minion_melee": make_minion_melee,
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
