"""把生成的 mp4 打包成 `Assets/Resources/video_fx/<名>/` 的图集序列。

产物格式与工程里已有的序列帧**逐字段一致**（小怪-行走 / 九尾狐-幻影 等）：
    <名>/manifest.json  +  <名>/page_00.png, page_01.png, ...

manifest 字段：name / canvas / scale / sourceCanvas / fps / frames / loop /
anchor / source / layout / pageSize / pages / entries / grid。
`Fx/SequencePlayer` 按 entries 逐帧切子图，pivot 取中心。

为什么要这个工具：`docs/design/游戏机制.md` 第八节写的 `pack_atlas.py` 是 Cocos
时代留在旧工程里的，迁移时没有搬过来，仓库里只剩产物。这里是按产物逆向补齐的版本。

处理链：
    mp4 → 逐帧控白底（从边缘泛洪，不吃角色内部的浅色）
        → 统一缩放（按「站立身高」对齐，跨动作尺寸一致）
        → 放进 canvas（脚底对齐 baseline，首帧定锚点）
        → 网格拼页 → manifest

用法：
    python tools/pack_seq_atlas.py --mp4 <clip>.mp4 --name 青禾-待机 \
        --out Assets/Resources/video_fx/青禾-待机 --canvas 384 --fps 16 --loop

依赖：opencv-python、numpy、Pillow（远端 anime-anim 环境已具备）。
"""
import argparse
import json
import math
import os
import sys

import cv2
import numpy as np
from PIL import Image

WHITE_TOL = 246        # 三通道都 >= 此值才算“白底种子”
FLOOD_TOL = 10         # 泛洪容差：纸面有轻微渐变
PAGE_MAX = 2048        # 单页纹理上限
GAP = 2                # 帧之间的间隙，防采样串色


def load_frames(mp4):
    cap = cv2.VideoCapture(mp4)
    if not cap.isOpened():
        raise SystemExit(f"打不开视频：{mp4}")
    frames = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        frames.append(f)
    cap.release()
    if not frames:
        raise SystemExit(f"视频里没有帧：{mp4}")
    return frames


def key_background(bgr):
    """白底 → alpha，按**每帧自己的边缘色**估背景。

    ⚠️ 这版是踩了两次坑之后重写的。生成视频的背景**不是恒定 255**：
    实测同一段里首帧角上 248，后半段掉到 234~237（模型把纸白越画越灰）。
    固定阈值 246 的结果是：
    - 前几帧勉强抠掉，中段整帧判定不出背景 → 灰底方板；
    - 边缘种子落在 237 的区域时，泛洪会**灌进角色**，整帧变全透明。

    现在的做法：
    1. 取 1px 边框的中位数当这一帧的背景色（相机固定、背景是平的，这个估计很稳）；
    2. 只把「与该色距离 ≤ 容差 且 与边框连通」的像素判为背景；
    3. 若判定出的背景面积异常（<2% 或 >85%），退回「与边框色差」的纯阈值判断，
       宁可留一点点底也不要整帧消失。
    """
    h, w = bgr.shape[:2]
    ring = np.concatenate([bgr[0, :], bgr[h - 1, :], bgr[:, 0], bgr[:, w - 1]])
    bg_color = np.median(ring, axis=0)

    diff = np.abs(bgr.astype(np.int16) - bg_color.astype(np.int16))
    near = (diff.max(axis=2) <= FLOOD_TOL).astype(np.uint8) * 255

    mask = np.zeros((h + 2, w + 2), np.uint8)
    seeds = [(x, y) for (x, y) in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
             if near[y, x]]
    if not seeds:
        for x in range(w):
            if near[0, x]:
                seeds = [(x, 0)]
                break
    for (x, y) in seeds:
        cv2.floodFill(near, mask, (x, y), 128,
                      loDiff=FLOOD_TOL, upDiff=FLOOD_TOL, flags=8)

    background = (near == 128)
    filled = float(background.mean())

    if filled < 0.02 or filled > 0.85:
        background = diff.max(axis=2) <= FLOOD_TOL
        filled = float(background.mean())

    alpha = np.where(background, 0, 255).astype(np.uint8)
    alpha = cv2.GaussianBlur(alpha, (3, 3), 0.6)
    alpha[background & (alpha < 24)] = 0

    rgba = cv2.cvtColor(bgr, cv2.COLOR_BGR2BGRA)
    rgba[:, :, 3] = alpha
    return rgba


