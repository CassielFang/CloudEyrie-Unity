#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把设计稿 `docs/design/五行关卡设计v3.html` 里的**关卡侧视图**解析成关卡布局数据。

## 为什么以 HTML 为准

那份文档里每境都有一张 `<svg class="map-svg">` 侧视图，把
**地面段 / 平台 / 机关 / 敌人 / 碎片 / 传送门**的坐标全画出来了。
它才是摆放的真相源 —— 场景里的手摆版本从段 3 开始逐段右漂
（木境灵泉偏 +1234），就是没照着这张图摆。

## 坐标换算（关键，别乱改）

- **横向**：SVG 宽 → 关卡宽（`LevelDefinition.levelRight`），系数 `sx = right / svgWidth`
- **纵向**：单独取 `VSCALE = 4.0`，**不是** sx。
  设计稿台阶高 30 SVG 单位；用 sx(≈5.2) 会得到 156 世界单位，
  而一次跳跃只有 `v²/2g = 320²/(2×320) = 160` —— 几乎顶满，跳不上去。
  取 4.0 → 台阶 120，留出余量。
- **基准**：SVG 的地面顶线 `y=250` 对应世界 `GROUND_Y = -295`（沿用旧 LevelDirector 的常量）

## 输出

`Assets/Editor/LevelLayoutData.cs` —— 一份静态数据表，供
`Assets/Editor/LevelLayoutBuilder.cs` 重建场景时读取。
选 C# 静态表而不是 JSON：少一层解析、少一种运行期失败，且 diff 可读。

## 用法

    python tools/gen_level_layout.py
