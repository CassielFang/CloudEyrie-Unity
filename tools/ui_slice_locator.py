# -*- coding: utf-8 -*-
"""
定位 美术素材/<设计稿>_slices/ 里每张切图在 美术素材/<设计稿>.png 中的**精确像素位置**。

为什么不能直接比 RGB
--------------------
切图之间是**有遮挡**的：比如灵炁条 `ztl/blood`（填充）压在 `ztl/empty`（石条+暗头冠）上，
`boss/blood` 压在 `boss/emptyblood` 上。被盖住的那块像素在设计稿里是**上面那层**的颜色，
拿下层切图的原图去比，必然对不上 —— 而且越大的切图被盖得越多。

所以这里比的是**轮廓**：

  1. 设计稿 -> 边缘图 -> 距离变换 `dt`（每个像素到最近边缘的距离）。
  2. 切图 -> 轮廓图（alpha 边界 + 内部边缘）。
  3. 滑动：`cost(位置) = mean(切图轮廓像素处的 dt)`，用 `matchTemplate` 的相关当卷积算。
     这就是倒角匹配（chamfer matching）。**完全被盖住的那段轮廓**在设计稿里没有对应边缘，
     dt 很大，所以再把 dt 截断到 TRUNC 像素 —— 截断后，被遮挡的部分贡献一个恒定值，
     不再主导位置选择，可见的那部分轮廓照常说话。

顺带能看出什么
--------------
每张切图都会记录它在设计稿里的**全部实例**（同一种按钮可能出现四五次）；
两两矩形相交时，比较交叠区的 RGB —— 谁的颜色更像设计稿谁就在**上层**，
据此输出一份层级偏序，用于决定 Unity 里的 siblingIndex 顺序。

输出：
  tools/ui_layout.json   位置（设计稿左上角为原点，y 向下）+ 层级关系
  tools/overlay_<名>.png 把定位结果画回设计稿，肉眼验收用

用法：
    /d/anaconda3/python.exe tools/ui_slice_locator.py
"""
import json
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT / "美术素材"

DESIGNS = {
    "shouye": "首页.png",
    "guanqia": "关卡主界面.png",
    "config": "弹窗-设置-音画设置.png",
}

# 倒角匹配的距离截断（像素）。太大则遮挡区会主导打分，太小则对半像素错位过敏。
TRUNC = 3.0

# 两档 alpha 阈值，用途**完全不同**，别合并：
#
#   ALPHA_SHAPE  128 —— 算**轮廓形状**用。切图的形状只要画到了就算数，
#                      描边那段半透明像素同样是形状的一部分。
#
#   ALPHA_OPAQUE 250 —— 算 **RGB 是否对得上**用。切图是带柔和 alpha 的美术图，
#                      半透明像素在设计稿里是**和背景混过色**的 ——
#                      拿切图的原始 RGB 去比，误差能到 30~40 级（实测「启境」按钮：
#                      alpha>=128 时平均误差 35.8，alpha>=254 时是 0.10）。
#                      只有完全不透明的那块才是「设计稿原样搬过来的像素」。
ALPHA_SHAPE = 128
ALPHA_OPAQUE = 250


# --------------------------------------------------------------------- 读写

def load_rgba(path):
    """
    读成 HxWx4 uint8（cv2 的 BGR 顺序 + alpha）。

    ⚠️ 必须走 imdecode 而不是 imread：OpenCV 在 Windows 上内部用窄字符 API 开文件，
    路径里只要有中文就一律打不开（而本项目的素材目录就叫「美术素材」）。
    np.fromfile 用宽字符 API 读字节，再交给 imdecode 解码，就绕开了。
    """
    buf = np.fromfile(str(path), dtype=np.uint8)
    img = cv2.imdecode(buf, cv2.IMREAD_UNCHANGED)

    if img is None:
        raise IOError(f"读不到 {path}")

    if img.ndim == 2:
        img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)

    if img.shape[2] == 3:
        img = np.dstack([img, np.full(img.shape[:2], 255, np.uint8)])

    return img