def best_loop_window(frames, expect=0):
    """在循环类片段里裁出**一个步态周期**。

    ⚠️ 为什么要裁：这些片子都从站立基准帧起步，第 0 帧是站着，接着才迈步。
    整段循环时每圈都会闪回站姿。

    ⚠️ 为什么不能纯自动：一开始按"相隔 L 帧差异最小"搜索，结果永远收敛到很小的 L
    （相邻帧本来就最像）——实测把待机 80 帧压成 9 帧、格挡 43 帧压成 5 帧，动作全丢了。
    所以必须给定**预期周期**（source 帧数，16fps），只在它 ±25% 范围内找最优点。
    """
    import numpy as _np

    n = len(frames)
    if n < 8:
        return 0, n

    start = max(0, int(n * 0.15))          # 跳过从站姿起步的过渡段

    if expect <= 0:
        return 0, n                        # 没给预期就不裁，至少不会把动作剪碎

    vecs = []
    for f in frames:
        g = cv2.cvtColor(f, cv2.COLOR_BGRA2BGR)
        g = cv2.resize(g, (32, 32), interpolation=cv2.INTER_AREA)
        vecs.append(g.astype(_np.float32) / 255.0)

    lo = max(2, int(expect * 0.75))
    hi = max(lo + 1, int(expect * 1.25))

    best_l, best_d = expect, None
    for lag in range(lo, hi + 1):
        if start + lag >= n:
            break
        d = 0.0
        cnt = 0
        for i in range(start, n - lag):
            d += float(_np.abs(vecs[i] - vecs[i + lag]).mean())
            cnt += 1
        if cnt == 0:
            continue
        d /= cnt
        if best_d is None or d < best_d:
            best_d, best_l = d, lag

    best_l = min(best_l, n - start)
    return start, best_l


def settle_cut(frames, window=14):
    """一次性动作的收势裁切：在尾部找与**首帧（站姿）**最像的一帧，裁到那里。

    一次性动作不需要循环，但要能接回待机/行走：结尾如果停在"挥完还举着剑"的姿势，
    切回去会硬跳。这里让片尾尽量回到与开头一致的姿态。
    """
    import numpy as _np

    n = len(frames)
    if n < window + 3:
        return n

    def vec(f):
        g = cv2.cvtColor(f, cv2.COLOR_BGRA2BGR)
        return cv2.resize(g, (32, 32), interpolation=cv2.INTER_AREA).astype(_np.float32) / 255.0

    v0 = vec(frames[0])
    best_i, best_d = n, None
    for i in range(n - window, n):
        d = float(_np.abs(vec(frames[i]) - v0).mean())
        if best_d is None or d < best_d:
            best_d, best_i = d, i

    return best_i


def content_bbox(rgba):
    """主体包围盒。

    阈值必须**很高**：抠图后人物边缘外面还留着一圈很淡的背景残留（alpha 几十），
    用 >40 会把包围盒撑大 20~30%。人物越矮被撑得越夸张 —— 宽景基准帧的片段
    因此被算成"比常规片矮 25%"，缩放补偿完全失效。
    这里只认 alpha > 200（近乎不透明）且每行/列至少 5 个这样的像素。
    """
    a = rgba[:, :, 3]
    solid = a > 200
    cols = solid.sum(axis=0)
    rows = solid.sum(axis=1)
    xs = np.where(cols >= 5)[0]
    ys = np.where(rows >= 5)[0]

    if len(xs) == 0 or len(ys) == 0:
        # 化雾 / 入合 / 灵炁爆发这几条，人物会散成低 alpha 的雾，严格判据量不到东西。
        # 退回宽松阈值，否则这些帧会被当成"空帧"整段剔掉。
        soft = a > 40
        xs = np.where(soft.sum(axis=0) >= 3)[0]
        ys = np.where(soft.sum(axis=1) >= 3)[0]

    if len(xs) == 0 or len(ys) == 0:
        return None

    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def best_loop_cut(frames, window=12):
    """找一个“与首帧最像”的靠后帧，作为循环切点（不含该帧）。
    返回应保留的帧数。"""
    if len(frames) < window + 2:
        return len(frames)
    f0 = frames[0].astype(np.int16)
    best, best_d = len(frames), None
    for j in range(len(frames) - window, len(frames)):
        d = float(np.abs(frames[j].astype(np.int16) - f0).mean())
        if best_d is None or d < best_d:
            best_d, best = d, j
    return best


