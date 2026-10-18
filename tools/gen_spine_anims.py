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
import re
import sys
from pathlib import Path

# Windows 控制台默认 GBK：验算里的警告符号会直接抛 UnicodeEncodeError
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

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
# ⚠️ 2026-10-17 第二轮：**目标偏移全部收小，并加了「基础抬升」**
#
# 上一版的问题（实测）：四肢像折断/卡直。原因不是 IK 本身，而是**目标点超出了骨链长度**：
#   - 左腿链 555.8 原始单位，setup 目标距 549.8（98.9%）—— 再往前后摆 ±158 就必然拉直；
#   - 右腿链 461.3，setup 目标距 470.6（**102%**）—— 一开始就够不着，永远是一根直棍；
#   - 右臂链 292.1，setup 目标距 296.6（101.5%）—— 同样。
# 于是腿/手在走到某个位置时会「啪」地锁死，看起来像折断。
#
# 现在的做法：
#   1. 每个肢体给一个**基础抬升**（base），把目标拉近一点，留出弯曲余量；
#   2. 摆幅按各自链长分别给（左右腿长度本来就不同：555.8 vs 461.3）；
#   3. 生成完用 setup 骨架**逐帧验算目标距离 / 链长**，超过 99.5% 直接打印警告。
#      （见 validate() —— 这类问题在截图里几乎看不出来，只有数字不会说谎。）
RUN = {
    "duration": 0.56,      # 一个完整步态周期（两步）
    "samples": 20,
    "stance_ratio": 0.58,

    # 脚目标：x 前后摆幅 / y 摆动相抬升 / base 基础抬升（留弯曲余量）
    # 右腿骨链已用 k6.scale=1.205 补到和左腿一样长，所以两边用同一组参数。
    "leg_l": {"stride": 12.0, "lift": 16.0, "base": 10.0},
    "leg_r": {"stride": 12.0, "lift": 16.0, "base": 10.0},

    # 手目标：前后摆幅 / 上下微动 / 基础抬升 / 基础横向内收
    # 右手持剑 → 摆幅小、手往身体内侧收（不内收会为了留 IK 余量把手上抬，
    # 结果整把剑横着举出去）；左手自由 → 摆幅大。
    "arm_l_swing": 10.0,
    "arm_r_swing": 3.0,
    "arm_lift": 4.0,
    "arm_l": {"base": -2.0, "base_x": 0.0},
    # 持剑手只轻微内收。收太多（-8）时肘部折成锐角，看起来像折断。
    "arm_r": {"base": 8.0, "base_x": -2.0},

    "lean_x": 5.0,          # 上半身前倾（世界单位）：长袍角色靠这个才有"在跑"的感觉
    "bob_y": 6.0,           # 躯干上下起伏（每周期两次）
    "head_bob": 0.4,        # 头部跟随躯干起伏的比例（<1 = 滞后缓冲）

    "cloth_amp": 6.0,       # 头发/衣摆的二次摆动幅度（度；末梢会按链深放大）
    "cloth_cycles": 2,      # 二次摆动在一个步态周期里摆几次
}

JUMP = {
    "duration": 0.30,
    "samples": 10,
    "crouch_y": -24.0,     # 起跳前下蹲（躯干）
    "rise_y": 16.0,        # 离地后躯干上提
    "leg_base_l": 4.0,
    "leg_base_r": 6.0,
    "crouch_leg_up": 4.0,  # 下蹲时脚目标略收（膝微弯）
    "tuck_up": 34.0,       # 离地收腿：目标往髋部收，膝盖自然弯
    "arm_base_l": 4.0,
    "arm_base_r": 6.0,
    "arm_back": 12.0,      # 下蹲时手往后
    "arm_up": 48.0,        # 起跳时手往上（目标靠近肩 → 肘弯；长袖要抬够才看得出来）
}

