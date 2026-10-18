#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把交付层的特效图**筛选 + 处理**成引擎层的 `Assets/Resources/fx_tex/`。

## 为什么要有这份表

`美术素材/特效贴图/`（14 类、约 4700 个、200MB）是一套**西幻风格的通用特效包**。
2026-10-17 第一轮筛选时，是把挑中的文件用 `cp` 手工拷进 `fx_tex/` 的 ——
**出处只留在那次提交的说明里，脚本里没有**。于是后来没人说得清
「`fx_flame.png` 到底是哪张图」，也没法重新生成。

这份表把两件事一起钉死：**从哪来**（交付层路径）与**怎么处理**（原样拷贝 / 黑底抠图）。

## 一个反复踩到的坑：把「材质」当「精灵」用

交付层里那批中文命名的（`四方连续火_td` / `yanjiang` / `c_pubu01` / `el_crack05` …）
是**无缝平铺材质**，不是精灵。第一轮把 `fx_crack`（透明白底裂纹）当成落石预警圈、
把 `fx_flame` 当成火柱精灵，都不成立：

- `fx_crack` 的 alpha **全程 255**（完全不透明）—— 当精灵用就是一块白板
- `fx_flame` 大部分半透明，压在**亮背景**上会露出自己的浅色底，读成矩形面板

判据是 **alpha 通道**：全不透 = 材质（只能作地表/液面），有真透明 = 精灵。
`--check` 会把每一张的透明度打出来，选素材前先跑它。

## 黑底抠图（`key_black`）

发光类的贴图是「黑底上画光」——黑的地方其实表示**不发光**，直接当精灵用就是一块黑方块。
所以按辉度把黑提取成 alpha：

    alpha  = max(R, G, B)          # 越亮越不透明
    RGB   /= alpha / 255           # 反预乘，把被黑色压暗的颜色还原回去

这样黑底变全透、光晕保持原本的色相，叠在任何背景上都干净。
（和 `tools/unpremultiply_atlas.py` 是同一套数学，只是方向相反。）

## 用法

    python tools/import_fx_tex.py            # 全部重新生成
    python tools/import_fx_tex.py --check    # 只报告，不写文件
    python tools/import_fx_tex.py --only flame