def compose(rgba, scale, canvas, anchor_xy, pivot_bbox=None):
    """把整帧按 scale 缩放后贴进 canvas。
    pivot_bbox 给定时，用它（而不是本帧包围盒）决定落点 —— 保证整段动画
    的位移不被逐帧抵消。"""
    h, w = canvas
    sw, sh = max(1, round(rgba.shape[1] * scale)), max(1, round(rgba.shape[0] * scale))
    small = cv2.resize(rgba, (sw, sh), interpolation=cv2.INTER_AREA)

    bbox = content_bbox(rgba) or (0, 0, rgba.shape[1], rgba.shape[0])
    # 锚点 = 首帧包围盒的「水平中心 + 脚底」；不能取左边，否则整个角色会偏到右侧
    px, py = pivot_bbox or ((bbox[0] + bbox[2]) / 2, bbox[3])
    del bbox
    ax = sw * (px / rgba.shape[1])
    ay = sh * (py / rgba.shape[0])

    out = np.zeros((h, w, 4), np.uint8)
    tx = int(round(anchor_xy[0] - ax))
    ty = int(round(anchor_xy[1] - ay))

    x0, y0 = max(0, tx), max(0, ty)
    x1, y1 = min(w, tx + sw), min(h, ty + sh)
    if x1 <= x0 or y1 <= y0:
        return out
    sx0, sy0 = x0 - tx, y0 - ty
    out[y0:y1, x0:x1] = small[sy0:sy0 + (y1 - y0), sx0:sx0 + (x1 - x0)]
    return out


