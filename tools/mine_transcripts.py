# -*- coding: utf-8 -*-
"""Mine Claude Code JSONL transcripts for a factual dev narrative."""
import json, os, sys, glob, io

DIRS = [
    ("UNITY", r"C:/Users/FNP20/.claude/projects/D--Desktop-Unity-Projects-CloudEyrie"),
    ("COCOS", r"C:/Users/FNP20/.claude/projects/D--Git-projects-CloudEyrie"),
]
OUT = r"D:/Desktop/Unity Projects/CloudEyrie/tools/_mined"
os.makedirs(OUT, exist_ok=True)


def text_of(content):
    """Return plain text from a message content (str or list of blocks)."""
    if isinstance(content, str):
        return content
    parts = []
    if isinstance(content, list):
        for b in content:
            if not isinstance(b, dict):
                continue
            t = b.get("type")
            if t == "text":
                parts.append(b.get("text", ""))
            elif t == "tool_result":
                pass  # skip tool results entirely
    return "\n".join(parts)


def is_human(ev):
    """True if this is a genuine human turn (not tool_result, not meta)."""
    if ev.get("type") != "user":
        return False
    msg = ev.get("message") or {}
    c = msg.get("content")
    if isinstance(c, list):
        # if every block is a tool_result, it's not human
        kinds = {b.get("type") for b in c if isinstance(b, dict)}
        if kinds and kinds <= {"tool_result"}:
            return False
        # strip tool_result blocks, keep text
        txt = "\n".join(b.get("text", "") for b in c
                        if isinstance(b, dict) and b.get("type") == "text")
        return bool(txt.strip())
    if isinstance(c, str):
        return bool(c.strip())
    return False


def first_text(ev):
    msg = ev.get("message") or {}
    c = msg.get("content")
    if isinstance(c, str):
        return c
    if isinstance(c, list):
        for b in c:
            if isinstance(b, dict) and b.get("type") == "text":
                return b.get("text", "")
    return ""


def clean(s):
    if not s:
        return ""
    s = s.replace("\r", " ")
    s = " ".join(s.split())
    return s


def process(tag, path):
    name = os.path.basename(path)
    users, assistants = [], []
    first_ts = last_ts = None
    n = 0
    with io.open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            n += 1
            line = line.strip()
            if not line:
                continue
            try:
                ev = json.loads(line)
            except Exception:
                continue
            ts = ev.get("timestamp")
            if ts:
                if first_ts is None or ts < first_ts:
                    first_ts = ts
                if last_ts is None or ts > last_ts:
                    last_ts = ts
            if is_human(ev):
                users.append((ts, clean(text_of((ev.get("message") or {}).get("content")))))
            elif ev.get("type") == "assistant":
                t = clean(first_text(ev))
                if t:
                    assistants.append((ts, t))

    print("=" * 78)
    print("TAG=%s FILE=%s" % (tag, name))
    print("  lines=%d  users=%d  assistant_texts=%d" % (n, len(users), len(assistants)))
    print("  first_ts=%s" % first_ts)
    print("  last_ts =%s" % last_ts)

    stem = "%s__%s" % (tag, name.replace(".jsonl", ""))
    upath = os.path.join(OUT, stem + ".users.txt")
    with io.open(upath, "w", encoding="utf-8") as f:
        for i, (ts, t) in enumerate(users):
            f.write("[%04d] %s | %s\n" % (i, ts or "?", t[:400]))
    apath = os.path.join(OUT, stem + ".asst.txt")
    with io.open(apath, "w", encoding="utf-8") as f:
        for i, (ts, t) in enumerate(assistants):
            f.write("[%04d] %s | %s\n" % (i, ts or "?", t[:200]))
    print("  wrote %s (%d)" % (upath, len(users)))
    print("  wrote %s (%d)" % (apath, len(assistants)))
    return first_ts, last_ts


for tag, d in DIRS:
    for p in sorted(glob.glob(os.path.join(d, "*.jsonl"))):
        try:
            process(tag, p)
        except Exception as e:
            print("ERR %s: %r" % (p, e))
