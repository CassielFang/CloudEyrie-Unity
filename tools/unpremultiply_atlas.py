#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把预乘 alpha 的 Spine 图集**反预乘**成直通 alpha。

## 为什么需要它

本工程是 **Linear 色彩空间**，而青禾的图集是**预乘 alpha**的 —— Spine 明确不支持
这个组合（控制台常驻一条警告）。原因不是配置写错，是数学上就矛盾：

  Linear 空间下贴图会做 sRGB → Linear 转换，而这是个**非线性**操作。
  预乘 alpha 要求「先线性化、再乘 alpha」，但预乘好的数据里 RGB 已经乘过 alpha 了 ——
  再线性化等于把 alpha 也一起非线性化，半透明边缘的颜色就是错的。

所以正确做法是让数据回到**直通 alpha**：反预乘（`RGB / alpha`），
再把材质设成 `Straight Alpha Texture = 1`，由 shader 在**线性空间里**乘 alpha。

## 判据（本脚本自己也用它来决定要不要动手）

预乘图集的两个特征：
1. `alpha == 0` 的像素，RGB 全是 0（预乘时 `RGB × 0 = 0`）
2. 任意像素 `RGB <= alpha`（预乘的不变式）

直通 alpha 的图集**不会**同时满足这两条。实测青禾图集两条都满足
（81.9 万个全透明像素 RGB 全 0；半透明区 58410 个通道没有一个 `RGB > alpha`）。

## 用法

    python tools/unpremultiply_atlas.py                    # 默认处理青禾图集
    python tools/unpremultiply_atlas.py --check            # 只检测，不改文件
    python tools/unpremultiply_atlas.py <别的.png>

## ⚠️ 什么时候要重跑

美术从 Spine 编辑器**重新导出图集**之后 —— 那会把预乘数据又带回来，
症状是控制台那条警告复发、且半透明边缘发暗。和 `Assets/Spine/UNITY6.6-PATCH-NOTES.md`
是同一类「就地修改会被覆盖」的问题，用同样的模式：留 notes + 一行命令重跑。
"""

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image

DEFAULT_ATLAS = "Assets/Art/Spine/Qinghe/青禾1.png"

# 判定「全透明像素是不是黑的」时允许的容差（0~255 的灰度值）
BLACK_TOLERANCE = 2


def analyse(arr: np.ndarray) -> dict:
    """量一量这张图集是不是预乘的，把判据的原始数据都带回来。"""
    alpha = arr[:, :, 3]
    rgb = arr[:, :, :3].astype(np.int32)

    transparent = alpha == 0
    semi = (alpha > 0) & (alpha < 255)

    transparent_rgb_max = int(rgb[transparent].max()) if transparent.any() else 0

    # 半透明区里 RGB 超过 alpha 的通道数 —— 预乘的话必须是 0
    over = 0
    if semi.any():
        over = int((rgb[semi] > alpha[semi][:, None]).sum())

    looks_premultiplied = transparent_rgb_max <= BLACK_TOLERANCE and over == 0

    return {
        "size": (arr.shape[1], arr.shape[0]),
        "transparent_px": int(transparent.sum()),
        "transparent_rgb_max": transparent_rgb_max,
        "semi_px": int(semi.sum()),
        "semi_channels_over_alpha": over,
        "looks_premultiplied": looks_premultiplied,
    }


def unpremultiply(arr: np.ndarray) -> np.ndarray:
    """
    RGB_straight = RGB_premultiplied * 255 / alpha

    预乘的不变式是 `RGB <= alpha`，所以结果天然落在 [0, 255]，不需要担心溢出
    （仍然 clip 一道是为了防御源图不干净）。

    `alpha == 0` 处无法反推原色（乘 0 把信息丢了），保留为 0。
    这些像素完全不可见，不影响正确性；但**双线性过滤会在边缘把邻近的全透明像素
    采样进来**，如果之后发现角色边缘有一圈暗边，那就是需要再补一步「alpha 扩张
    / bleed」（把全透明像素的 RGB 填成最近的不透明颜色）。本脚本暂不做这一步。
    """
    out = arr.astype(np.float32)
    alpha = out[:, :, 3]

    visible = alpha > 0

    scale = np.zeros_like(alpha)
    np.divide(255.0, alpha, out=scale, where=visible)

    out[:, :, :3] = np.clip(out[:, :, :3] * scale[:, :, None], 0.0, 255.0)

    return np.rint(out).astype(np.uint8)


def main() -> int:
    parser = argparse.ArgumentParser(description="Spine 图集反预乘（预乘 alpha → 直通 alpha）")
    parser.add_argument("atlas", nargs="?", default=DEFAULT_ATLAS,
                        help=f"要处理的 PNG（默认 {DEFAULT_ATLAS}）")
    parser.add_argument("--check", action="store_true",
                        help="只检测并报告，不写文件")
    args = parser.parse_args()

    path = Path(args.atlas)

    if not path.exists():
        print(f"[错误] 找不到文件：{path}", file=sys.stderr)
        return 2

    img = Image.open(path)
    original_mode = img.mode
    img = img.convert("RGBA")
    arr = np.array(img)

    info = analyse(arr)

    print(f"图集：{path}")
    print(f"  尺寸 {info['size'][0]}×{info['size'][1]}（原文件模式 {original_mode}）")
    print(f"  全透明像素 {info['transparent_px']} 个，其 RGB 最大值为 {info['transparent_rgb_max']}")
    print(f"  半透明像素 {info['semi_px']} 个，其中 RGB > alpha 的通道数 = "
          f"{info['semi_channels_over_alpha']}")

    if not info["looks_premultiplied"]:
        print("  判定：**不是**预乘 alpha（或已经被反预乘过）→ 无需处理。")
        return 0

    print("  判定：**是**预乘 alpha。")

    if args.check:
        print("  （--check：未写入任何文件）")
        return 0

    result = unpremultiply(arr)

    # 复核：反预乘之后，判据应当不再成立
    after = analyse(result)
    if after["looks_premultiplied"]:
        print("[错误] 反预乘后判据仍然成立，结果不可信，未写入。", file=sys.stderr)
        return 1

    # 不要传 mode= —— Pillow 11 起该参数已废弃（会报 DeprecationWarning），
    # 数组本身是 (H, W, 4) uint8，模式能自动推断出来就是 RGBA
    Image.fromarray(result).save(path, optimize=False)

    print(f"  已写入 {path}")
    print(f"  复核：全透明像素 RGB 最大值 {after['transparent_rgb_max']}、"
          f"半透明区 RGB > alpha 的通道数 {after['semi_channels_over_alpha']}")
    print()
    print("  ⚠️ 还差一步：把 Spine 材质的 `_StraightAlphaInput` 设成 1，")
    print("     否则 shader 仍按预乘处理，会把 alpha 乘两次（边缘发暗）。")
    print("     本工程是 Assets/Art/Spine/Qinghe/ 下的两个 .mat。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