def pack_pages(cells, cell):
    """网格拼页。每页最多 floor(PAGE_MAX/(cell+gap))² 格。"""
    n = max(1, PAGE_MAX // (cell + GAP))
    per_page = n * n
    pages = []
    entries = []
    for start in range(0, len(cells), per_page):
        chunk = cells[start:start + per_page]
        page = np.zeros((n * (cell + GAP) + GAP, n * (cell + GAP) + GAP, 4), np.uint8)
        for i, c in enumerate(chunk):
            cx, cy = i % n, i // n
            x, y = GAP + cx * (cell + GAP), GAP + cy * (cell + GAP)
            page[y:y + cell, x:x + cell] = c
            entries.append({"page": len(pages), "x": x, "y": y, "w": cell, "h": cell})
        pages.append(page)
    return pages, entries, [n, n]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mp4", required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--out", required=True, help="目标目录（…/video_fx/<名>）")
    ap.add_argument("--canvas", type=int, default=384)
    ap.add_argument("--fps", type=float, default=16.0)
    ap.add_argument("--scale", type=float, default=0.5, help="manifest 里的缩放入库比例")
    ap.add_argument("--char-height", type=int, default=232,
                    help="角色站立时在 canvas 里的高度（世界单位=像素）")
    ap.add_argument("--ref-height", type=float, default=904.0,
                    help="生成图里角色站立时的高度（px，1024² 基准帧约 904）")
    ap.add_argument("--feet-y", type=int, default=None,
                    help="脚底在 canvas 里的 y，默认 canvas-24")
    ap.add_argument("--loop", action="store_true", help="裁到最后与首帧最像的一帧")
    ap.add_argument("--recenter-x", action="store_true",
                    help="逐帧按包围盒居中（走路/奔跑原地循环用，抵消横向漂移）")
    ap.add_argument("--flip", action="store_true", help="水平镜像（统一朝向）")
    ap.add_argument("--min-edge", type=int, default=0,
                    help="剔除距画布边缘小于该值的帧（模型偶发把剑甩出画的第一帧）")
    ap.add_argument("--settle", action="store_true",
                    help="一次性动作：片尾裁到与首帧最像的那帧，便于接回待机")
    ap.add_argument("--loop-period", type=int, default=0,
                    help="循环类的预期周期（source 帧数，16fps）。0 = 不裁")
    ap.add_argument("--auto-height", action="store_true",
                    help="按本片实测的人物身高归一化缩放（跨动作尺寸一致）")
    ap.add_argument("--duration", type=float, default=0.0,
                    help="这条片子该播多少秒。>0 时按 frames/duration 反算 fps，"
                         "不再用固定 fps —— 动作快慢由动作本身决定")
    a = ap.parse_args()

    raw = load_frames(a.mp4)
    if a.flip:
        raw = [cv2.flip(f, 1) for f in raw]
    keyed = [key_background(f) for f in raw]

    if a.min_edge > 0:
        kept = []
        dropped = 0
        for f in keyed:
            bb = content_bbox(f)
            if bb is None:
                dropped += 1
                continue
            h0, w0 = f.shape[:2]
            margin = min(bb[0], bb[1], w0 - bb[2], h0 - bb[3])
            if margin < a.min_edge:
                dropped += 1
                continue
            kept.append(f)
        if kept:
            print(f"  出画帧剔除：{dropped} 帧（留白 < {a.min_edge}px）")
            keyed = kept

    if a.loop:
        # 「循环类」片段：先找稳定步态的起点与周期，只留一个完整周期。
        # 之前的做法是「砍掉与首帧最像的尾巴」，但首帧是站姿，循环起来每圈都会闪一下。
        start, length = best_loop_window(keyed, a.loop_period)
        if 0 < length < len(keyed):
            print(f"  步态窗口：从第 {start} 帧起取 {length} 帧（共 {len(keyed)}）")
            keyed = keyed[start:start + length]

    if a.settle:
        keep = settle_cut(keyed)
        if 0 < keep < len(keyed):
            print(f"  收势裁切：{len(keyed)} → {keep}")
            keyed = keyed[:keep]

    scale = (a.char_height / a.ref_height) if a.ref_height else 1.0

    # 跨动作尺寸一致：模型每条片子画的人物高矮不同，用固定 ref-height 会一大一小。
    # 取本片**所有帧包围盒高度的中位数**当基准 —— 中位数比首帧稳（首帧有时被模型拉宽），
    # 也比最大值稳（跳跃/挥剑的瞬间会短暂变高）。
    boxes = [b for b in (content_bbox(f) for f in keyed) if b]

    if a.auto_height and boxes:
        # 基准取**第 0 帧**的身高。
        # 实测：第 0 帧是模型对输入基准帧的复现（站姿、剑垂身侧），而第 1、2 帧模型会
        # 自己放大一点 —— 用前三帧中位数会把整体缩小约 27%，跨动作看就是一跳一变。
        med_h = float(boxes[0][3] - boxes[0][1])
        scale = a.char_height / max(1.0, med_h)
        print(f"  首帧身高 {med_h:.0f}px → 缩放 {scale:.3f}")

    canvas = (a.canvas, a.canvas)
    feet_y = a.feet_y if a.feet_y is not None else a.canvas - 24

    # 锚点：前几帧包围盒的「水平中心 + 底边」中位数。
    # 只取首帧会被那一帧的膨胀/收缩带偏；取全部帧又会让跳跃类整段被拉平。
    head = boxes[:min(3, len(boxes))]
    if head:
        anchor_x_src = sorted((b[0] + b[2]) / 2 for b in head)[len(head) // 2]
        anchor_y_src = sorted(b[3] for b in head)[len(head) // 2]
        first_bbox = (anchor_x_src, anchor_y_src)
    else:
        first_bbox = None
    cells = []
    for i, f in enumerate(keyed):
        anchor_x = canvas[0] // 2
        if a.recenter_x:
            bb = content_bbox(f)
            if bb:
                # 先按当前帧包围盒算临时落点，再把整帧平移回中心
                cell = compose(f, scale, canvas, (anchor_x, feet_y),
                               pivot_bbox=((bb[0] + bb[2]) / 2, bb[3]))
                ox = (bb[0] + bb[2]) / 2
                shift = int(round((f.shape[1] / 2 - ox) * scale))
                cell = np.roll(cell, shift, axis=1)
            else:
                cell = np.zeros((canvas[1], canvas[0], 4), np.uint8)
        else:
            pivot = first_bbox if first_bbox else None
            cell = compose(f, scale, canvas, (anchor_x, feet_y), pivot_bbox=pivot)
        cells.append(cell)

    pages, entries, grid = pack_pages(cells, a.canvas)

    # 速度：给了 duration 就反算 fps。16fps 是生成端的原生帧率，不是"这条动作该多快"——
    # 行走 77 帧按 16fps 播是 4.8 秒一个循环，而正常步频约 1 秒。
    fps = a.fps

    if a.duration > 0.0 and cells:
        fps = len(cells) / a.duration
        print(f"  时长 {a.duration:.2f}s → fps {fps:.1f}")

    os.makedirs(a.out, exist_ok=True)
    page_names = []
    for i, p in enumerate(pages):
        fn = f"page_{i:02d}.png"
        Image.fromarray(cv2.cvtColor(p, cv2.COLOR_BGRA2RGBA)).save(
            os.path.join(a.out, fn), optimize=True)
        page_names.append(fn)

    manifest = {
        "name": a.name,
        "canvas": [a.canvas, a.canvas],
        "scale": a.scale,
        "sourceCanvas": [int(round(a.canvas / a.scale)), int(round(a.canvas / a.scale))],
        "fps": fps,
        "frames": len(cells),
        "loop": "trim",
        "anchor": "bottom-center",
        "source": os.path.basename(a.mp4),
        "layout": "grid",
        "pageSize": list(pages[0].shape[1::-1]) if pages else [0, 0],
        "pages": page_names,
        "entries": entries,
        "grid": grid,
    }
    with open(os.path.join(a.out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    print(f"  {a.name}: {len(cells)} 帧 / {len(pages)} 页 / 格子 {a.canvas}px / "
          f"缩放 {scale:.3f} → {a.out}")


if __name__ == "__main__":
    sys.exit(main() or 0)
