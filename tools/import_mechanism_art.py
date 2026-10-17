#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把美术交付的**机关物件**从交付层搬进引擎层。

## 为什么要有这份表

交付层 `美术素材/物件机关/<五行>/` 是美术按境交的原图（中文名、带 `（终版` `（改版` 后缀）。
引擎层要用英文 snake_case，且必须落在某个 `Resources/` 之下 —— 否则
`CloudEyrieTextureImporter` 不生效、按 PPU=100 导入、`Resources.Load<Sprite>` 静默返回 null。

上一轮（`fx_tex`）就是 `cp` 手工拷的，出处只留在提交说明里，后来没人说得清对应关系。
这份表把「交付层哪个文件 → 引擎层哪个名字」钉死。

## 为什么机关美术放全局 `Assets/Resources/mechanisms/` 而不是每关一份

`MovingPlatform` / `VinePlatform` / `SwingPlatform` 是**跨关复用的 prefab**
（一块「摆荡藤台」在木境、火境、水境都有）。美术却是按机制各画一张
（升降石柱 / 磁力平台 / 水流浮台 / 沉浮浮石 是四个不同的东西）。
同一张图被两关用到（`火_间歇火柱` 在火境、水境都有 Recap），
每关一份就会分叉。Unity 的 Resources 本来就是全局命名空间，放一份最省事。

## 用法

    python tools/import_mechanism_art.py            # 全部导入
    python tools/import_mechanism_art.py --check    # 只报告
"""

import argparse
import shutil
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SRC = Path("美术素材/物件机关")
OUT = Path("Assets/Resources/mechanisms")

# (引擎层文件名, 交付层相对路径, 用途说明)
ENTRIES = [
    ("bounce_flower.png",     "木/木_弹跳花_待机（终版.png",           "弹跳花（BounceFlower）"),
    ("swing_vine.png",        "木/木_摆荡藤台（终版.png",             "摆荡藤台（SwingPlatform）"),
    ("vine_seed.png",         "木/木_藤蔓种子_待触发（终版.png",       "藤蔓种子（VineSeed）"),
    ("lava_strip.png",        "火/火_岩浆_横向条（改版.png",            "岩浆横向条（Hazard·常驻）"),
    ("float_stone.png",       "火/火_沉浮浮石_升起（改版.png",          "沉浮浮石（MovingPlatform）"),
    ("brazier.png",           "火/火_火盆_点燃（改版.png",            "火盆（Brazier）"),
    ("fire_pillar.png",       "火/火_间歇火柱_喷发（改版.png",          "间歇火柱（Hazard·周期性）"),
    ("lift_pillar.png",       "土/机关_升降石柱.png",                 "升降石柱（MovingPlatform）"),
    ("push_rock.png",         "土/机关_可推玄石.png",                 "可推玄石（PushableRock）"),
    ("quicksand.png",         "土/机关_流沙.png",                     "流沙（Quicksand）"),
    ("falling_rock.png",      "土/机关_预警落石.png",                 "预警落石（FallingRock.rock）"),
    ("sword_stele.png",       "金/机关_剑碑.png",                     "剑碑（SwordStele）"),
    ("sword_wheel.png",       "金/机关_旋转剑轮.png",                 "旋转剑轮（SwordWheel）"),
    ("magnetic_platform.png", "金/机关_磁力平台1.png",                "磁力平台（MovingPlatform）"),
    ("water_curtain.png",     "水/机关_水幕.png",                     "水幕（ElementBarrier·水境）"),
    ("water_raft.png",        "水/机关_水流浮台.png",                 "水流浮台（MovingPlatform）"),
    ("bronze_clock.png",      "水/机关_青铜水钟.png",                 "青铜水钟（场景里挂的是 SwordStele 组件）"),
]

# 整关背景：直接覆盖引擎层同名文件（尺寸已核对 9600×1080，与关宽一致）
# 水 / 金 这两张是美术后来补的，交付层用的是哈希文件名，靠这张表记住它是什么。
SCROLLS = [
    ("美术素材/物件机关/木/木长图(最终版.png",         "Assets/Levels/Mu/Resources/Mu/bg/mu_far.png",     "木·青芽境"),
    ("美术素材/物件机关/水/b9cc00e241eb193a14a5f644203f935a.png", "Assets/Levels/Shui/Resources/Shui/bg/shui_far.png", "水·玄泽境"),
    ("美术素材/物件机关/金/04f00334ecf8b17434e41abab47d461e.png", "Assets/Levels/Jin/Resources/Jin/bg/jin_far.png",   "金·白锋境"),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    if not SRC.is_dir():
        print(f"找不到交付层目录 {SRC}（要在仓库根目录跑）", file=sys.stderr)
        return 1

    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)

    ok = 0

    for out_name, rel, note in ENTRIES:
        src = SRC / rel

        if not src.is_file():
            print(f"❌ 缺文件：{src}")
            continue

        if not args.check:
            shutil.copyfile(src, OUT / out_name)

        print(f"{out_name:24s} ← {rel:36s} {note}")
        ok += 1

    for src, dst, label in SCROLLS:
        if Path(src).is_file():
            if not args.check:
                shutil.copyfile(src, dst)
            print(f"长卷 {label:6s} ← {Path(src).name[:30]:32s} 覆盖 {dst}")
        else:
            print(f"❌ 缺长卷：{src}")

    verb = "检查" if args.check else "导入"
    print(f"\n{verb}完成：{ok} 个机关物件 + 长卷")
    return 0


if __name__ == "__main__":
    sys.exit(main())