"""

import io
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HTML = Path("docs/design/五行关卡设计v3.html")
OUT = Path("Assets/Editor/LevelLayoutData.cs")

GROUND_SVG_Y = 250.0
GROUND_Y = -295.0
VSCALE = 4.0

# 每境：关卡宽 + 颜色 → 语义
# ⚠️ 五境的配色**各画各的**，同一个颜色在不同境里意思不同（土境的 #e8785c 是落石预警，
#    火境的 #e8785c 是火柱），所以必须按境分别映射。改设计稿配色要同步改这里。
CFG = {
    "Mu": (8832, {
        "#3a5a44": "Ground", "#6ec88a": "Platform", "#e8a8c8": "Bounce",
        "#8a6a3a": "Seed", "#7ec8e8": "Spring", "#ffd700": "Fragment",
        "#c8503c": "Enemy", "#e8785c": "Portal",
    }),
    "Huo": (9600, {
        "#5a3430": "Ground", "#8a8a9a": "Platform", "#e8785c": "FirePillar",
        "#ff9848": "Lava", "#6a5a4a": "Brazier", "#c8503c": "Enemy",
        # 设计稿用浅蓝小圆单独标「弓手」：和红色近战是两种敌人，不能合并成一个 kind
        "#b8c4d4": "EnemyRanged", "#7ec8e8": "Spring", "#d8b06a": "Portal",
        "#ffd700": "Fragment",
    }),
    "Tu": (9600, {
        "#4a3d2a": "Ground", "#d8b06a": "Platform", "#d8c898": "Quicksand",
        "#8a8a8a": "PushRock", "#a89878": "FallingRock", "#e8785c": "RockWarning",
        "#7ec8e8": "Spring", "#c8503c": "Enemy", "#ffd700": "Fragment",
        "#d4dce8": "Portal",
    }),
    "Jin": (9600, {
        "#3a3f4a": "Ground", "#8ab8d8": "Platform", "#c8a86a": "SwordStele",
        "#d4dce8": "SwordWheel", "#b8c4d4": "EnemyRanged", "#c8503c": "Enemy",
        "#7ec8e8": "Spring", "#ffd700": "Fragment", "#d8b06a": "Portal",
        "#6ab8d8": "Portal",
    }),
    "Shui": (9600, {
        # ⚠️ #4a8ab8 是**水面**（溪流/水池，带 opacity），不是可站立平台 ——
        #    它的形状是横贯整段的长条，当成 Platform 会生成一整块隐形实心地面，
        #    玩家能踩着"水面"走过去。
        "#2a3d4a": "Ground", "#6ab8d8": "Platform", "#4a8ab8": "Water",
        "#9ad8e8": "WaterCurtain", "#e8a8c8": "Bounce", "#e8785c": "FirePillar",
        "#d8b06a": "WaterClock", "#c8a86a": "WaterClock", "#d4dce8": "SwordWheel", "#c8503c": "Enemy",
        "#b8c4d4": "EnemyRanged", "#7ec8e8": "Spring", "#ffd700": "Fragment",
        "#d4c4a8": "Portal",
    }),
}


def attr(attrs, key):
    m = re.search(key + r'="([^"]*)"', attrs)
    return m.group(1) if m else ""


def parse_svg(svg):
    """返回 [(kind, x, y, w, h)]，x/y 是**左上角**的世界坐标。"""
    out = []

    # 已经出现过的椭圆/圆的圆心：同心的第二个图形是**装饰**（传送门内芯光晕、
    # 剑轮轴心圆点），不是独立物件。
    # 不排掉的话，传送门内芯会被当成第二个传送门/平台/剑轮（土境甚至会被
    # 当成 RockWarning），重排时会去找一个不存在的对象。
    seen_centers = []

    for m in re.finditer(r"<(rect|ellipse|circle|polygon)\b([^>]*)>", svg):
        tag, attrs = m.group(1), m.group(2)
        fill = attr(attrs, "fill").lower()
        stroke = attr(attrs, "stroke").lower()

        if tag in ("ellipse", "circle"):
            cx, cy = float(attr(attrs, "cx")), float(attr(attrs, "cy"))

            if any(abs(cx - px) < 0.5 and abs(cy - py) < 0.5 for px, py in seen_centers):
                continue

            seen_centers.append((cx, cy))

        if fill == "none":
            # 空心图形靠形状 + 描边区分：椭圆 = 传送门；圆 = 剑轮 / 水钟（看描边色）
            if tag == "ellipse":
                kind = "Portal"
            elif tag == "circle":
                kind = CFG_KIND.get(stroke)
            else:
                kind = None
        else:
            kind = CFG_KIND.get(fill)

        if not kind:
            continue

        # 同一支青色既画灵障（矩形）又画灵泉（圆），按形状分
        if kind == "Spring" and tag == "rect":
            kind = "Barrier"

        if tag == "rect":
            x, y = float(attr(attrs, "x")), float(attr(attrs, "y"))
            w, h = float(attr(attrs, "width")), float(attr(attrs, "height"))
        elif tag == "ellipse":
            rx, ry = float(attr(attrs, "rx")), float(attr(attrs, "ry"))
            x, y = float(attr(attrs, "cx")) - rx, float(attr(attrs, "cy")) - ry
            w, h = rx * 2, ry * 2
        elif tag == "circle":
            r = float(attr(attrs, "r"))
            x, y = float(attr(attrs, "cx")) - r, float(attr(attrs, "cy")) - r
            w = h = r * 2
        else:  # polygon
            pts = [tuple(map(float, p.split(","))) for p in attr(attrs, "points").split()]
            x, y = min(p[0] for p in pts), min(p[1] for p in pts)
            w = max(p[0] for p in pts) - x
            h = max(p[1] for p in pts) - y

        out.append((
            kind,
            round(x * SX),
            round(GROUND_Y + (GROUND_SVG_Y - y) * VSCALE),
            round(w * SX),
            round(h * VSCALE),
        ))

    return out


def main():
    global SX, CFG_KIND

    if not HTML.is_file():
        print(f"找不到 {HTML}（要在仓库根目录跑）", file=sys.stderr)
        return 1

    text = HTML.read_text(encoding="utf-8", errors="replace")
    blocks = re.findall(r'(<svg[^>]*class="map-svg"[^>]*>)(.*?)</svg>', text, flags=re.S)

    if len(blocks) != len(CFG):
        print(f"⚠️ SVG 数量 {len(blocks)} 与配置 {len(CFG)} 不符", file=sys.stderr)

    lines = [
        "// 由 tools/gen_level_layout.py 从 docs/design/五行关卡设计v3.html 的 SVG 侧视图生成。",
        "// **不要手改**：改设计稿后重跑脚本。",
        "// 坐标：x/y 为左上角，世界单位；纵向系数 4.0（不是横向比例，见脚本注释）。",
        "namespace CloudEyrie.EditorTools",
        "{",
        "    public static class LevelLayoutData",
        "    {",
    ]

    for (name, (right, colors)), (open_tag, svg) in zip(CFG.items(), blocks):
        width = int(re.search(r'viewBox="0 0 (\d+)', open_tag).group(1))
        SX = right / width
        CFG_KIND = colors

        items = parse_svg(svg)
        print(f"{name}: {len(items)} 个元素（sx={SX:.3f}）")

        lines.append(f"        // {name}：SVG {width} 宽 → 世界 {right}（sx={SX:.2f}）")
        lines.append(f"        public static readonly LayoutItem[] {name} =" + " {")

        for kind, x, y, w, h in items:
            lines.append(f'            new LayoutItem("{kind}", {x}, {y}, {w}, {h}),')

        lines.append("        };")
        lines.append("")

    lines += ["    }", "}", ""]

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"→ {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