FALL = {
    "duration": 0.50,
    "samples": 10,
    "leg_base_l": 4.0,
    "leg_base_r": 6.0,
    "leg_up": 18.0,        # 腿略收（弯曲）
    "leg_spread_l": 10.0,
    "leg_spread_r": 8.0,
    "arm_base_l": 4.0,
    "arm_base_r": 6.0,
    "arm_up": 40.0,        # 双手上举
    "arm_spread": 12.0,
    "bob_y": 4.0,
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
    """走/跑循环。返回 {骨骼名: {字段: [关键帧]}}。

    腿和手臂都驱动 IK 目标；左右参数分别给（两条腿的骨链长度不同）。
    手臂与**对侧腿**同相（左臂跟右腿），这是自然摆臂的关键。
    """
    n = p["samples"]
    dur = p["duration"]
    tracks = {}

    def add(bone, field, keys):
        tracks.setdefault(bone, {}).setdefault(field, []).extend(keys)

    leg_l, leg_r = p["leg_l"], p["leg_r"]

    for i in range(n + 1):
        phase = i / n
        t = phase * dur

        # ---- 腿：左右相差半个周期 ----
        lx, ly = foot_cycle(phase, leg_l["stride"], leg_l["lift"], p["stance_ratio"])
        rx, ry = foot_cycle((phase + 0.5) % 1.0, leg_r["stride"], leg_r["lift"], p["stance_ratio"])
        add("leg-l", "translate", [kf(t, x=lx, y=leg_l["base"] + ly)])
        add("leg-r1", "translate", [kf(t, x=rx, y=leg_r["base"] + ry)])

        # 躯干：每周期起伏两次（每步一次），并略微前倾
        bob = -p["bob_y"] * math.cos(math.pi * 4 * phase)

        # ---- 手臂：与对侧腿同相；持剑的右手摆幅小得多 ----
        # ⚠️ 手目标要**跟着躯干一起**做前倾 + 起伏。否则躯干一动，
        # 手到肩的距离就变，IK 会把持剑的手越推越远，剑就横着甩出去了。
        ax = p["lean_x"] + p["arm_l"]["base_x"] - rx / max(1e-3, leg_r["stride"]) * p["arm_l_swing"]
        ay = bob + p["arm_l"]["base"] + abs(math.sin(math.pi * 2 * phase)) * p["arm_lift"]
        add("arm-l1", "translate", [kf(t, x=ax, y=ay)])

        bx = p["lean_x"] + p["arm_r"]["base_x"] - lx / max(1e-3, leg_l["stride"]) * p["arm_r_swing"]
        by = bob + p["arm_r"]["base"] + abs(math.cos(math.pi * 2 * phase)) * p["arm_lift"]
        add("arm-r", "translate", [kf(t, x=bx, y=by)])

        add("zx", "translate", [kf(t, x=p["lean_x"], y=bob)])

        # ---- 头：跟着躯干动，但幅度打折（滞后缓冲，否则像僵尸） ----
        add("bz", "translate", [kf(t, y=-bob * p["head_bob"])])

    return tracks


def sample_jump(p):
    """起跳：下蹲蓄力 → 蹬伸 → 离地收腿。非循环。

    收腿是**把 IK 目标往髋部收**（y 为正），膝盖因此自然弯曲；
    旧版把目标往下压（负值）会直接超出骨链长度，腿锁成一根直棍。
    """
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
            leg_up = p["crouch_leg_up"] * k
            arm_x = -p["arm_back"] * k
            arm_y = 0.0
        else:
            k = (u - 0.3) / 0.7
            ease = 1.0 - (1.0 - k) ** 2          # ease-out
            torso = p["crouch_y"] * (1.0 - ease) + p["rise_y"] * ease
            leg_up = p["crouch_leg_up"] * (1.0 - ease) + p["tuck_up"] * ease
            arm_x = -p["arm_back"] * (1.0 - ease)
            arm_y = p["arm_up"] * ease

        add_track(tracks, "zx", "translate", kf(t, y=torso))
        add_track(tracks, "bz", "translate", kf(t, y=-torso * 0.3))
        add_track(tracks, "leg-l", "translate", kf(t, y=p["leg_base_l"] + leg_up))
        add_track(tracks, "leg-r1", "translate", kf(t, y=p["leg_base_r"] + leg_up * 0.9))
        add_track(tracks, "arm-l1", "translate",
                  kf(t, x=arm_x, y=p["arm_base_l"] + arm_y))
        add_track(tracks, "arm-r", "translate",
                  kf(t, x=arm_x * 0.9, y=p["arm_base_r"] + arm_y * 0.9))

    return tracks


def sample_fall(p):
    """下落：双腿微张、略收，双臂上举，带轻微飘动。可循环。"""
    n = p["samples"]
    dur = p["duration"]
    tracks = {}

    for i in range(n + 1):
        phase = i / n
        t = phase * dur
        bob = math.sin(math.pi * 2 * phase) * p["bob_y"]

        add_track(tracks, "zx", "translate", kf(t, y=bob))
        add_track(tracks, "bz", "translate", kf(t, y=-bob * 0.4))
        add_track(tracks, "leg-l", "translate",
                  kf(t, x=p["leg_spread_l"], y=p["leg_base_l"] + p["leg_up"] + bob * 0.2))
        add_track(tracks, "leg-r1", "translate",
                  kf(t, x=-p["leg_spread_r"], y=p["leg_base_r"] + p["leg_up"] * 0.8 + bob * 0.2))
        add_track(tracks, "arm-l1", "translate",
                  kf(t, x=p["arm_spread"], y=p["arm_base_l"] + p["arm_up"] + bob * 0.4))
        add_track(tracks, "arm-r", "translate",
                  kf(t, x=-p["arm_spread"], y=p["arm_base_r"] + p["arm_up"] * 0.9 + bob * 0.4))

    return tracks


def add_track(tracks, bone, field, key):
    tracks.setdefault(bone, {}).setdefault(field, []).append(key)


def add_secondary_sway(tracks, skeleton, cycles, amp):
    """
    给头发/衣摆的骨骼加一点**二次摆动**。

    这些是 idle_breath 里已经在转的 `t*` / `yb*` 链（角度很小，约 4°）。
    跑动时如果它们完全不动，角色看起来像一块硬纸板 —— 腿又被长袍挡住，
    画面里几乎看不到"在动"。
    """
    parent = {b["name"]: b.get("parent") for b in skeleton["bones"]}
    names = [b["name"] for b in skeleton["bones"] if re.match(r"^(yb|t)\d+$", b["name"])]
    times = [k.get("time", 0.0) for k in tracks.get("zx", {}).get("translate", [])]

    if not times:
        return

    dur = times[-1]

    if dur <= 1e-6:
        return

    def chain_depth(n):
        d = 0
        while parent.get(n):
            d += 1
            n = parent[n]
        return d

    max_depth = max(chain_depth(n) for n in names) if names else 1

    for idx, name in enumerate(names):
        # 越靠末梢（链越深）摆得越大：发尾/袍角才是"飘"的，发根跟着头走
        depth_k = 0.45 + 0.85 * chain_depth(name) / max(1, max_depth)
        off = idx * 0.37
        keys = []

        for t in times:
            ang = amp * depth_k * math.sin(math.pi * 2 * cycles * (t / dur) + off)
            key = {"time": round(t, 4)}

            if abs(ang) > 1e-6:
                key["angle"] = round(ang, 2)

            keys.append(key)

        tracks.setdefault(name, {})["rotate"] = keys


GENERATORS = {
    "run": (RUN, sample_run),
    "jump": (JUMP, sample_jump),
    "fall": (FALL, sample_fall),
}


# ============================================================
# IK 目标余量验算
# ============================================================

# IK 约束 → 驱动的骨链 / 目标骨骼（与 青禾1.json 的 "ik" 段一致）
LIMBS = {
    "leg-l1": ("leg-l", ["k3", "k4"]),
    "leg-r1": ("leg-r1", ["k6", "k7"]),
    "arm-l1": ("arm-l1", ["xk4", "xk5"]),
    "arm-r": ("arm-r", ["xk7", "xk8"]),
}


def _bone_world(bones, name):
    """按 Spine 的父级旋转累积，算一根骨骼的世界位置（原始单位）。"""
    chain = []
    n = name
    while n:
        chain.append(bones[n])
        n = bones[n].get("parent")
    chain.reverse()

    x = y = rot = 0.0
    sx = sy = 1.0

    for b in chain:
        # 父级累积缩放会缩放子骨骼的局部偏移
        lx, ly = b.get("x", 0.0) * sx, b.get("y", 0.0) * sy
        r = math.radians(rot)
        x += lx * math.cos(r) - ly * math.sin(r)
        y += lx * math.sin(r) + ly * math.cos(r)
        rot += b.get("rotation", 0.0)
        sx *= b.get("scaleX", 1.0)
        sy *= b.get("scaleY", 1.0)

    return x, y, rot, sx, sy


def _has_ancestor(bones, name, ancestor):
    n = bones[name].get("parent")
    while n:
        if n == ancestor:
            return True
        n = bones[n].get("parent")
    return False


def validate(name, tracks, skeleton):
    """
    逐帧验算「IK 目标到骨链根的距离 / 骨链长度」。

    ≥100% = 骨链够不着目标 → IK 会锁成一条直线，视觉上就是**折断**。
    这里是硬性数字检查：截图里几乎看不出来，只有这个数能提前发现。
    """
    bones = {b["name"]: b for b in skeleton["bones"]}
    warnings = []
    parts = []

    for limb, (target, chain) in LIMBS.items():
        # 链长 = 两段骨骼**实际的**世界距离 + 末段长度 × 世界缩放。
        # 不能简单把 data.length 相加 —— 骨骼可以带 scale（青禾的右腿就是靠 k6 放大
        # 补到和左腿一样长），缩放不会写回 data.length。
        a = _bone_world(bones, chain[0])
        c = _bone_world(bones, chain[1])
        l1 = math.hypot(c[0] - a[0], c[1] - a[1]) * SKELETON_SCALE
        l2 = bones[chain[1]].get("length", 0.0) * c[3] * SKELETON_SCALE
        chain_len = l1 + l2

        root_x = a[0] * SKELETON_SCALE
        root_y = a[1] * SKELETON_SCALE

        # 目标骨骼是 root 的子骨骼；它的位置就是 setup 值
        tb = bones[target]
        target_x = tb.get("x", 0.0) * SKELETON_SCALE
        target_y = tb.get("y", 0.0) * SKELETON_SCALE

        # 目标骨骼的平移轨道（缺省 = 0）
        trans = tracks.get(target, {}).get("translate", [])
        zx = tracks.get("zx", {}).get("translate", [])
        zx_applies = _has_ancestor(bones, chain[0], "zx")

        limit = 0.0

        for i in range(max(len(trans), len(zx))):
            dx = trans[i].get("x", 0.0) if i < len(trans) else 0.0
            dy = trans[i].get("y", 0.0) if i < len(trans) else 0.0

            hx, hy = root_x, root_y
            if zx_applies and i < len(zx):
                hx += zx[i].get("x", 0.0)
                hy += zx[i].get("y", 0.0)

            d = math.hypot((target_x + dx) - hx, (target_y + dy) - hy)
            if chain_len > 1e-6:
                limit = max(limit, d / chain_len)

        parts.append(f"{limb} {limit * 100:5.1f}%")

        if limit >= 0.995:
            warnings.append(f"{name}: {limb} 目标距/链长 = {limit * 100:.1f}% —— 会锁直/折断")

    print(f"  IK 余量 {name}: " + "  ".join(parts))

    for w in warnings:
        print(f"  ⚠️ {w}")

    return not warnings


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
                # ⚠️ 只缩放**位移**。Spine 的 SkeletonDataAsset.scale 不影响旋转角度 ——
                # 把 angle 也乘 1/scale 会让头发/衣摆转出 5 倍，直接是灾难。
                {kk: (round(vv * k, 2) if kk in ("x", "y") else vv)
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

    skeleton = json.loads(raw)

    new_entries = []
    all_ok = True

    for name in sorted(target):
        params, fn = GENERATORS[name]
        tracks = fn(params)

        if name == "run":
            add_secondary_sway(tracks, skeleton, params["cloth_cycles"], params["cloth_amp"])
        else:
            # 起跳/下落时头发与长袍要甩得更明显
            add_secondary_sway(tracks, skeleton, 1, 8.0)

        all_ok &= validate(name, tracks, skeleton)
        anim = build_animation(tracks)
        new_entries.append((name, dump_compact(anim)))

    if not all_ok:
        print("\n⚠️ 有肢体的目标超出骨链长度 —— 实机上会看到『锁直 / 折断』。"
              "先调小对应 stride/lift，或加大 base。", file=sys.stderr)

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
