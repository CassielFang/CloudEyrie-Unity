#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
程序化生成青禾的 Spine 骨骼动画（走路 / 跳跃 / 下落），写进 `青禾1.json`。

## 为什么可以程序生成

青禾的骨架绑得很完整：108 根骨骼，**四肢各有一组 IK 约束**
（`leg-l1`[k3,k4]、`leg-r1`[k6,k7]、`arm-l1`[xk4,xk5]、`arm-r`[xk7,xk8]），
而 IK 的目标骨骼（`leg-l` / `leg-r1` / `arm-l1` / `arm-r`）是 root 下的独立骨骼，**可直接驱动**。

所以步态不需要手算骨骼旋转 —— **只要给 IK 目标一条足/手轨迹，腿和手臂自己解算**。
现有的 `idle_breath` 就是这么做的（它在驱动 `arm-r` 与 `leg-r1`）。
好处是 IK 天然保证脚不打滑、不穿地。

## 为什么用密集线性关键帧

Spine 3.8 的关键帧曲线用 bezier 控制点编码（`curve`/`c2`/`c3`/`c4`），手写容易错。
本工具改为**在一个周期内密集采样**（默认每周期 16 帧）并全部用 `linear` ——
正弦/摆线被采样得足够密时，线性插值的误差看不出来，而且完全避开了曲线编码的坑。

## 幂等与"不动手绘动画"

- 本工具只会覆盖**它自己生成过的**动画（名单记在同目录的 `青禾1.gen-anims.json`）。
- `idle_breath` 与空占位 `animation` **按原字节保留**，连排版都不碰。
- 写法是**局部替换** `animations` 的值，文件其余部分逐字节不变。

## ⚠️ 什么时候要重跑

美术从 Spine 编辑器**重新导出** `青禾1.json` 之后 —— 生成的动作会全部丢失
（重导是整份覆盖）。和 `Assets/Spine/UNITY6.6-PATCH-NOTES.md` 是同一类问题。

## 用法

    python tools/gen_spine_anims.py --list          # 列出会生成哪些动画与参数
    python tools/gen_spine_anims.py                 # 生成/更新全部
    python tools/gen_spine_anims.py --only run      # 只生成某一条
    python tools/gen_spine_anims.py --stash         # 移除本工具生成的全部动画
