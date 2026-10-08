#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""生成战斗特效用的占位贴图（黑底 + alpha 遮罩，运行时由 SpriteRenderer.color 染色）。

## 为什么要补这几张

原来 `Resources/fx/` 只有六张「墨」：`ink_bleed / dot / speck / streak×3`。
三种形态的攻击**只有颜色不同、形状完全一样** —— 玩家没法从画面上分辨
「这一刀是实体砍的、那一下是灵合放的光环」。用户反馈正是「看不出来」。

所以补三张**非墨**的形状，让三种形态各有一套形状语言：

    实体：飞白（细长枯笔）—— 锐、快、有方向
    灵雾：柔晕 + 雾团       —— 圆、慢、往上飘
    灵合：光环 + 光点       —— 放射、亮、不接触

`glow_ring` 是「光环/护盾/净化」的通用件（拉长生命 + sizeEndRatio > 1 就是扩散波），
`glow_soft` 是柔光（当底衬、当闪光），`mote` 是光点（灵合的上升光点、完美格挡的火花）。

## 约定（跟原来六张保持一致）

- **RGB 全 0、只有 alpha** —— 颜色一律运行时染
- 像素单位 1（`CloudEyrieTextureImporter` 对 `Assets/Resources/` 下的图统一钉死）
- 四周留透明边，尺寸取 2 的幂附近

## 用法

    python tools/gen_fx_sprites.py
    python tools/gen_fx_sprites.py --out Assets/Resources/fx

