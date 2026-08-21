"""檢查錄製器產出的示範資料。

模仿學習最容易踩的坑是資料本身有問題卻不知道 —— 影格和動作對不齊、
取樣頻率不穩、或是九成的畫面都沒有任何輸入。這些在訓練完之後才發現
會浪費很多時間，先驗證比較划算。

用法:
    python inspect_dataset.py <session 資料夾>
    python inspect_dataset.py <根資料夾>        # 會列出底下所有 session，
                                               # 並依標籤統計資料平不平衡
"""

import json
import sys
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

# 最多的標籤是最少的幾倍就該補錄。差 2 倍的時候，少的那類還撐得住；
# 到 3、4 倍時模型會把多數類當成通用解，而這件事從訓練損失上看不出來 ——
# 少數類就算全錯，對整體損失的影響也很小。
BALANCE_WARN_RATIO = 2.0


def width(text):
    """終端機顯示寬度。中文是全形，一個字算一格會讓表格排歪。"""
    return sum(2 if unicodedata.east_asian_width(c) in "WF" else 1 for c in text)


def pad(text, n):
    return text + " " * max(0, n - width(text))


def count_frames(path: Path, meta: dict):
    """回傳 (張數, 來源說明)。張數不確定時回傳 None。

    frames/ 被 pack_session.py 壓成 frames.mp4 之後就沒有 jpg 了，
    這時候要改看 meta.json 記下的張數，否則會誤判成「影格全部不見」。
    """
    frames_dir = path / "frames"
    if frames_dir.exists():
        n = len(list(frames_dir.glob("*.jpg")))
        if n:
            return n, "frames/"
    if (path / "frames.mp4").exists():
        packed = meta.get("packed") or {}
        n = packed.get("frames")
        return (n if isinstance(n, int) else None), "frames.mp4"
    return 0, "frames/"


def load_session(path: Path):
    actions_path = path / "actions.jsonl"
    frames_dir = path / "frames"
    if not actions_path.exists():
        raise FileNotFoundError(f"找不到 {actions_path}")

    records = []
    # utf-8-sig：舊版錄製器寫出來的檔案開頭有 BOM，不吃掉會在第一行就解析失敗
    for lineno, line in enumerate(
            actions_path.read_text(encoding="utf-8-sig").splitlines(), 1):
        line = line.strip()
        if not line:
            continue
        try:
            records.append(json.loads(line))
        except json.JSONDecodeError as exc:
            raise ValueError(f"{actions_path}:{lineno} 不是合法 JSON: {exc}") from exc

    meta = {}
    meta_path = path / "meta.json"
    if meta_path.exists():
        meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))

    n_frames, source = count_frames(path, meta)
    return records, n_frames, source, meta