def save_rgb(path, bgr):
    ext = Path(path).suffix or ".png"
    ok, buf = cv2.imencode(ext, bgr)
    if ok:
        buf.tofile(str(path))


# --------------------------------------------------------------------- 边缘

def edge_map(bgr, alpha=None):
    """
    边缘图 = 灰度 Canny ∪ 各通道梯度。

    只用灰度会漏掉**等亮度但不同色**的边界（青玉条压在墨底上就属于这种），
    所以再补一路 LAB 三通道的梯度取最大。
    """
    gray = cv2.cvtColor(bgr, cv2.COLOR_BGR2GRAY)
    e = cv2.Canny(gray, 50, 140)

    lab = cv2.cvtColor(bgr, cv2.COLOR_BGR2LAB).astype(np.float32)
    mag = np.zeros(gray.shape, np.float32)

    for c in range(3):
        sx = cv2.Sobel(lab[:, :, c], cv2.CV_32F, 1, 0, ksize=3)
        sy = cv2.Sobel(lab[:, :, c], cv2.CV_32F, 0, 1, ksize=3)
        mag = np.maximum(mag, cv2.magnitude(sx, sy))

    e = cv2.bitwise_or(e, (mag > 90).astype(np.uint8) * 255)

    if alpha is not None:
        e[alpha < ALPHA_SHAPE] = 0

    return e


def silhouette_edges(alpha):
    """alpha 的轮廓线（一圈 1px 宽的边界）。"""
    m = (alpha >= ALPHA_SHAPE).astype(np.uint8) * 255
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    grad = cv2.morphologyEx(m, cv2.MORPH_GRADIENT, k)
    # 梯度算子会把边界撑到 2px，细化回 1px —— 轮廓越细，匹配越锐
    return cv2.Canny(grad, 50, 140)


# --------------------------------------------------------------------- 匹配

def chamfer_cost(dt_trunc, templ_edge):
    """
    在 dt 上滑动 templ，返回每个位置的**平均截断距离**（越小越好）。

    templ 是 0/1 float，所以相关 = 把轮廓处的 dt 加起来 / 轮廓像素数。
    """
    t = templ_edge.astype(np.float32)

    if t.sum() == 0:
        return None

    res = cv2.matchTemplate(dt_trunc, t, cv2.TM_CCORR)
    return res / float(t.sum())


def nms_peaks(cost, tw, th, limit=12):
    """
    从代价图里挑出局部极小，按代价排序，按模板尺寸做非极大值抑制。

    同一张切图在设计稿里可能有多个实例（左右箭头各有 5 个），所以这里返回列表而不是单个最优点。
    """
    lo = float(cost.min())
    hi = float(cost.max())

    # 接受阈值：比最优差不过一个「半截断」，且不超过截断本身 —— 两重限制都要有，
    # 否则平坦区域（整片代价都低）会一口气冒出一堆假实例。
    accept = min(lo + 0.5 * TRUNC, lo + 0.25 * (hi - lo))
    ys, xs = np.where(cost <= accept)

    order = np.argsort(cost[ys, xs])
    xs, ys = xs[order], ys[order]

    picked = []

    for x, y in zip(xs.tolist(), ys.tolist()):
        if any(abs(x - px) < tw * 0.6 and abs(y - py) < th * 0.6 for px, py, _ in picked):
            continue

        picked.append((x, y, float(cost[y, x])))

        if len(picked) >= limit:
            break

    return picked


