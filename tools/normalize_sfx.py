#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""把 Resources/audio 下的音效响度归一（增益烘进 wav 采样 + 软限幅留余量）。

## 为什么必须烘进文件，而不是运行时乘一个增益

Unity 的 `AudioSource.volume` **硬夹在 [0,1]**（实测：设 7.47 读回 1.000），
`PlayOneShot(clip, volumeScale)` 的 volumeScale 同样是 0..1。
所以「把狐鸣放大约 7 倍」这件事在运行时**根本做不到** ——
写一个 >1 的增益只会被静默夹成 1，听感上等于没加，而且不报错。

上一轮就是这么踩的：游戏里那张增益表里所有 >1 的值全部无效，
只有 <1 的衰减真正生效 —— 表现是「响的被压了、轻的一点没抬」。
结论：**要抬电平只能抬源文件。**

## 量的是什么：最响的 300ms 窗，不是整段 RMS

整段 RMS 会把长尾的轻音算进来：狐鸣 3 秒里前段响、后半段拖得很轻，
一平均就把电平摊平了，算出来的增益偏小。
「能不能听见」取决于**它最响那一段够不够响** —— 所以按 0.3 秒滑窗取最大，
左右声道各算一遍取较大的。

## 基准、目标、上限

- 基准：`sfx_slash`（青禾实体剑击）—— 实测反馈里唯一「听着正好」的一条。
  **基准文件永不改动**：动它就把整套对齐关系挪走了。
- 目标：各条的最响窗对齐到 **基准 × --target-db**（默认 −2 dB）
- 上限：**软限幅**到 --ceiling（默认 0.7 FS）

### 为什么要软限幅，而不是简单封顶

第一版是「算好增益后把峰值夹在 0.98 FS」。三个问题，都是实测出来的：

1. **编解码会过冲**：音效在 `.meta` 里是 Vorbis 压缩，有损编解码在瞬态上会超过
   输入峰值 —— 格挡封在 0.98，**引擎里解码出来是 1.0000**。
   （这几条现已改成 PCM 无压缩，过冲消失。）
2. **多条同时响要叠**：剑击 0.944 FS + 格挡 1.0 FS 一叠就超满刻度**硬削** ——
   硬削就是用户听到的「电流声」。战斗里剑击几乎每刀都响，和格挡 / 踏云闪重叠是常态。
3. **直接整体调低会白干**：格挡 / 踏云闪的源峰值本就在 −4.4 / −1.6 dBFS，
   一留余量就没得抬了。

所以用**软限幅**：只压最顶上那几 dB，把峰值降到 ceiling 以内，而中间的响度
（= 最响窗）尽量不动 —— 「够响」和「有余量」才能同时成立。
代价是峰值附近有轻微谐波失真，对本来就已经压到满刻度的素材是可接受的。

### 迭代到不动点

限幅会略微拉低最响窗（它也在被压的那部分里），所以增益和限幅要互相迭代几次
才收敛。收敛之后本脚本**再跑一次是空操作**（增益≈1 且峰值已在 ceiling 以内就跳过），
所以可以放心重复执行。

## 用法

    python tools/normalize_sfx.py                    # 只报告，不动文件
    python tools/normalize_sfx.py --apply            # 写回 wav
    python tools/normalize_sfx.py --target-db -3     # 再轻一点

⚠️ 只处理 `sfx_*.wav`。**不碰 BGM**（美工正在重制，见 GameAudio 的说明）。
⚠️ `美术素材/` 下那批**不是** Resources 里这几份的母版（实测最响窗对不上，
   是不同版本），所以**别拿它当重建源**，就地迭代即可。