"""

import argparse
import io
import json
import math
import sys
from pathlib import Path

SKELETON = "Assets/Art/Spine/Qinghe/青禾1.json"
MANIFEST = "Assets/Art/Spine/Qinghe/青禾1.gen-anims.json"

# ============================================================
# ⚠️ 骨骼缩放：所有参数都是「**缩放后**的单位」
# ============================================================
#
# 青禾的 `Qinghe1_SkeletonData.asset` 里 **scale = 0.2025**，
# 而 spine-unity 会把**动画里的位移偏移也乘上这个 scale**。
#
# 踩过的坑：一开始按 JSON 里的原始骨骼单位写参数（步幅 ±72），
# 结果实机只有 ±14.6 —— 因为 72 × 0.2025 ≈ 14.6。
# 而且**看截图几乎看不出来**（角色只是"动得有点小"），
# 是靠量 `leg-l`/`k5` 的世界坐标才算出来的。
#
# 所以本文件里的参数统一用**缩放后的单位**（≈ 世界单位），
# 由 `to_raw()` 在写 JSON 前统一除以 SKELETON_SCALE。
# 直观的参照：**腿长约 115**（躯干 zx 在 y≈115.8、脚在 y≈0）。
SKELETON_SCALE = 0.2025

# ============================================================
# 可调参数 —— 手感都在这儿，改完重跑即可
# 单位为「缩放后单位」，参照腿长 115、身高约 232。
# ============================================================
RUN = {
    "duration": 0.60,      # 一个完整步态周期（两步）的秒数 → 约 3.3 步/秒
    "samples": 16,         # 每周期采样帧数（密集线性）
    "stride_x": 32.0,      # 脚的前后摆幅（±）≈ 腿长的 28%，慢跑步幅
    "lift_y": 26.0,        # 摆动相的抬脚高度
    "stance_ratio": 0.5,   # 支撑相占比（跑步可降到 0.4 制造腾空）
    "bob_y": 9.0,          # 躯干上下起伏（每周期两次）
    "arm_swing_x": 26.0,   # 手臂前后摆幅
    "arm_lift_y": 8.0,     # 手臂上下微动
    "head_bob": 0.35,      # 头部跟随躯干起伏的比例（<1 = 滞后缓冲）
}

JUMP = {
    "duration": 0.28,      # 起跳（上升段）的时长
    "samples": 8,
    "crouch_y": -22.0,     # 起跳前下蹲（躯干）
    "rise_y": 18.0,        # 离地后躯干上提
    "leg_tuck": 40.0,      # 收腿（双脚上提）
    "arm_up_y": 32.0,      # 手臂上扬
}

FALL = {
    "duration": 0.45,
    "samples": 10,
    "leg_spread": 24.0,    # 双腿前后分开
    "leg_drop": -18.0,     # 腿略下垂
    "arm_up_y": 42.0,      # 双臂上举（下坠姿态）
    "bob_y": 5.0,          # 轻微飘动
}


# ============================================================
# 关键帧构造
# ============================================================

def kf(time, **values):
    """
    造一个关键帧。值为 0 的字段**不写**（Spine 的约定：缺省 = 0 = 相对 setup 无偏移），
    省字节能明显缩小文件。最后一个关键帧由调用方决定要不要留 curve。
    """
    d = {}
    if abs(time) > 1e-9:
        d["time"] = round(time, 4)
    for k in sorted(values):
        v = values[k]
        if abs(v) > 1e-9:
            d[k] = round(v, 2)
    return d


def timeline(keys):
    """
    给一串关键帧补上显式 `curve: linear`。

    **不要省这个字段**：Spine 3.8 在没有 curve 时的默认行为不够直观
    （最后一个关键帧确实不该有 curve，中间的必须显式给），写清楚最稳。
    本工具是密集采样，线性插值已经足够平滑。
    """
    out = []
    for i, k in enumerate(keys):
        item = dict(k)
        if i < len(keys) - 1:
            item["curve"] = "linear"
        out.append(item)
    return out


def foot_cycle(phase, stride, lift, stance_ratio):
    """
    单只脚在一个周期内的 (x, y) 偏移。

    - **支撑相**（phase < stance_ratio）：脚踩在地上，从前方匀速拖到后方（y 恒为 0）
    - **摆动相**：脚抬起、从后方划回前方（y 走一个正弦弧）

    角色本身不前移（原地循环），所以支撑相就是"脚相对身体往后走"。
    """
    if phase < stance_ratio:
        u = phase / stance_ratio
        return stride * (1.0 - 2.0 * u), 0.0

    u = (phase - stance_ratio) / (1.0 - stance_ratio)
    return stride * (-1.0 + 2.0 * u), lift * math.sin(math.pi * u)


def sample_run(p):
    """走/跑循环。返回 {骨骼名: [(t, {字段}), ...]}。"""
    n = p["samples"]
    dur = p["duration"]
    tracks = {}

    def add(bone, field, keys):
        tracks.setdefault(bone, {}).setdefault(field, []).extend(keys)

    # 采样 0..n（含两端），保证首尾同姿态 → 循环无缝
    for i in range(n + 1):
        phase = i / n
        t = phase * dur

        # ---- 腿：左右相差半个周期 ----
        lx, ly = foot_cycle(phase, p["stride_x"], p["lift_y"], p["stance_ratio"])
        rx, ry = foot_cycle((phase + 0.5) % 1.0, p["stride_x"], p["lift_y"], p["stance_ratio"])
        add("leg-l", "translate", [kf(t, x=lx, y=ly)])
        add("leg-r1", "translate", [kf(t, x=rx, y=ry)])

        # ---- 手臂：与同侧腿反相（左臂跟右腿），这是自然摆臂的关键 ----
        ax = -rx / p["stride_x"] * p["arm_swing_x"]
        ay = abs(math.sin(math.pi * 2 * phase)) * p["arm_lift_y"]
        add("arm-l1", "translate", [kf(t, x=ax, y=ay)])

        bx = -lx / p["stride_x"] * p["arm_swing_x"]
        by = abs(math.cos(math.pi * 2 * phase)) * p["arm_lift_y"]
        add("arm-r", "translate", [kf(t, x=bx, y=by)])

        # ---- 躯干：每周期起伏两次（每步一次） ----
        bob = -p["bob_y"] * math.cos(math.pi * 4 * phase)
        add("zx", "translate", [kf(t, y=bob)])

        # ---- 头：跟着躯干动，但幅度打折（滞后缓冲，否则像僵尸） ----
        add("bz", "translate", [kf(t, y=-bob * p["head_bob"])])

    return tracks


def sample_jump(p):
    """起跳：下蹲蓄力 → 蹬伸 → 离地收腿。非循环。"""
    n = p["samples"]
    dur = p["duration"]
    tracks = {}

    for i in range(n + 1):
        u = i / n
        t = u * dur

        # 前 30% 下蹲，之后快速起跳
        if u < 0.3:
            k = u / 0.3
            torso = p["crouch_y"] * k
            legs = -p["leg_tuck"] * 0.35 * k
            arms = -k * 20.0
        else:
            k = (u - 0.3) / 0.7
            ease = 1.0 - (1.0 - k) ** 2          # ease-out
            torso = p["crouch_y"] * (1.0 - ease) + p["rise_y"] * ease
            legs = -p["leg_tuck"] * ease
            arms = -20.0 + p["arm_up_y"] * ease

        tracks.setdefault("zx", {}).setdefault("translate", []).append(kf(t, y=torso))
        tracks.setdefault("bz", {}).setdefault("translate", []).append(kf(t, y=-torso * 0.3))
        tracks.setdefault("leg-l", {}).setdefault("translate", []).append(kf(t, y=legs))
        tracks.setdefault("leg-r1", {}).setdefault("translate", []).append(kf(t, y=legs * 0.85))
        tracks.setdefault("arm-l1", {}).setdefault("translate", []).append(kf(t, y=arms))
        tracks.setdefault("arm-r", {}).setdefault("translate", []).append(kf(t, y=arms * 0.9))

    return tracks


def sample_fall(p):
    """下落：双腿前后张开、双臂上举，带轻微飘动。可循环。"""
    n = p["samples"]
    dur = p["duration"]
    tracks = {}

    for i in range(n + 1):
        phase = i / n
        t = phase * dur
        bob = math.sin(math.pi * 2 * phase) * p["bob_y"]

        tracks.setdefault("zx", {}).setdefault("translate", []).append(kf(t, y=bob))
        tracks.setdefault("bz", {}).setdefault("translate", []).append(kf(t, y=-bob * 0.4))
        tracks.setdefault("leg-l", {}).setdefault("translate", []).append(
            kf(t, x=p["leg_spread"], y=p["leg_drop"]))
        tracks.setdefault("leg-r1", {}).setdefault("translate", []).append(
            kf(t, x=-p["leg_spread"], y=p["leg_drop"] * 0.7))
        tracks.setdefault("arm-l1", {}).setdefault("translate", []).append(
            kf(t, y=p["arm_up_y"] + bob * 0.5))
        tracks.setdefault("arm-r", {}).setdefault("translate", []).append(
            kf(t, y=p["arm_up_y"] * 0.92 + bob * 0.5))

    return tracks


GENERATORS = {
    "run": (RUN, sample_run),
    "jump": (JUMP, sample_jump),
    "fall": (FALL, sample_fall),
}


# ============================================================
# JSON 局部替换（保住 idle_breath 的原字节）
# ============================================================

def read_json_value(text, start):
    """
    从 `start`（指向 `{`/`[`/`"`/数字）读出一整个 JSON 值，返回 (值文本, 结束位置)。
    自己扫而不是用 json 库 —— 目的是**拿到原始子串**，才能原样保留。
    """
    # 字符串要单独处理：闭合引号就是结尾，不能继续往后找逗号
    # （否则会把 `:` 和后面的值一起吞进来 —— 读键名时必踩）
    if text[start] == '"':
        i = start + 1
        esc = False

        while i < len(text):
            c = text[i]
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                return text[start:i + 1], i + 1
            i += 1

        raise ValueError("JSON 字符串没有闭合")

    depth = 0
    in_str = False
    esc = False
    i = start

    while i < len(text):
        c = text[i]

        if in_str:
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                in_str = False
            i += 1
            continue

        if c == '"':
            in_str = True
        elif c in "{[":
            depth += 1
        elif c in "}]":
            depth -= 1
            if depth == 0:
                return text[start:i + 1], i + 1
        elif c == "," and depth == 0:
            return text[start:i], i
        i += 1

    raise ValueError("JSON 值没有正常结束（括号不配对）")


def split_entries(obj_text):
    """
    把 `{"a":{…},"b":{…}}` 的内层切成 [("a", "{…}"), ("b", "{…}")]，
    **保持原顺序与原字节**。
    """
    inner = obj_text[1:-1]
    entries = []
    i = 0

    while i < len(inner):
        while i < len(inner) and inner[i] in " \t\r\n,":
            i += 1
        if i >= len(inner):
            break

        if inner[i] != '"':
            raise ValueError(f"期望键名，实际是 {inner[i]!r}（位置 {i}）")

        key_raw, j = read_json_value(inner, i)
        key = json.loads(key_raw)

        while j < len(inner) and inner[j] in " \t\r\n:":
            j += 1

        val_raw, k = read_json_value(inner, j)
        entries.append((key, val_raw))
        i = k

    return entries


def find_animations(text):
    """定位顶层 `"animations"` 的值，返回 (值起始下标, 结束下标, 值文本)。"""
    anchor = text.find('"animations"')
    if anchor < 0:
        raise ValueError("青禾1.json 里找不到 animations 段")

    colon = text.index(":", anchor)
    start = colon + 1
    while text[start] in " \t\r\n":
        start += 1

    val_raw, end = read_json_value(text, start)
    return start, end, val_raw


def to_raw(tracks):
    """
    把「缩放后单位」的参数换算回 JSON 里的**原始骨骼单位**。

    spine-unity 加载时会把骨骼位置（含动画偏移）乘上 SkeletonDataAsset.scale，
    所以想让实机看到 X 个单位的位移，JSON 里就得写 X / scale。
    """
    k = 1.0 / SKELETON_SCALE
    out = {}
    for bone, fields in tracks.items():
        out[bone] = {}
        for field, keys in fields.items():
            out[bone][field] = [
                {kk: (round(vv * k, 2) if kk in ("x", "y", "angle") else vv)
                 for kk, vv in key.items()}
                for key in keys
            ]
    return out


def build_animation(tracks):
    """把 {骨骼: {字段: [关键帧]}} 变成 Spine 的动画对象。"""
    tracks = to_raw(tracks)
    bones = {}
    for bone in sorted(tracks):
        fields = {}
        for field in sorted(tracks[bone]):
            fields[field] = timeline(tracks[bone][field])
        bones[bone] = fields
    return {"bones": bones}


def dump_compact(obj):
    """与 Spine 自己导出的风格一致：紧凑、不转义中文。"""
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def load_manifest(path):
    if not path.exists():
        return []
    try:
        return json.loads(io.open(path, encoding="utf-8").read()).get("generated", [])
    except Exception:
        return []


def save_manifest(path, names):
    io.open(path, "w", encoding="utf-8").write(
        json.dumps({"generated": sorted(names)}, ensure_ascii=False, indent=2) + "\n")


def main() -> int:
    ap = argparse.ArgumentParser(description="程序化生成青禾的 Spine 动画")
    ap.add_argument("--only", action="append", metavar="NAME",
                    help="只处理指定动画（可重复）")
    ap.add_argument("--stash", action="store_true",
                    help="移除本工具此前生成的全部动画，恢复到手绘状态")
    ap.add_argument("--list", action="store_true", help="列出会生成什么，不写文件")
    args = ap.parse_args()

    skeleton_path = Path(SKELETON)
    manifest_path = Path(MANIFEST)

    if not skeleton_path.exists():
        print(f"[错误] 找不到 {skeleton_path}", file=sys.stderr)
        return 2

    if args.list:
        for name, (params, _) in GENERATORS.items():
            print(f"  {name:<8} 时长 {params['duration']}s  采样 {params['samples']} 帧")
        print(f"\n  已生成过：{load_manifest(manifest_path) or '（无）'}")
        return 0

    raw = io.open(skeleton_path, encoding="utf-8").read()

    try:
        start, end, anim_text = find_animations(raw)
    except ValueError as e:
        print(f"[错误] {e}", file=sys.stderr)
        return 2

    entries = split_entries(anim_text)
    previously = set(load_manifest(manifest_path))

    # 先摘掉此前生成的（剩下的就是手绘的：idle_breath / animation）
    kept = [(k, v) for (k, v) in entries if k not in previously]

    if args.stash:
        target = set()
    else:
        target = set(GENERATORS) if not args.only else set(args.only)

    unknown = target - set(GENERATORS)
    if unknown:
        print(f"[错误] 不认识这些动画：{sorted(unknown)}", file=sys.stderr)
        return 2

    new_entries = []
    for name in sorted(target):
        params, fn = GENERATORS[name]
        anim = build_animation(fn(params))
        new_entries.append((name, dump_compact(anim)))

    all_entries = kept + new_entries

    rebuilt = "{" + ",".join(f'"{k}":{v}' for k, v in all_entries) + "}"
    result = raw[:start] + rebuilt + raw[end:]

    # 自检：改完之后整份文件必须是合法 JSON
    try:
        parsed = json.loads(result)
    except json.JSONDecodeError as e:
        print(f"[错误] 生成结果不是合法 JSON，未写入：{e}", file=sys.stderr)
        return 1

    # 自检：手绘动画必须原样还在，且字节未变
    for k, v in kept:
        if k not in parsed.get("animations", {}) or \
                find_animations(result)[2].find(f'"{k}":{v}') < 0:
            print(f"[错误] 手绘动画 {k} 被动过，未写入。", file=sys.stderr)
            return 1

    io.open(skeleton_path, "w", encoding="utf-8", newline="").write(result)
    save_manifest(manifest_path, target)

    print(f"{skeleton_path}")
    print(f"  保留（手绘，逐字节未动）：{[k for k, _ in kept]}")
    if target:
        print(f"  生成：{sorted(target)}")
    else:
        print("  已移除此前生成的全部动画（--stash）")
    print(f"  文件 {len(raw)} → {len(result)} 字节")
    return 0


if __name__ == "__main__":
    sys.exit(main())