⚠️ 这些是**程序化占位图**，和原来那六张一样 —— 美术到位后直接替换同名文件，代码不用动。
"""

import argparse
import math
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OUT = os.path.join(ROOT, "Assets", "Resources", "fx")


def _grid(size):
    """返回 (x, y, r)：以图片中心为原点的坐标与半径（r 归一化到半宽 = 1）。"""
    half = (size - 1) / 2.0
    y, x = np.mgrid[0:size, 0:size].astype(np.float64)
    x = (x - half) / half
    y = (y - half) / half
    return x, y, np.sqrt(x * x + y * y)


def glow_ring(size=256, radius=0.66, thickness=0.095):
    """光环：一圈高斯截面的环。

    `thickness` 是环的「软」程度 —— 越小越像硬边圆环，越大越像一圈光晕。
    """
    _, _, r = _grid(size)
    d = (r - radius) / thickness
    alpha = np.exp(-0.5 * d * d)

    # 内圈稍微压一点，让环看起来是「中空的」而不是一块圆盘
    alpha *= np.clip((r - radius + thickness * 2.2) / (thickness * 2.2), 0.0, 1.0)
    return alpha


def glow_ring_half(size=256, radius=0.66, thickness=0.095, upper=True, feather=0.075):
    """半环：把整环按水平中线切开，只留上半或下半。

    ## 为什么要半环

    环绕类特效（净化光环、格挡圈、换形态炸开）**必须分前后两层**：
    在人身上的那一半要被人挡住（远侧），朝观众的那一半要盖住人（近侧）。
    整环只有一层，无论放哪边都会「要么整个挡在人前、要么整个人被环挡住」，
    看着就是一张贴纸而不是套在身上的圈。

    分片而不是靠遮罩/材质：本项目所有特效都是普通 `SpriteRenderer`
    （见 InkFx 的说明），**加不了遮罩**。最省事也最稳的做法就是两张图、
    两个 sortingOrder。

    `feather` 是中线附近的**羽化**宽度 —— 硬切的话，两半在不同层上会在交界处
    露出一条缝；羽化让它们在中间叠一小段，叠起来看不出接缝。
    """
    a = glow_ring(size, radius, thickness)

    # _grid 的 y 是「向下为正」，先翻成向上为正
    _, y, _ = _grid(size)
    v = -y

    t = np.clip((v if upper else -v) / feather, 0.0, 1.0)
    return a * (t * t * (3.0 - 2.0 * t))       # smoothstep


def arc_slash(size=256, radius=0.70, thickness=0.085,
              spread_deg=62, lead_frac=0.30):
    """刀锋弧：一道有粗细变化的弧光，会随生命被拉长/扫开。

    ## 为什么需要它

    原来挥砍的视觉全是**散落的点状飞白** —— 一堆长短不一的笔画，看得出「有东西扫过」，
    但看不出**刀锋的走向**。真正的挥砍要让眼睛跟着一道弧走，它是整下攻击的「主笔」，
    飞白只是它的碎屑。

    `spread_deg` 是弧张开的半角；`lead_frac` 控制**粗细不对称** ——
    起手那端厚、收势那端薄（真刀挥出去就是这个形状：前段快而宽、后段甩细）。
    完全对称的弧看着像「一圈橡皮筋」，没有速度感。
    """
    x, y, r = _grid(size)

    # 屏幕 y 向下，翻成「向上为正」再取角度：0 = 正右，逆时针为正
    ang = np.arctan2(-y, x)
    half = np.deg2rad(spread_deg)

    # 角窗：主体留在 ±half 内，外侧再各留 1.6 倍做收敛
    a = np.exp(-0.5 * (ang / half) ** 2)

    # 粗细不对称：负角那侧（起手）保持厚度，正角那侧（收势）收细
    t = np.clip(ang / (half * 1.6), -1.0, 1.0)
    a *= np.clip(1.0 - lead_frac * np.clip(t, 0.0, 1.0), 0.0, 1.0)

    d = (r - radius) / thickness
    return a * np.exp(-0.5 * d * d)


def glow_soft(size=192, power=2.4, max_alpha=0.92):
    """柔光：中心最亮、向外幂次衰减的圆。当底衬或闪光用。"""
    _, _, r = _grid(size)
    alpha = np.clip(1.0 - r, 0.0, 1.0) ** power
    return alpha * max_alpha


def mote(size=64, core=0.16, ray_len=0.96, ray_width=0.075):
    """光点：一个小核心 + 四道细射线（十字星）。

    核心给「亮点」，射线给「星芒」—— 单靠一个圆点在高亮背景上会被吞掉，
    有射线才看得出这是「光」而不是「一张小圆图」。
    """
    x, y, r = _grid(size)
    alpha = np.exp(-0.5 * (r / core) ** 2)

    for ax, ay in ((1, 0), (0, 1)):
        # 到该轴的距离（横向射线看 |y|，纵向看 |x|）
        across = np.abs(y) if ax else np.abs(x)
        along = np.abs(x) if ax else np.abs(y)
        line = np.exp(-0.5 * (across / ray_width) ** 2) * np.clip(1.0 - along / ray_len, 0.0, 1.0) ** 2
        alpha = np.maximum(alpha, line * 0.85)

    return np.clip(alpha, 0.0, 1.0)


def save(name, alpha, out_dir):
    """按「黑底 + alpha」写盘 —— RGB 全 0，颜色留给运行时。"""
    a = np.clip(alpha * 255.0, 0, 255).astype(np.uint8)
    h, w = a.shape
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[:, :, 3] = a

    path = os.path.join(out_dir, name + ".png")
    Image.fromarray(rgba, "RGBA").save(path)

    ys, xs = np.where(a > 8)
    print("  %-18s %3dx%-3d  内容 %3dx%-3d  峰值alpha=%3d  → %s"
          % (name, w, h, xs.max() - xs.min() + 1, ys.max() - ys.min() + 1,
             int(a.max()), os.path.relpath(path, ROOT)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    print("生成特效占位贴图（黑底 + alpha，运行时染色）：")

    save("glow_ring", glow_ring(), args.out)

    # 半环：远侧（上半圈）画在人物之后，近侧（下半圈）画在人物之前
    save("glow_ring_far", glow_ring_half(upper=True), args.out)
    save("glow_ring_near", glow_ring_half(upper=False), args.out)

    save("glow_soft", glow_soft(), args.out)
    save("mote", mote(), args.out)

    # 挥砍的主笔：刀锋弧（贴图朝右张开，用时按朝向旋转/镜像）
    save("arc_slash", arc_slash(), args.out)

    print("\n完成。改样式就调本文件里的参数，重跑即可。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
