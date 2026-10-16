#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成地形瓦片（Tilemap 用）。

从「纯色块」升级成「**用真材质**」：地面/平台的底色取自 `Assets/Resources/fx_tex/`
里那批筛选过的材质贴图（青苔石砖、金焰纹岩、赭黄岩、冷灰云石、蓝冰晶、金裂石），
按各境的主题色染色后叠上"上表面亮边"。

**上表面亮边是功能，不是装饰**：本作是平台跳跃，
玩家必须一眼看出「哪一面能站」。材质再好看，如果四面一样，
跳过去就是"撞墙"还是"站上去"完全分不清。

## 为什么材质是"叠上去"而不是直接铺满

那批材质多是 128–256px 的**细节纹理**，缩到 64px 一格会糊成噪点。
所以只取它 **45% 的权重**压在主题色上 —— 远看是那个"材质感"，
近看又不会花。真实美术到位后**同名覆盖**这套瓦片即可，不用改代码。

## 用法

    python tools/gen_placeholder_tiles.py            # 全部主题
    python tools/gen_placeholder_tiles.py --theme huo

⚠️ 输出目录必须在 `Resources/` 或 `Assets/Art/` 之下，否则
`CloudEyrieTextureImporter` 不生效 → 按 Unity 默认 PPU=100 导入 → 瓦片几乎看不见。
"""

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 64

# 材质贴图目录（已入库、已筛过的那批）
TEX = Path("Assets/Resources/fx_tex")

THEMES = {
    "mu": {
        "top": (128, 168, 118), "body": (86, 112, 84), "edge": (58, 76, 58),
        "tex": "terrain_mu.png",
        "out": "Assets/Levels/Mu/Resources/Mu/terrain",
    },
    "huo": {
        "top": (176, 118, 96), "body": (124, 78, 68), "edge": (82, 48, 44),
        "tex": "terrain_huo.png",
        "out": "Assets/Levels/Huo/Resources/Huo/terrain",
    },
    "tu": {
        "top": (206, 178, 122), "body": (156, 130, 92), "edge": (108, 88, 60),
        "tex": "terrain_tu.png",
        "out": "Assets/Levels/Tu/Resources/Tu/terrain",
    },
    "jin": {
        "top": (210, 218, 228), "body": (150, 160, 176), "edge": (100, 110, 128),
        "tex": "terrain_jin.png",
        "out": "Assets/Levels/Jin/Resources/Jin/terrain",
    },
    "shui": {
        "top": (152, 190, 206), "body": (100, 138, 158), "edge": (62, 92, 110),
        "tex": "terrain_shui.png",
        "out": "Assets/Levels/Shui/Resources/Shui/terrain",
    },
    "altar": {
        "top": (216, 196, 148), "body": (162, 146, 116), "edge": (114, 100, 78),
        "tex": "terrain_altar.png",
        "out": "Assets/Levels/Altar/Resources/Altar/terrain",
    },
}

# 材质在瓦片里的权重。太高会花、太低等于没有
TEX_WEIGHT = 0.60


def load_texture(name):
    """
    读材质，取一块**原尺寸**的 tile 尺寸区域。

    ⚠️ <b>是裁剪，不是缩放。</b> 这一点踩过：一开始把 256px 的材质
    `resize` 到 64px，等于对整张图做了一次平均 —— 细节被抹平，
    瓦片看起来和纯色块几乎没区别（实测标准差只有 12.8，而原图是 40+）。
    改成从图心裁一块 64×64，纹理就保持它本来的尺度与对比。

    材质本身是"四方连续"（可平铺）的，所以裁哪一块都能无缝拼。
    """
    p = TEX / name
    if not p.exists():
        return None

    im = Image.open(p).convert("RGB")
    w, h = im.size

    # 源图比瓦片大 → 从图心裁一块原尺寸
    if w >= SIZE and h >= SIZE:
        left = (w - SIZE) // 2
        top = (h - SIZE) // 2
        return im.crop((left, top, left + SIZE, top + SIZE))

    # 源图比瓦片小 → 只能放大（用 NEAREST，至少不糊）
    return im.resize((SIZE, SIZE), Image.NEAREST)


def make_tile(height_ratio, top_ratio, cfg):
    """height_ratio：实心占多高（1.0 = 满格，0.5 = 半格薄平台）"""
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    solid_h = int(SIZE * height_ratio)
    y0 = SIZE - solid_h          # 从底部往上填：瓦片的"上表面"在 y 小的一侧

    body = cfg["body"]
    top = cfg["top"]
    edge = cfg["edge"]

    # 1) 底色
    d.rectangle([0, y0, SIZE - 1, SIZE - 1], fill=body)

    # 2) 叠材质（只叠在实心区）
    tex = load_texture(cfg["tex"])
    if tex is not None:
        px_t = tex.load()
        px = img.load()
        w = TEX_WEIGHT

        for y in range(y0, SIZE):
            for x in range(SIZE):
                r, g, b = px_t[x, y]

                # 材质的**明暗结构**保留、颜色丢掉 —— 它是照片级的，
                # 直接混色会把主题色冲淡；只取亮度做 ±40% 的调制。
                lum = (r * 299 + g * 587 + b * 114) // 1000
                k = 1.0 + (lum / 255.0 - 0.5) * 0.8

                tr = min(255, body[0] * k)
                tg = min(255, body[1] * k)
                tb = min(255, body[2] * k)

                px[x, y] = (int(body[0] * (1 - w) + tr * w),
                            int(body[1] * (1 - w) + tg * w),
                            int(body[2] * (1 - w) + tb * w),
                            255)

    # 3) 上表面亮边 —— **功能性的**，玩家靠它看出哪面能站
    band = max(3, int(SIZE * top_ratio))
    ImageDraw.Draw(img).rectangle([0, y0, SIZE - 1, min(SIZE - 1, y0 + band - 1)], fill=top)

    # 4) 上沿加深一道，把"面"和"体"分开
    d = ImageDraw.Draw(img)
    d.line([0, y0, SIZE - 1, y0], fill=edge)
    d.rectangle([0, y0, SIZE - 1, SIZE - 1], outline=edge)

    return img


def main() -> int:
    ap = argparse.ArgumentParser(description="生成地形瓦片")
    ap.add_argument("--theme", default=None, help=f"只出某一境（{', '.join(THEMES)}），默认全部")
    args = ap.parse_args()

    names = [args.theme] if args.theme else list(THEMES)
    unknown = [n for n in names if n not in THEMES]

    if unknown:
        print(f"[错误] 不认识的主题：{unknown}", file=sys.stderr)
        return 2

    shapes = {"tile_ground": (1.0, 0.24), "tile_platform": (0.5, 0.36)}

    for theme in names:
        cfg = THEMES[theme]
        out = Path(cfg["out"])

        if "Resources" not in out.parts and "Art" not in out.parts:
            print(f"[错误] 输出目录必须在 Resources/ 或 Art/ 之下：{out}", file=sys.stderr)
            return 2

        out.mkdir(parents=True, exist_ok=True)
        has_tex = (TEX / cfg["tex"]).exists()
        print(f"== {theme}  ->  {out}   材质={cfg['tex'] if has_tex else '（缺，退回纯色）'}")

        for name, (h, t) in shapes.items():
            path = out / f"{name}.png"
            make_tile(h, t, cfg).save(path)

    return 0


if __name__ == "__main__":
    sys.exit(main())
