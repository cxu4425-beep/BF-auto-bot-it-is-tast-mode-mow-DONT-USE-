"""檢查錄製器產出的示範資料。

模仿學習最容易踩的坑是資料本身有問題卻不知道 —— 影格和動作對不齊、
取樣頻率不穩、或是九成的畫面都沒有任何輸入。這些在訓練完之後才發現
會浪費很多時間，先驗證比較划算。

用法:
    python inspect_dataset.py <session 資料夾>
    python inspect_dataset.py <根資料夾>        # 會列出底下所有 session
"""

import json
import sys
from collections import Counter
from pathlib import Path


def load_session(path: Path):
    actions_path = path / "actions.jsonl"
    frames_dir = path / "frames"
    if not actions_path.exists():
        raise FileNotFoundError(f"找不到 {actions_path}")

    records = []
    for lineno, line in enumerate(actions_path.read_text(encoding="utf-8").splitlines(), 1):
        line = line.strip()
        if not line:
            continue
        try:
            records.append(json.loads(line))
        except json.JSONDecodeError as exc:
            raise ValueError(f"{actions_path}:{lineno} 不是合法 JSON: {exc}") from exc

    frames = sorted(frames_dir.glob("*.jpg")) if frames_dir.exists() else []
    meta = {}
    meta_path = path / "meta.json"
    if meta_path.exists():
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
    return records, frames, meta


def report(path: Path) -> bool:
    """回傳 True 表示這份資料看起來可以拿去訓練。"""
    records, frames, meta = load_session(path)
    problems = []

    print(f"\n=== {path.name} ===")
    if not records:
        print("  ❌ 沒有任何動作紀錄")
        return False

    print(f"  影格檔案   : {len(frames)}")
    print(f"  動作紀錄   : {len(records)}")

    # 影格與動作必須一一對應，否則訓練時圖文不符
    if len(frames) != len(records):
        problems.append(f"影格數 {len(frames)} 與動作數 {len(records)} 不一致")

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
    return not problems


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
    total_frames = 0
    for s in sessions:
        try:
            if not report(s):
                ok = False
            total_frames += len(list((s / "frames").glob("*.jpg")))
        except Exception as exc:
            print(f"\n=== {s.name} ===\n  ❌ 讀取失敗: {exc}")
            ok = False

    if len(sessions) > 1:
        print(f"\n總計 {len(sessions)} 個 session，{total_frames} 幀")
        print(f"以 10Hz 估算約 {total_frames / 10 / 60:.0f} 分鐘的示範資料")

    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