def rgb_report(design, templ, x, y):
    """
    用 RGB 复核一个位置：在切图**实心**像素里，设计稿颜色和切图颜色有多接近。

    返回 (inlier 比例, inlier 上的平均绝对误差)。
    inlier = 单像素误差 < 12 级的那些点。

    遮挡会让 inlier 比例下降（被盖住的那块对不上），**但对错位置会掉到接近 0** ——
    所以它是个很好的「这位置对不对」的旁证，而且天然容忍遮挡。
    """
    th, tw = templ.shape[:2]
    mask = templ[:, :, 3] >= ALPHA_OPAQUE

    if mask.sum() == 0:
        return 0.0, 255.0

    patch = design[y:y + th, x:x + tw, :3].astype(np.int16)
    t = templ[:, :, :3].astype(np.int16)

    err = np.abs(patch - t).max(axis=2)[mask]
    inl = err < 12

    return float(inl.mean()), float(err[inl].mean()) if inl.any() else 255.0


# --------------------------------------------------------------------- 层级

def overlap_winner(a, b, design):
    """
    两张切图的矩形相交时，判断谁在上层。

    取交叠区里**双方都实心**的像素，各自和设计稿比：误差小的是被画在上面的那个
    （因为设计稿在那儿画的就是它）。谁都没有足够多的公共像素就返回 None（无法判定）。
    """
    ax, ay, aw, ah = a["x"], a["y"], a["w"], a["h"]
    bx, by, bw, bh = b["x"], b["y"], b["w"], b["h"]

    x0, y0 = max(ax, bx), max(ay, by)
    x1, y1 = min(ax + aw, bx + bw), min(ay + ah, by + bh)

    if x1 <= x0 or y1 <= y0:
        return None

    # 各自裁出交叠区
    def crop(s, sa):
        return s[(y0 - sa["y"]):(y0 - sa["y"]) + (y1 - y0),
                 (x0 - sa["x"]):(x0 - sa["x"]) + (x1 - x0)]

    sub_a = crop(a["sprite"], a)
    sub_b = crop(b["sprite"], b)

    if sub_a.size == 0 or sub_b.size == 0:
        return None

    both = (sub_a[:, :, 3] >= ALPHA_OPAQUE) & (sub_b[:, :, 3] >= ALPHA_OPAQUE)

    if both.sum() < 40:      # 公共像素太少，判不出来
        return None

    d = design[y0:y1, x0:x1, :3].astype(np.int16)
    ea = float(np.abs(d - sub_a[:, :, :3].astype(np.int16)).max(axis=2)[both].mean())
    eb = float(np.abs(d - sub_b[:, :, :3].astype(np.int16)).max(axis=2)[both].mean())

    if abs(ea - eb) < 3.0:       # 差距太小，不敢下判断
        return None

    return "a" if ea < eb else "b"


def topo_order(names, above_pairs):
    """
    按「谁在谁上面」的偏序排出从下到上的顺序（Kahn 拓扑排序，无解的边直接丢）。

    above_pairs 里是 (上层, 下层)。返回从底到顶的列表。
    """
    below = {n: set() for n in names}       # n 的下层集合

    for up, dn in above_pairs:
        if up != dn:
            below[up].add(dn)

    result = []
    remaining = set(names)

    while remaining:
        # 找出「下面没有还没排的人」的那些 —— 它们可以放最底
        ready = [n for n in remaining if not (below[n] & remaining)]

        if not ready:                       # 环：随便挑一个打破
            ready = [sorted(remaining)[0]]

        ready.sort()
        result.extend(ready)
        remaining -= set(ready)

    return result


# --------------------------------------------------------------------- 主流程

