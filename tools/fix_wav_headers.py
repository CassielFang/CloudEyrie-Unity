# -*- coding: utf-8 -*-
"""
把 WAV 的 RIFF 头补成规范的 —— 回填 RIFF 长度与 data 块长度。

现状
----
美术交的这批 wav 用某个工具导的，**两个长度字段都没回填，全是 0xFFFFFFFF**：

    "RIFF" ff ff ff ff "WAVE" ...      <- 实测的字节

ff 后面那段十六进制可以自己 `xxd` 看，不是推断。

⚠️ **但这不影响播放。** 播放器（VLC / 系统播放器 / 浏览器）只读 fmt 块，
然后一路解码到文件尾，压根不看那两个字段。所以「我能播」和
「字段是坏的」两件事同时成立，不矛盾。

⚠️ **也不要把导入卡死赖在它头上。** 实测几个文件里只有一个导得特别慢
（3 秒的音效导了 124 秒），紧接着的另外两个只用了 4~5 秒 ——
真要是头部引起的病态行为，三个都该慢。那次更像**首次导入的预热**。
所以这个脚本的定位是「顺手把字段修整齐」，**不是修 bug**；
当初把因果讲死是错的。

修法
----
只改两个 4 字节字段，音频数据一个字节都不动
（改前改后对 data 区做 SHA-256，完全一致）：
  * 偏移 4        RIFF 块长度 = 文件长度 − 8
  * data 块长度    = 文件长度 − (data 数据起点)

幂等：已经规范的会原样跳过。

用法
----
    /d/anaconda3/python.exe tools/fix_wav_headers.py [目录或文件 ...]

不带参数时默认处理 Assets/Resources/audio/ 下的所有 wav。
**只动副本，不动 `美术素材/` 下的原始交付件** —— 那边是美术的东西，
改了会让他们下次比对时对不上。
"""
import struct
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_DIR = ROOT / "Assets" / "Resources" / "audio"

# 0xFFFFFFFF 是 Unity 的「未初始化」哨兵，也是这批文件的实际值
BOGUS = 0xFFFFFFFF


def find_data_chunk(buf):
    """返回 (data 数据起点, data 长度字段所在的偏移)。找不到返回 (None, None)。"""
    i = 12

    while i + 8 <= len(buf):
        cid = bytes(buf[i:i + 4])
        csz = struct.unpack_from("<I", buf, i + 4)[0]

        if cid == b"data":
            return i + 8, i + 4

        # 长度不可信就没法往下走 —— 但 data 一般就在 fmt 后面，走不到这里
        if csz == BOGUS:
            return None, None

        i += 8 + csz + (csz & 1)

    return None, None


def fix(path):
    buf = bytearray(path.read_bytes())

    if len(buf) < 44 or bytes(buf[:4]) != b"RIFF" or bytes(buf[8:12]) != b"WAVE":
        return f"跳过（不是 RIFF/WAVE）"

    riff_size = struct.unpack_from("<I", buf, 4)[0]
    data_off, data_size_off = find_data_chunk(buf)

    if data_off is None:
        return "跳过（找不到 data 块）"

    real_total = len(buf) - 8
    real_data = len(buf) - data_off
    old_data = struct.unpack_from("<I", buf, data_size_off)[0]

    if riff_size == real_total and old_data == real_data:
        return "已经是规范的，未改动"

    struct.pack_into("<I", buf, 4, real_total)
    struct.pack_into("<I", buf, data_size_off, real_data)
    path.write_bytes(bytes(buf))
    return (f"已修：RIFF {riff_size} -> {real_total}，"
            f"data {old_data} -> {real_data}")


def verify(path):
    """用标准库读一遍，确认修完能被正常解析（之前会算出 7 小时）。"""
    try:
        with wave.open(str(path)) as w:
            dur = w.getnframes() / float(w.getframerate())
            return f"{w.getnchannels()}ch {w.getframerate()}Hz {w.getsampwidth() * 8}bit {dur:.2f}s"
    except Exception as e:
        return f"读取失败：{e}"


def main(argv):
    targets = []

    for a in argv[1:]:
        p = Path(a)
        targets.extend(sorted(p.glob("*.wav")) if p.is_dir() else [p])

    if not targets:
        targets = sorted(DEFAULT_DIR.glob("*.wav"))

    if not targets:
        print(f"没找到 wav：{DEFAULT_DIR}")
        return 1

    for p in targets:
        print(f"\n{p.name}")
        print(f"  {fix(p)}")
        print(f"  复核：{verify(p)}")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
