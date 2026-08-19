"""把 decisions.jsonl 變成一份可以下判斷的統計報告。

用法：
    python analyze_decisions.py decisions.jsonl
    python analyze_decisions.py baseline.jsonl fewshot.jsonl   # 兩組對照

為什麼需要這個：終端機捲過去的字沒辦法回答「IDLE 佔幾成」。而那正是唯一
重要的問題 —— 上一次量到 120 步裡有 113 步 IDLE，那是在改掉別名和目標提示詞
之前。改完到底有沒有用，只能靠同樣的量法再量一次。

離開碼： 0 = 看起來健康  1 = 有問題  2 = 檔案讀不到
"""

import json
import re
import sys
import unicodedata
from collections import Counter
from datetime import datetime

# 判定門檻。這些是拿來「引起注意」的，不是物理定律 —— 邊界附近的數字
# 還是要自己看畫面判斷。
IDLE_FAIL = 0.40          # 在遊戲世界裡站著不動幾乎不會是對的
DEGENERATE_FAIL = 0.05    # 模型壞掉，不是決策
DOMINANT_FAIL = 0.70      # 永遠只出同一招，和永遠 IDLE 一樣沒用
SWITCH_WARN = 0.10        # 幾乎不換動作 = 卡住
LATENCY_WARN_MS = 4000    # 一輪超過這個長度就跟不上遊戲
MIN_STEPS = 30            # 少於這個數量，比例值沒有意義

CANONICAL = {
    "MOVE_FORWARD", "TURN_LEFT", "TURN_RIGHT", "JUMP", "ATTACK",
    "TALK_TO_NPC", "SWITCH_CHANNEL", "RECONNECT", "IDLE",
}


def parse_ts(raw):
    """C# 的 "o" 格式小數點後有 7 位，datetime.fromisoformat 只吃 3 或 6 位。"""
    if not raw:
        return None
    text = raw.replace("Z", "+00:00")
    text = re.sub(r"\.(\d{6})\d+", r".\1", text)
    try:
        return datetime.fromisoformat(text)
    except ValueError:
        return None


def width(text):
    """終端機顯示寬度。中文是全形，一個字算一格會讓整張表歪掉。"""
    return sum(2 if unicodedata.east_asian_width(c) in "WF" else 1 for c in text)


def pad(text, n, right=False):
    space = " " * max(0, n - width(text))
    return space + text if right else text + space


def percentile(values, pct):
    if not values:
        return 0
    ordered = sorted(values)
    idx = min(int(len(ordered) * pct), len(ordered) - 1)
    return ordered[idx]


def load(path):
    rows, bad = [], 0
    # utf-8-sig：舊版的紀錄檔第一行可能帶 BOM，不吃掉會在第一行就失敗
    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                bad += 1
    return rows, bad


def analyse(path):
    """回傳 (統計 dict, 問題清單)。"""
    rows, bad_lines = load(path)
    problems = []

    if not rows:
        return {"path": path, "steps": 0}, [f"{path} 裡沒有任何可解析的紀錄"]

    actions = [r.get("action", "") or "?" for r in rows]
    counts = Counter(actions)
    n = len(rows)

    degenerate = sum(1 for r in rows if r.get("degenerate"))
    latencies = [r["latency_ms"] for r in rows if isinstance(r.get("latency_ms"), (int, float))]
    switches = sum(1 for a, b in zip(actions, actions[1:]) if a != b)
    switch_rate = switches / (n - 1) if n > 1 else 0.0

    longest_run, run = 1, 1
    for a, b in zip(actions, actions[1:]):
        run = run + 1 if a == b else 1
        longest_run = max(longest_run, run)

    first_ts, last_ts = parse_ts(rows[0].get("ts")), parse_ts(rows[-1].get("ts"))
    duration = (last_ts - first_ts).total_seconds() if first_ts and last_ts else 0.0

    # 模型講了、但我們接不住的說法。這是 ACTION_ALIASES 該補什麼的直接證據。
    unmapped = Counter()
    for r in rows:
        raw = (r.get("raw_action") or "").strip()
        if raw and r.get("action") == "IDLE" and raw.upper() not in ("IDLE", "IDLE."):
            unmapped[raw[:40]] += 1

    idle_ratio = counts.get("IDLE", 0) / n
    degen_ratio = degenerate / n
    top_action, top_count = counts.most_common(1)[0]
    dominant_ratio = top_count / n

    stats = {
        "path": path,
        "tag": rows[0].get("tag", "") or "(無標籤)",
        "steps": n,
        "duration_s": duration,
        "period_s": duration / (n - 1) if n > 1 and duration else 0.0,
        "counts": counts,
        "idle_ratio": idle_ratio,
        "degen_ratio": degen_ratio,
        "dominant": (top_action, dominant_ratio),
        "switch_rate": switch_rate,
        "longest_run": longest_run,
        "p50": percentile(latencies, 0.50),
        "p95": percentile(latencies, 0.95),
        "max_latency": max(latencies) if latencies else 0,
        "unmapped": unmapped,
        "bad_lines": bad_lines,
        "rows": rows,
    }

    if bad_lines:
        problems.append(f"{bad_lines} 行解析失敗（檔案可能在寫入中途被中斷）")
    if n < MIN_STEPS:
        problems.append(f"只有 {n} 步，少於 {MIN_STEPS} 步，比例值還不夠穩定 —— 再多跑一會兒")
    if idle_ratio > IDLE_FAIL:
        problems.append(
            f"IDLE 佔 {idle_ratio:.0%}（門檻 {IDLE_FAIL:.0%}）。"
            "在遊戲世界裡站著不動幾乎不會是對的答案，這是「旁白模式」的特徵"
        )
    if degen_ratio > DEGENERATE_FAIL:
        problems.append(
            f"{degen_ratio:.0%} 的回合模型輸出退化（門檻 {DEGENERATE_FAIL:.0%}）。"
            "這不是決策品質問題，是模型壞掉 —— 先縮小 BOT_MAX_IMAGE_WIDTH 或減少送出的圖片數"
        )
    if dominant_ratio > DOMINANT_FAIL and top_action != "IDLE":
        problems.append(
            f"{dominant_ratio:.0%} 的步驟都是 {top_action}（門檻 {DOMINANT_FAIL:.0%}）。"
            "永遠只出同一招，和永遠 IDLE 一樣沒有用"
        )
    if n > 1 and switch_rate < SWITCH_WARN:
        problems.append(
            f"動作幾乎不變（切換率 {switch_rate:.0%}，最長連續 {longest_run} 步）—— 它大概是卡住了"
        )
    if stats["p95"] > LATENCY_WARN_MS:
        problems.append(
            f"延遲 p95 = {stats['p95']:.0f}ms（門檻 {LATENCY_WARN_MS}ms），跟不上遊戲節奏"
        )

    return stats, problems