def main():
    out = {}
    warnings = []

    for key, fname in DESIGNS.items():
        design_path = ART / fname
        slices_dir = ART / (design_path.stem + "_slices")

        if not design_path.exists() or not slices_dir.is_dir():
            print(f"[跳过] {key}: 缺设计稿或切图目录", file=sys.stderr)
            continue

        design = load_rgba(design_path)
        dh, dw = design.shape[:2]

        # 设计稿的距离场只跟设计稿有关，整个循环共用一次
        dt = cv2.distanceTransform(255 - edge_map(design[:, :, :3]), cv2.DIST_L2, 3)
        dt_trunc = np.minimum(dt, TRUNC).astype(np.float32)

        print(f"\n=== {key}  {fname}  {dw}x{dh} ===")

        entries = {}

        for f in sorted(slices_dir.iterdir()):
            if f.suffix.lower() != ".png":
                continue

            # @2x 是同一张图的 2 倍图，位置与 1x 完全一致，不必再定位一次
            if "@2x" in f.stem:
                continue

            # "(1)" "(2)" 是同一切图的其它状态（如箭头的 hover 态），尺寸一致 -> 位置一致，
            # 只在基名上做定位，避免同一张图被匹配掉好几遍
            base = f.stem
            if base.endswith(")") and "(" in base:
                continue

            sprite = load_rgba(f)
            th, tw = sprite.shape[:2]

            # 整屏背景：两个方向都占满，只可能贴在 (0,0)，不必匹配。
            # ⚠️ 必须**两个方向都**判，不能写成 or —— 分隔线 `line/yhsz` 是 1920x5，
            # 宽度等于设计稿宽度但高度只有 5，用 or 会让它跳过匹配贴到 (0,0)（实测踩过）。
            if tw >= dw * 0.98 and th >= dh * 0.98:
                entries[base] = {
                    "x": 0, "y": 0, "w": tw, "h": th,
                    "cost": 0.0, "inlier": 1.0, "mae": 0.0,
                    "instances": [{"x": 0, "y": 0}],
                    "sprite": sprite,
                }
                print(f"  {base:28s} (    0,    0) {tw:5d}x{th:<5d} 整屏背景")
                continue

            edges = cv2.bitwise_or(
                silhouette_edges(sprite[:, :, 3]),
                edge_map(sprite[:, :, :3], sprite[:, :, 3]))

            cost = chamfer_cost(dt_trunc, edges)

            if cost is None:
                warnings.append(f"{key}/{base}: 算不出轮廓")
                print(f"  !! {base} 算不出轮廓")
                continue

            peaks = nms_peaks(cost, tw, th)

            if not peaks:
                warnings.append(f"{key}/{base}: 没有候选位置")
                print(f"  !! {base} 没有候选位置")
                continue

            # 多个候选时用 RGB inlier 复核，选最像的那个当主位置
            scored = [(rgb_report(design, sprite, x, y)[0], x, y, c) for x, y, c in peaks]
            scored.sort(key=lambda t: (-t[0], t[3]))

            best_inl, bx, by, best_cost = scored[0]
            _, best_mae = rgb_report(design, sprite, bx, by)

            entries[base] = {
                "x": bx, "y": by, "w": tw, "h": th,
                "cost": round(best_cost, 4),
                "inlier": round(best_inl, 4),
                "mae": round(best_mae, 3),
                "instances": [{"x": x, "y": y} for _, x, y, _ in scored],
                "sprite": sprite,
            }

            flag = ""

            if best_inl < 0.9:
                flag = f"  <-- inlier 只有 {best_inl:.0%}（可能被遮挡，或定位存疑）"

            if len(scored) > 1:
                flag += f"  <-- 设计稿里有 {len(scored)} 处"

            print(f"  {base:28s} ({bx:5d},{by:5d}) {tw:5d}x{th:<5d} "
                  f"cost={best_cost:5.3f} inlier={best_inl:6.1%}{flag}")

            if best_inl < 0.6:
                warnings.append(f"{key}/{base}: inlier={best_inl:.3f}")

        # ---- 层级：两两矩形相交就判一次上下 ----
        names = list(entries.keys())
        pairs = []

        for i in range(len(names)):
            for j in range(i + 1, len(names)):
                r = overlap_winner(entries[names[i]], entries[names[j]], design)

                if r == "a":
                    pairs.append((names[i], names[j]))
                elif r == "b":
                    pairs.append((names[j], names[i]))

        order = topo_order(names, pairs)        # 从底到顶

        print(f"  层级（下 -> 上）：{' < '.join(order)}")

        # 落盘前把 sprite 抠掉（JSON 里放不下）
        for e in entries.values():
            e.pop("sprite", None)

        out[key] = {
            "design": fname,
            "design_w": dw,
            "design_h": dh,
            "slices": entries,
            "z_bottom_to_top": order,
            "z_relations": [{"above": a, "below": b} for a, b in pairs],
        }

        print_unity_table(key, entries)

        draw_overlay(key, design, entries, dw, dh)

    dst = ROOT / "tools" / "ui_layout.json"
    dst.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\n写出 {dst}")

    if warnings:
        print(f"\n{len(warnings)} 项需要人工确认：")
        for w in warnings:
            print(f"  {w}")

    return 0