⚠️ 输出目录必须在 `Resources/` 之下，否则 `CloudEyrieTextureImporter` 不生效
→ 按 Unity 默认 PPU=100 导入 → `Resources.Load<Sprite>` 静默返回 null。
"""

import argparse
import shutil
import sys
from pathlib import Path

import numpy as np
from PIL import Image

# Windows 的控制台默认是 GBK，打不出「⚠」这类符号会直接抛 UnicodeEncodeError。
# 报告里有几处警示符号，所以这里把 stdout 钉成 UTF-8。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SRC_ROOT = Path("美术素材/特效贴图")
OUT_DIR = Path("Assets/Resources/fx_tex")


class Entry:
    """一条入库规则。"""

    def __init__(self, out, src, mode, note, group="fx"):
        self.out = out            # 引擎层文件名
        self.src = src            # 交付层相对路径（相对 SRC_ROOT）
        self.mode = mode          # "copy" | "key_black"
        self.note = note
        self.group = group        # fx / terrain

    @property
    def key(self):
        """`--only` 用的短名，例如 fx_flame.png -> flame。"""
        return Path(self.out).stem.replace("fx_", "").replace("terrain_", "")


# ============================================================
# 入库表 —— 交付层「从哪来」+「怎么处理」，改素材只改这里
# ============================================================
ENTRIES = [
    # ---- 机关精灵 ----
    Entry("fx_flame.png", "扭曲烟雾类/火焰弹.png", "key_black",
          "火柱。黑底发光图 —— 抠掉黑底才叠得上（原用『四方连续火_td』是材质，压在亮天空上会是块矩形面板）"),
    Entry("fx_lava.png", "无缝贴图类/FIRELANDS_LAVA.png", "copy",
          "岩浆液面。**不透明是对的** —— 岩浆本来就该是实心液面，不需要 alpha"),
    Entry("fx_water.png", "无缝贴图类/水纹02.png", "copy",
          "水流 / 水面。挑它的理由：**在『无缝贴图类』里（平铺不出缝）且 alpha 有 58% 透明**。"
          "更漂亮的『ctx_shuimian1』（带焦散的水面）实测**全不透**，是材质不是精灵，用上去就是块方板"),
    Entry("fx_crack.png", "无缝贴图类/el_crack05.png", "copy",
          "龟裂材质。⚠️ **不是精灵**（alpha 全程 255），当预警圈会是一块白板；只作地形/材质用途"),
    Entry("fx_spring.png", "无缝贴图类/c_quanshui.png", "copy",
          "灵泉水面。已被『场景小物件/灵泉石台（回血）』取代，保留备用"),

    # ---- 青禾的角色特效（2026-10-17 第二轮筛选）----
    Entry("fx_dust.png", "扭曲烟雾类/a001.png", "copy",
          "起跳/落地尘雾：柔灰烟团，alpha 边缘自然"),
    Entry("fx_cloud_ring.png", "扩散旋转类/Flare05.png", "copy",
          "二段跳『云跃』的云环：青绿柔光，和青禾的灵炁同色系"),
    Entry("fx_ring_white.png", "扩散旋转类/e68de1d0.png", "copy",
          "落地冲击环 / 形态切换的白光环"),
    Entry("fx_wave.png", "扭曲烟雾类/circlewave02.png", "copy",
          "落地冲击波 / 水纹"),
    Entry("fx_sparkle.png", "光点类/k_guangmang2.png", "copy",
          "小星芒。白色可染色，用于二段跳/灵泉/形态切换"),
    Entry("fx_magic_circle.png", "魔法阵类/jn_fu_015_h.png", "copy",
          "灵合/形态切换的青色法阵"),
    Entry("fx_slash_ink.png", "刀光类/4604933d.png", "copy",
          "实体挥砍的白色笔锋（内容居中，替换 _arc）"),
    Entry("fx_smoke.png", "扭曲烟雾类/yan1.png", "key_black",
          "墨迹/命中碎屑的柔白烟团（替换 _bleed）。⚠️ 原图是**黑底**（alpha 全 255）—— "
          "必须抠黑底，否则就是一块黑方板"),
    Entry("fx_glow.png", "光点类/08ce35de.png", "copy",
          "柔光晕（替换 _glow）"),
    Entry("fx_streak1.png", "线性条状类/trail0066.png", "copy",
          "飞白拖尾 1（替换 _streaks[0]）"),
    Entry("fx_streak2.png", "线性条状类/trail014.png", "copy",
          "飞白拖尾 2（替换 _streaks[1]）"),
    Entry("fx_streak3.png", "线性条状类/sn_zxb_texiao_028_lan.png", "copy",
          "飞白拖尾 3（替换 _streaks[2]）"),

    # ---- 地形材质（喂给 tools/gen_placeholder_tiles.py）----
    Entry("terrain_mu.png", "物件特效贴图/st_hz001.png", "copy", "木境：青苔石砖", "terrain"),
    Entry("terrain_huo.png", "无缝贴图类/纹理38_td.png", "copy", "火境：金焰纹岩", "terrain"),
    Entry("terrain_tu.png", "物件特效贴图/sn_carl_qiao01.png", "copy", "土境：赭黄龟裂", "terrain"),
    Entry("terrain_jin.png", "物件特效贴图/mon713.png", "copy", "金境：冷灰云石（对比偏低，等正式美术）", "terrain"),
    Entry("terrain_shui.png", "物件特效贴图/sn_bing.png", "copy", "水境：蓝冰晶", "terrain"),
    Entry("terrain_altar.png", "物件特效贴图/feixu05.png", "copy", "祭坛：金裂石", "terrain"),
]


def key_black(im):
    """
    黑底发光图 → 带 alpha 的精灵。

    alpha = 辉度（max 通道），RGB 反预乘还原色相。
    原图自带的 alpha（如果是半透的）取较小值，避免凭空放大已有透明区。
    """
    src = np.asarray(im.convert("RGBA")).astype(np.float32)
    rgb, src_a = src[..., :3], src[..., 3]

    lum = rgb.max(axis=2)                      # 黑底贴图的「不发光」处 lum = 0
    safe = np.maximum(lum, 1e-6)
    gain = (255.0 / safe)[..., None]           # 反预乘系数

    out = np.zeros_like(src)
    out[..., :3] = np.clip(rgb * gain, 0.0, 255.0)
    out[..., 3] = np.minimum(lum, src_a)       # 取小：别把原有的透明区放大

    return Image.fromarray(out.astype(np.uint8))


def alpha_stats(im):
    a = np.asarray(im.convert("RGBA"))[..., 3]
    total = a.size
    return (
        int((a == 0).sum() * 100 // total),
        int((a == 255).sum() * 100 // total),
        int(a.min()),
        int(a.max()),
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只报告，不写文件")
    ap.add_argument("--only", default=None, help="只处理名字里含这个串的条目")
    args = ap.parse_args()

    if not SRC_ROOT.is_dir():
        print(f"找不到交付层目录 {SRC_ROOT}（要在仓库根目录跑）", file=sys.stderr)
        return 1

    entries = [e for e in ENTRIES if not args.only or args.only in e.key]

    if not entries:
        print(f"没有条目匹配 --only {args.only}", file=sys.stderr)
        return 1

    if not args.check:
        OUT_DIR.mkdir(parents=True, exist_ok=True)

    print(f"{'输出':22s} {'来源':30s} {'处理':10s} 透明度(全透/全不透)")
    print("-" * 92)

    done = 0

    for e in entries:
        src_path = SRC_ROOT / e.src

        if not src_path.is_file():
            print(f"{e.out:22s} ❌ 来源不存在：{src_path}")
            continue

        im = Image.open(src_path)

        if e.mode == "key_black":
            im = key_black(im)
        elif e.mode != "copy":
            print(f"{e.out:22s} ❌ 未知处理方式 {e.mode}")
            continue

        clear, solid, lo, hi = alpha_stats(im)

        # 这两条是「材质 / 精灵」的判据，值得当场喊出来
        if e.group == "fx" and solid == 100:
            print(f"{e.out:22s} {e.src:30s} {e.mode:10s} ⚠️ 全不透 —— 这是材质不是精灵，"
                  f"当 sprites 用会是一块方板")

        if not args.check:
            # ⚠️ `copy` 必须走**逐字节拷贝**，不能 `Image.open` 再 `save` ——
            # 那是**重新编码**，会把同一张图写出不同的字节（实测把已入库的 8 张
            # 地形/材质全改了一遍）。交付层到引擎层的\"只改名\"约定靠的就是字节一致。
            if e.mode == "copy":
                shutil.copyfile(src_path, OUT_DIR / e.out)
            else:
                im.save(OUT_DIR / e.out)

        print(f"{e.out:22s} {e.src:30s} {e.mode:10s} {clear:3d}% / {solid:3d}%  (alpha {lo}~{hi})")
        done += 1

    verb = "检查" if args.check else "写入"
    print(f"\n{verb}完成：{done} 条 → {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