def report(stats, problems):
    print(f"\n{'=' * 66}")
    print(f"  {stats['path']}   [{stats.get('tag', '')}]")
    print("=" * 66)

    if not stats["steps"]:
        for p in problems:
            print(f"  ✗ {p}")
        return

    n = stats["steps"]
    print(f"步數: {n}    時長: {stats['duration_s']:.0f}s    "
          f"平均一輪: {stats['period_s']:.2f}s")
    print(f"延遲: p50 {stats['p50']:.0f}ms   p95 {stats['p95']:.0f}ms   "
          f"最大 {stats['max_latency']:.0f}ms")

    print("\n動作分布：")
    for action, count in stats["counts"].most_common():
        bar = "█" * int(round(count / n * 40))
        mark = " ←" if action == "IDLE" else ""
        print(f"  {action:<16} {count:>5}  {count / n:>5.1%}  {bar}{mark}")

    print(f"\n動作切換率: {stats['switch_rate']:.0%}"
          f"    最長連續同一動作: {stats['longest_run']} 步")
    print(f"退化回合: {stats['degen_ratio']:.1%}")

    if stats["unmapped"]:
        print("\n模型講了但我們接不住的說法（退回成 IDLE）：")
        for raw, count in stats["unmapped"].most_common(8):
            print(f"  {count:>4}x  {raw!r}")
        print("  → 把這些加進 main.py 的 ACTION_ALIASES，就能直接救回這些決策")

    # 抽幾句思考出來，讓你知道它「為什麼」這樣決定。
    # 數字說不出模型是誤判畫面還是誤解任務，這幾句可以。
    print("\n幾句實際的思考內容：")
    seen = set()
    shown = 0
    for row in stats["rows"]:
        action = row.get("action", "")
        thought = (row.get("thought") or "").strip().replace("\n", " ")
        if action in seen or not thought:
            continue
        seen.add(action)
        print(f"  [{action}] {thought[:110]}")
        shown += 1
        if shown >= 5:
            break

    print()
    if problems:
        for p in problems:
            print(f"  ✗ {p}")
    else:
        print("  ✓ 沒有觸發任何門檻，決策品質看起來可用")


def compare(all_stats):
    usable = [s for s in all_stats if s["steps"]]
    if len(usable) < 2:
        return
    print(f"\n{'=' * 66}")
    print("  對照")
    print("=" * 66)
    print(pad("指標", 22) + "".join(pad(s.get("tag", "?")[:14], 16, right=True)
                                    for s in usable))
    rows = [
        ("步數", lambda s: f"{s['steps']}"),
        ("IDLE 比例", lambda s: f"{s['idle_ratio']:.1%}"),
        ("退化比例", lambda s: f"{s['degen_ratio']:.1%}"),
        ("ATTACK 次數", lambda s: f"{s['counts'].get('ATTACK', 0)}"),
        ("TALK_TO_NPC 次數", lambda s: f"{s['counts'].get('TALK_TO_NPC', 0)}"),
        ("動作切換率", lambda s: f"{s['switch_rate']:.0%}"),
        ("延遲 p95", lambda s: f"{s['p95']:.0f}ms"),
    ]
    for label, fn in rows:
        print(pad(label, 22) + "".join(pad(fn(s), 16, right=True) for s in usable))
    print("\n注意：兩次是在不同的遊戲情境下錄的，差異不完全等於設定的效果。"
          "\n差距要夠大（例如 IDLE 從 90% 掉到 40%）才值得下結論。")


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    all_stats, any_problem = [], False
    for path in argv[1:]:
        try:
            stats, problems = analyse(path)
        except OSError as exc:
            print(f"讀不到 {path}: {exc}")
            return 2
        report(stats, problems)
        all_stats.append(stats)
        any_problem = any_problem or bool(problems)

    compare(all_stats)
    return 1 if any_problem else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