def report(path: Path) -> dict:
    """印出單一 session 的檢查結果，並回傳供彙總用的統計。"""
    records, n_frames, source, meta = load_session(path)
    problems = []
    label = (meta.get("label") or "").strip()

    print(f"\n=== {path.name} ===")
    if label:
        print(f"  標籤       : {label}")
    else:
        print("  標籤       : （沒有標籤）")

    if not records:
        print("  ❌ 沒有任何動作紀錄")
        return {"ok": False, "label": label, "frames": 0, "duration": 0.0}

    if n_frames is None:
        print(f"  影格檔案   : ?（已打包成 {source}，meta.json 沒記張數）")
    else:
        print(f"  影格檔案   : {n_frames}" + (f"（來自 {source}）" if source != "frames/" else ""))
    print(f"  動作紀錄   : {len(records)}")

    # 影格與動作必須一一對應，否則訓練時圖文不符
    if n_frames is not None and n_frames != len(records):
        problems.append(f"影格數 {n_frames} 與動作數 {len(records)} 不一致")

    indices = [r["i"] for r in records]
    if indices != list(range(len(indices))):
        problems.append("索引不連續，可能有掉幀或檔案被動過")

    duration = records[-1]["t"] - records[0]["t"] if len(records) > 1 else 0.0
    actual_hz = (len(records) - 1) / duration if duration > 0 else 0.0
    declared_hz = meta.get("sample_rate_hz")
    print(f"  時間長度   : {duration:.1f} 秒")
    print(f"  實際頻率   : {actual_hz:.1f} Hz" + (f"（設定 {declared_hz} Hz）" if declared_hz else ""))

    if declared_hz and actual_hz > 0 and abs(actual_hz - declared_hz) / declared_hz > 0.25:
        problems.append(f"實際取樣頻率 {actual_hz:.1f}Hz 與設定 {declared_hz}Hz 差距超過 25%")

    # 取樣間隔的穩定度。抖動太大代表某幾輪卡住了
    if len(records) > 2:
        gaps = [records[i]["t"] - records[i - 1]["t"] for i in range(1, len(records))]
        gaps.sort()
        p50 = gaps[len(gaps) // 2]
        p95 = gaps[int(len(gaps) * 0.95)]
        print(f"  間隔 p50/p95: {p50 * 1000:.0f} / {p95 * 1000:.0f} ms")
        if p50 > 0 and p95 > p50 * 3:
            problems.append(f"取樣間隔抖動大（p95 是 p50 的 {p95 / p50:.1f} 倍）")

    # 按鍵分布。這是最重要的一項：資料不平衡的話模型只會學到最常見的動作
    counter = Counter()
    idle = 0
    for r in records:
        keys = r.get("keys", [])
        if not keys:
            idle += 1
        counter.update(keys)

    print(f"  無輸入影格 : {idle} （{idle / len(records) * 100:.0f}%）")
    print("  按鍵分布   :")
    if counter:
        for key, n in counter.most_common():
            pct = n / len(records) * 100
            bar = "█" * max(1, int(pct / 2))
            print(f"      {key:<11} {n:>6}  {pct:>5.1f}%  {bar}")
    else:
        print("      （完全沒有按鍵）")

    if idle / len(records) > 0.7:
        problems.append(f"{idle / len(records) * 100:.0f}% 的影格沒有任何輸入，模型很可能學成一直不動")
    if not counter:
        problems.append("整段沒有任何按鍵，這份資料無法用於訓練")

    # 滑鼠位置分布，看得出有沒有在瞄準
    mxs = [r.get("mx", 0.5) for r in records]
    mys = [r.get("my", 0.5) for r in records]
    if mxs:
        spread = (max(mxs) - min(mxs)) + (max(mys) - min(mys))
        print(f"  游標移動幅度: {spread:.2f} （0 表示完全沒動）")
        if spread < 0.02:
            print("      ⚠ 游標幾乎沒移動 —— Roblox 鎖定游標時會這樣，"
                  "代表鏡頭操作沒被記錄到")

    if problems:
        print("  ⚠ 需要注意:")
        for p in problems:
            print(f"      - {p}")
    else:
        print("  ✅ 看起來可以用")

    return {
        "ok": not problems,
        "label": label,
        "frames": len(records) if n_frames is None else n_frames,
        "duration": duration,
    }


def balance_report(stats: list) -> bool:
    """依標籤統計，看資料有沒有偏到某一類。回傳 True 表示夠平衡。

    這是「戰鬥 + 導航一起做」時最容易翻車的地方：室內只錄了三趟、
    室外錄了二十趟，訓練損失照樣漂亮，但模型在室內會亂走。
    """
    groups = defaultdict(lambda: {"sessions": 0, "frames": 0, "duration": 0.0})
    unlabelled = 0
    for s in stats:
        key = s["label"] or "（沒有標籤）"
        if not s["label"]:
            unlabelled += 1
        g = groups[key]
        g["sessions"] += 1
        g["frames"] += s["frames"]
        g["duration"] += s["duration"]

    total = sum(g["frames"] for g in groups.values())
    if total <= 0:
        return True

    print("\n標籤分布：")
    name_w = max(width(k) for k in groups) + 2
    for name, g in sorted(groups.items(), key=lambda kv: -kv[1]["frames"]):
        pct = g["frames"] / total
        bar = "█" * int(round(pct * 30))
        print(f"  {pad(name, name_w)}{g['sessions']:>3} 段 · {g['frames']:>7,} 幀 · "
              f"{g['duration'] / 60:>5.1f} 分 · {pct:>5.1%}  {bar}")

    ok = True
    labelled = {k: v for k, v in groups.items() if k != "（沒有標籤）"}
    if len(labelled) >= 2:
        lo = min(g["frames"] for g in labelled.values())
        hi = max(g["frames"] for g in labelled.values())
        if lo > 0 and hi / lo > BALANCE_WARN_RATIO:
            print(f"\n  ⚠ 最多的標籤是最少的 {hi / lo:.1f} 倍"
                  f"（超過 {BALANCE_WARN_RATIO:.0f} 倍就該補錄少的那邊）。")
            print("    模型會把數量多的那類當成通用解，而這件事從訓練損失上看不出來。")
            ok = False
    if unlabelled:
        print(f"\n  ⚠ 有 {unlabelled} 段沒有標籤，沒被算進上面的比例。"
              "\n    錄製器的「這段是什麼」欄位填了才統計得到。")

    return ok


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    root = Path(sys.argv[1])
    if not root.exists():
        print(f"找不到路徑: {root}")
        return 2

    sessions = [root] if (root / "actions.jsonl").exists() else sorted(
        d for d in root.iterdir() if d.is_dir() and (d / "actions.jsonl").exists())

    if not sessions:
        print(f"{root} 底下找不到任何 session（需要有 actions.jsonl）")
        return 2

    ok = True
    stats = []
    for s in sessions:
        try:
            result = report(s)
            stats.append(result)
            if not result["ok"]:
                ok = False
        except Exception as exc:
            print(f"\n=== {s.name} ===\n  ❌ 讀取失敗: {exc}")
            ok = False

    if len(sessions) > 1 and stats:
        total_frames = sum(s["frames"] for s in stats)
        total_minutes = sum(s["duration"] for s in stats) / 60
        print(f"\n總計 {len(sessions)} 個 session，{total_frames:,} 幀，"
              f"約 {total_minutes:.0f} 分鐘的示範資料")
        # 時長直接從紀錄的時間戳算，不再假設一定是 10Hz
        if not balance_report(stats):
            ok = False

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
