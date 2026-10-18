#!/usr/bin/env python3
"""逐条体检生成出来的动作片。

四类反馈对应四个可量化判据：

1. **跑出画面**：逐帧算主体包围盒，距画布边缘最小留白 < EDGE_MIN 判 OUT。
2. **动作不完整/太短**：相邻帧位移中位数，太小说明几乎没动。
3. **节奏不对**：同一个位移指标，作为「该快的地方够不够快」的客观参考。
4. **循环不闭环**：首帧与末尾 12 帧的最小差异。

背景板用**整段中位帧**：镜头固定、人物在动，逐像素取中位数基本就是背景本身。
之前用「单帧边框颜色」估背景，边缘的暗角/压缩噪点会残留几个不透明像素，
包围盒永远等于整帧，出画判据失效 —— 这是换法的原因。
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ANIM = Path.home() / "ai/ComfyUI/output/character_pipeline/base_animations"
EDGE_MIN = 12
DIFF_TOL = 18


def frames_of(mp4):
    cap = cv2.VideoCapture(str(mp4))
    out = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        out.append(f)
    cap.release()
    return out


def bbox_against(frame, bg):
    diff = np.abs(frame.astype(np.int16) - bg.astype(np.int16)).max(axis=2)
    mask = diff > DIFF_TOL
    cols = mask.sum(axis=0)
    rows = mask.sum(axis=1)
    xs = np.where(cols >= 3)[0]
    ys = np.where(rows >= 3)[0]
    if len(xs) == 0 or len(ys) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1


def main():
    names = sys.argv[1:]
    if not names:
        names = sorted(p.name for p in ANIM.iterdir()
                       if p.is_dir() and p.name.startswith("青禾"))

    print(f"{'clip':<16}{'frames':>7}{'edge':>7}{'px/frame':>10}{'loopdiff':>10}  flag")
    for name in names:
        d = ANIM / name
        mp4s = sorted(d.glob("*.mp4"))
        if not mp4s:
            continue
        fr = frames_of(mp4s[-1])
        if len(fr) < 2:
            continue

        h, w = fr[0].shape[:2]
        bg = np.median(np.stack(fr, axis=0), axis=0).astype(np.uint8)

        boxes = [b for b in (bbox_against(f, bg) for f in fr) if b]
        if not boxes:
            print(f"{name:<16}{len(fr):>7}      --  (no subject)")
            continue

        margins = [min(b[0], b[1], w - b[2], h - b[3]) for b in boxes]
        min_edge = min(margins)

        cx = np.array([(b[0] + b[2]) / 2 for b in boxes])
        cy = np.array([(b[1] + b[3]) / 2 for b in boxes])
        step = float(np.median(np.hypot(np.diff(cx), np.diff(cy)))) if len(cx) > 1 else 0.0

        f0 = fr[0].astype(np.int32)
        loop = min(float(np.abs(fr[j].astype(np.int32) - f0).mean())
                   for j in range(max(1, len(fr) - 12), len(fr)))

        flag = "OUT" if min_edge < EDGE_MIN else "ok"
        print(f"{name:<16}{len(fr):>7}{min_edge:>7}{step:>10.2f}{loop:>10.2f}  {flag}")


if __name__ == "__main__":
    main()