def print_unity_table(key, entries):
    """
    把设计稿坐标换算成 Unity 画布坐标，打成一张可以直接抄进 C# 的表。

    换算（设计稿 1920x1080，原点左上、y 向下 -> Unity 画布原点中心、y 向上）：
        cx     = (x + w/2) - 设计宽/2         居中式摆放用
        cy     = 设计高/2 - (y + h/2)
        left   = x                            贴边式摆放（HUD）用
        bottom = 设计高 - (y + h)

    ⚠️ 取整用 floor(v + 0.5) 而**不是** Mathf.Round 那种「四舍六入五成双」：
    切图宽高常是奇数（菜单按钮 307、徽记 549），中心必然落在 *.5 上，
    偶数舍入会把这几个中心一半往上、一半往下，**间距就散了**（实测 99/100 混着出现）。
    统一往一个方向收，间距才是齐的。
    """
    dw = 1920.0
    dh = 1080.0

    def half_up(v):
        return int(np.floor(v + 0.5))

    print(f"  ---- {key} 换成 Unity 画布坐标（画布 1920x1080，原点中心，y 向上）----")
    print(f"  {'切图':28s} {'cx':>7s} {'cy':>7s} {'left':>6s} {'bottom':>7s}   w x h")

    for name, e in entries.items():
        x, y, w, h = e["x"], e["y"], e["w"], e["h"]
        cx = half_up((x + w / 2) - dw / 2)
        cy = half_up(dh / 2 - (y + h / 2))
        left = x
        bottom = half_up(dh - (y + h))
        print(f"  {name:28s} {cx:7d} {cy:7d} {left:6d} {bottom:7d}   {w} x {h}")


def draw_overlay(key, design, entries, dw, dh):
    """把定位结果画回设计稿，供肉眼验收。"""
    canvas = design[:, :, :3].copy()
    names = list(entries.keys())

    for idx, name in enumerate(names):
        e = entries[name]
        hue = int(180 * idx / max(1, len(names)))
        hsv = np.uint8([[[hue, 255, 255]]])
        bgr = cv2.cvtColor(hsv, cv2.COLOR_HSV2BGR)[0, 0]
        color = (int(bgr[0]), int(bgr[1]), int(bgr[2]))

        x, y, w, h = e["x"], e["y"], e["w"], e["h"]
        cv2.rectangle(canvas, (x, y), (x + w, y + h), color, 2)

        label = f"{idx}:{name}"[:34]
        ty = max(14, y - 5)
        cv2.putText(canvas, label, (x + 2, ty), cv2.FONT_HERSHEY_SIMPLEX,
                    0.5, (0, 0, 0), 3, cv2.LINE_AA)
        cv2.putText(canvas, label, (x + 2, ty), cv2.FONT_HERSHEY_SIMPLEX,
                    0.5, color, 1, cv2.LINE_AA)

    save_rgb(ROOT / "tools" / f"overlay_{key}.png", canvas)
    print(f"  验收图 tools/overlay_{key}.png")


if __name__ == "__main__":
    sys.exit(main())