"""

import argparse
import math
import os
import struct
import sys
import wave

# 项目根（本文件在 tools/ 下）
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AUDIO_DIR = os.path.join(ROOT, "Assets", "Resources", "audio")

REFERENCE = "sfx_slash"
LOUD_WINDOW_SEC = 0.3
FULL_SCALE = 32767

# 目标电平：基准的最响窗再减这么多 dB。0 = 和剑击一样响。
# 2026-10-07：先按 0（完全对齐）做，实测反馈「有点调过了、有电流声」，改 −2。
TARGET_DB = -2.0

# 软限幅：超过 KNEE 的部分按 tanh 压进 [KNEE, CEILING]，渐近到 CEILING 不越界。
# CEILING 0.7 FS ≈ 留 3 dB 给「多条同时响」的叠加。
CEILING = 0.7
KNEE = 0.5

# 收敛迭代次数与容差（见文件头「迭代到不动点」）
ITERATIONS = 5
TOLERANCE = 0.01


def read_wav(path):
    """读 16bit wav → (interleaved samples, n_channels, sample_rate)。"""
    with wave.open(path, "rb") as w:
        ch = w.getnchannels()
        width = w.getsampwidth()
        rate = w.getframerate()
        raw = w.readframes(w.getnframes())

    if width != 2:
        raise ValueError("%s 是 %d 字节采样，本脚本只处理 16bit" % (path, width))

    return list(struct.unpack("<%dh" % (len(raw) // 2), raw)), ch, rate


def write_wav(path, samples, ch, rate):
    """按 16bit 写回。头是规范的。"""
    clamped = [max(-32768, min(32767, int(round(v)))) for v in samples]
    data = struct.pack("<%dh" % len(clamped), *clamped)

    with wave.open(path, "wb") as w:
        w.setnchannels(ch)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(data)


def rms(values):
    if not values:
        return 0.0
    return math.sqrt(sum(float(v) * v for v in values) / len(values))


def loudest_window(samples, ch, rate, window_sec=LOUD_WINDOW_SEC):
    """最响窗口的 RMS（逐声道取最大）。"""
    win = int(rate * window_sec)
    hop = max(1, win // 10)
    best = 0.0

    for c in range(ch):
        mono = samples[c::ch]

        if len(mono) <= win:
            best = max(best, rms(mono))
            continue

        for i in range(0, len(mono) - win, hop):
            v = rms(mono[i:i + win])

            if v > best:
                best = v

    return best


def peak_of(samples):
    return max(abs(v) for v in samples) if samples else 0


def soft_limit(samples, knee, ceiling, full_scale=FULL_SCALE):
    """软限幅：|x| <= knee 原样通过，之上按 tanh 平滑压进 [knee, ceiling]。

        y = knee + (ceiling - knee) * tanh((|x| - knee) / (ceiling - knee))

    tanh 渐近到 1，所以 **y 恒不超过 ceiling**（不会削顶）；
    在 x = knee 处斜率仍为 1，衔接平滑（不会有拐点杂音）。

    ⚠️ 是**逐样本无记忆**的波形整形，不是带时间常数的动态限幅器 ——
    没有起振/释放，也就没有「抽气」感；代价是峰值附近有谐波失真。
    所以调用方必须保证**只在需要时调一次**（见 converge 的收敛判断）。
    """
    k = knee * full_scale
    span = (ceiling - knee) * full_scale
    out = []

    for v in samples:
        a = abs(v)

        if a <= k:
            out.append(float(v))
        else:
            shaped = k + span * math.tanh((a - k) / span)
            out.append(shaped if v >= 0 else -shaped)

    return out


def converge(samples, ch, rate, target, ceiling, knee=KNEE):
    """迭代「增益 → 限幅」，直到最响窗落在 target 附近（且峰值不超 ceiling）。

    返回 (输出采样, 总增益)。若本来就不需要动（增益≈1 且峰值已在上限内），
    返回 (samples, 1.0)，调用方据此跳过写盘 —— 这样重复跑是空操作。
    """
    peak0 = peak_of(samples)
    loud0 = loudest_window(samples, ch, rate)

    if loud0 <= 0:
        return list(samples), 1.0

    # ⚠️ **收敛判据必须是「两个目标都满足」，不能只看增益 ≈ 1**。
    #
    # 限幅会顺带把最响窗压得略低于目标（被压的那部分也在窗口里），于是
    # 「增益是否 ≈ 1」永远差那零点几个百分点 → 每次跑都想去补 → 又压一次峰。
    # 实测就是这么把格挡/踏云闪连压两轮的（峰值 22832 → 21400，越跑越扁）。
    #
    # 改成：**响度已进容差带，且峰值已在上限内 → 完成**。两条都满足时再跑就是空操作。
    if abs(loud0 - target) / target <= TOLERANCE and peak0 <= ceiling * FULL_SCALE:
        return list(samples), 1.0

    gain = target / loud0
    out = None

    for _ in range(ITERATIONS):
        out = soft_limit([v * gain for v in samples], knee, ceiling)
        loud = loudest_window(out, ch, rate)

        if loud <= 0 or abs(loud - target) / target <= TOLERANCE:
            break

        gain *= target / loud

    return out, gain


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="真正写回 wav（默认只报告）")
    ap.add_argument("--dir", default=AUDIO_DIR, help="音频目录")
    ap.add_argument("--target-db", type=float, default=TARGET_DB,
                    help="目标电平相对基准的 dB（默认 %.1f）" % TARGET_DB)
    ap.add_argument("--ceiling", type=float, default=CEILING,
                    help="软限幅上限（满刻度比例，默认 %.2f）" % CEILING)
    args = ap.parse_args()

    names = sorted(n[:-4] for n in os.listdir(args.dir)
                   if n.startswith("sfx_") and n.endswith(".wav"))

    if REFERENCE not in names:
        print("找不到基准 %s，中止" % REFERENCE, file=sys.stderr)
        return 1

    loaded = {n: read_wav(os.path.join(args.dir, n + ".wav")) for n in names}

    ref_loud = loudest_window(*loaded[REFERENCE])
    target = ref_loud * (10 ** (args.target_db / 20.0))

    print("基准 %s：最响 %.0fms 窗 RMS = %.0f" % (REFERENCE, LOUD_WINDOW_SEC * 1000, ref_loud))
    print("目标 = 基准 %+.1f dB = %.0f   软限幅上限 = %.2f FS (%.0f)\n"
          % (args.target_db, target, args.ceiling, args.ceiling * FULL_SCALE))

    print("%-16s %9s %9s %8s %8s %11s %11s" % (
        "clip", "整段RMS", "最响窗", "峰值", "增益", "限幅后峰值", "限幅后最响窗"))

    plan = []

    for n in names:
        samples, ch, rate = loaded[n]

        if n == REFERENCE:
            # 基准**永不改动** —— 它是锚（用户唯一认可「听着正好」的那条），
            # 动它就把整套对齐关系挪走了。它的峰值 0.944 FS 偏高、和别的叠会削，
            # 但那是「用户认可的听感」，宁可用限幅去压别人也不动它。
            print("%-16s %9.0f %9.0f %8d %8s %11s %11s   ← 基准，不动" % (
                n, rms(samples), loudest_window(samples, ch, rate),
                peak_of(samples), "—", "—", "—"))
            plan.append((n, list(samples), 1.0, ch, rate, peak_of(samples)))
            continue

        out, gain = converge(samples, ch, rate, target, args.ceiling)
        new_peak = peak_of(out)
        new_loud = loudest_window(out, ch, rate)

        print("%-16s %9.0f %9.0f %8d %8.3f %11d %11.0f" % (
            n, rms(samples), loudest_window(samples, ch, rate),
            peak_of(samples), gain, int(new_peak), new_loud))

        plan.append((n, out, gain, ch, rate, peak_of(samples)))

    print()

    if not args.apply:
        print("（这是预演。要写回请加 --apply）")
        return 0

    for n, out, gain, ch, rate, peak0 in plan:
        if abs(gain - 1.0) <= 1e-4:
            print("跳过 %-16s 已达标，无变化" % n)
            continue

        write_wav(os.path.join(args.dir, n + ".wav"), out, ch, rate)
        print("写入 %-16s 增益 ×%.3f + 限幅(≤%.2f FS)   峰值 %d → %d"
              % (n, gain, args.ceiling, peak0, int(peak_of(out))))

    return 0


if __name__ == "__main__":
    sys.exit(main())
