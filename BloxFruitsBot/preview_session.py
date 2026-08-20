"""把錄好的 session 疊上按鍵狀態，輸出成一支可以用播放器慢慢看的影片。

用法：
    python preview_session.py <session資料夾>
    python preview_session.py <session資料夾> --range 300:600 --scale 3

這是資料品質檢查，不是訓練用的東西 —— 目的是讓你能親眼確認「錄下來的
按鍵真的對得上當時的畫面」。錄製端是先存圖再讀鍵盤，理論上差距很小，
但這種事情用看的比用推論的可靠。

看的時候特別注意三件事：
  1. 你按住 W 前進的那幾秒，W 是不是整段都亮著（而不是一閃一閃）
  2. 攻擊的瞬間，LeftClick 有沒有跟畫面上的動作對上
  3. 游標的十字有沒有在轉視角時卡住不動 —— 卡住代表滑鼠視角沒被錄到

離開碼： 0 成功  1 資料有問題  2 路徑或參數錯誤
"""

import argparse
import json
import os
import sys

import cv2
import numpy as np

VIDEO_NAME = "frames.mp4"
DEFAULT_KEYS = ["W", "A", "S", "D", "Space", "Shift", "Z", "X", "C", "V",
                "E", "F", "Q", "R", "1", "2", "3", "4", "LeftClick", "RightClick"]

BG = (24, 20, 16)
KEY_OFF = (60, 52, 46)
KEY_ON = (90, 230, 120)
TEXT_OFF = (150, 140, 132)
TEXT_ON = (20, 20, 20)
CROSSHAIR = (80, 200, 255)

# 方框放不下完整名稱。滑鼠鍵是檢查攻擊時序最關鍵的兩個，
# 截成 "LeftCli" / "RightCl" 反而最難認，所以另外給短名。
SHORT = {"LeftClick": "LMB", "RightClick": "RMB", "Space": "SPC", "Shift": "SHF"}


def die(msg, code=2):
    print(f"✗ {msg}")
    sys.exit(code)


def load_actions(path):
    if not os.path.exists(path):
        die(f"找不到 {path}")
    rows, bad = [], 0
    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                bad += 1
    if bad:
        print(f"⚠️  {bad} 行解析失敗（錄製可能被硬中斷）")
    return rows


def load_meta(session):
    path = os.path.join(session, "meta.json")
    if not os.path.exists(path):
        return {}
    try:
        with open(path, encoding="utf-8-sig") as fh:
            return json.load(fh)
    except (OSError, json.JSONDecodeError):
        return {}


def iter_frames(session, start, stop):
    """從 frames/ 或 frames.mp4 依序吐出影格，兩種來源用同一個介面。"""
    frames_dir = os.path.join(session, "frames")
    names = sorted(n for n in os.listdir(frames_dir)
                   if n.lower().endswith(".jpg")) if os.path.isdir(frames_dir) else []

    if names:
        for name in names:
            try:
                i = int(os.path.splitext(name)[0])
            except ValueError:
                print(f"⚠️  檔名不是編號，略過: {name}")
                continue
            if i < start:
                continue
            if stop is not None and i >= stop:
                return
            img = cv2.imread(os.path.join(frames_dir, name))
            if img is None:
                print(f"⚠️  讀不了 {name}，略過")
                continue
            yield i, img
        return

    video = os.path.join(session, VIDEO_NAME)
    if not os.path.exists(video):
        die(f"{session} 裡既沒有 frames/ 也沒有 {VIDEO_NAME}")
    cap = cv2.VideoCapture(video)
    if not cap.isOpened():
        die(f"影片開不起來: {video}", 1)
    try:
        i = 0
        while True:
            ok, frame = cap.read()
            if not ok:
                return
            if i >= start and (stop is None or i < stop):
                yield i, frame
            i += 1
            if stop is not None and i >= stop:
                return
    finally:
        cap.release()


def draw_panel(canvas, y0, keys, held, index, t):
    """畫面下方的按鍵列。亮起來的是當下按著的鍵。"""
    h, w = canvas.shape[:2]
    cv2.rectangle(canvas, (0, y0), (w, h), BG, -1)

    box_w, box_h, gap = 52, 26, 5
    per_row = max(1, (w - gap) // (box_w + gap))
    for n, name in enumerate(keys):
        row, col = divmod(n, per_row)
        x = gap + col * (box_w + gap)
        y = y0 + 8 + row * (box_h + gap)
        if y + box_h > h:
            break
        on = name in held
        cv2.rectangle(canvas, (x, y), (x + box_w, y + box_h),
                      KEY_ON if on else KEY_OFF, -1)
        label = SHORT.get(name, name)[:7]
        size = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.36, 1)[0]
        cv2.putText(canvas, label,
                    (x + (box_w - size[0]) // 2, y + (box_h + size[1]) // 2),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.36,
                    TEXT_ON if on else TEXT_OFF, 1, cv2.LINE_AA)

    cv2.putText(canvas, f"#{index}  t={t:.2f}s", (gap, y0 - 8),
                cv2.FONT_HERSHEY_SIMPLEX, 0.45, (200, 200, 200), 1, cv2.LINE_AA)


def main(argv):
    parser = argparse.ArgumentParser(
        description="把 session 疊上按鍵狀態輸出成影片，用來檢查錄製有沒有錄對",
        formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    parser.add_argument("session")
    parser.add_argument("--out", default="", help="輸出檔（預設 <session>/preview.mp4）")
    parser.add_argument("--range", default="", metavar="A:B",
                        help="只輸出這段影格，例如 300:600")
    parser.add_argument("--scale", type=int, default=0,
                        help="放大倍率（預設自動放大到至少 640px 寬，字才看得清楚）")
    parser.add_argument("--fps", type=int, default=0, help="預設讀 meta.json 的取樣率")
    args = parser.parse_args(argv[1:])

    if not os.path.isdir(args.session):
        die(f"找不到資料夾: {args.session}")

    start, stop = 0, None
    if args.range:
        try:
            a, _, b = args.range.partition(":")
            start = int(a) if a else 0
            stop = int(b) if b else None
        except ValueError:
            die(f"--range 格式應該像 300:600，收到的是 {args.range!r}")

    meta = load_meta(args.session)
    keys = meta.get("tracked_keys") or DEFAULT_KEYS
    fps = args.fps or meta.get("sample_rate_hz") or 10

    actions = load_actions(os.path.join(args.session, "actions.jsonl"))
    by_index = {row.get("i"): row for row in actions}

    out_path = args.out or os.path.join(args.session, "preview.mp4")
    writer, scale, panel_h, out_size, written, missing = None, 1, 0, (0, 0), 0, 0
    seen_lo, seen_hi = None, None

    for index, img in iter_frames(args.session, start, stop):
        if writer is None:
            # 錄下來的影格只有 320px 寬，不放大的話疊上去的字根本看不清楚
            scale = args.scale or max(1, -(-640 // img.shape[1]))
            fw, fh = img.shape[1] * scale, img.shape[0] * scale
            per_row = max(1, (fw - 5) // 57)
            panel_h = 8 + -(-len(keys) // per_row) * 31 + 8
            # mp4v 要求長寬都是偶數
            out_size = (fw - fw % 2, (fh + panel_h) - (fh + panel_h) % 2)
            writer = cv2.VideoWriter(out_path, cv2.VideoWriter_fourcc(*"mp4v"), fps, out_size)
            if not writer.isOpened():
                die(f"寫不了 {out_path}（OpenCV VideoWriter 開啟失敗）", 1)
            print(f"輸出 {out_path} · {out_size[0]}x{out_size[1]} · {fps}fps · 放大 {scale}x")

        # INTER_NEAREST：放大是為了看清楚，不要讓內插把畫面糊掉
        big = cv2.resize(img, (img.shape[1] * scale, img.shape[0] * scale),
                         interpolation=cv2.INTER_NEAREST)

        seen_lo = index if seen_lo is None else min(seen_lo, index)
        seen_hi = index if seen_hi is None else max(seen_hi, index)

        row = by_index.get(index)
        if row is None:
            missing += 1
        held = set(row.get("keys", [])) if row else set()

        if row and "mx" in row and "my" in row:
            cx = int(row["mx"] * big.shape[1])
            cy = int(row["my"] * big.shape[0])
            cv2.line(big, (cx - 9, cy), (cx + 9, cy), CROSSHAIR, 1, cv2.LINE_AA)
            cv2.line(big, (cx, cy - 9), (cx, cy + 9), CROSSHAIR, 1, cv2.LINE_AA)

        canvas = np.full((big.shape[0] + panel_h, big.shape[1], 3), BG, np.uint8)
        canvas[:big.shape[0], :big.shape[1]] = big
        draw_panel(canvas, big.shape[0], keys, held, index,
                   float(row.get("t", 0.0)) if row else 0.0)

        # 尺寸必須跟開檔時宣告的一模一樣，否則 VideoWriter 會靜默丟掉這一幀
        writer.write(canvas[:out_size[1], :out_size[0]])
        written += 1

    if writer is None:
        die("沒有讀到任何影格", 1)
    writer.release()

    print(f"✓ 寫入 {written} 幀")

    span = (seen_hi - seen_lo + 1) if seen_lo is not None else 0
    if span > written:
        print(f"✗ 編號 {seen_lo}~{seen_hi} 之間少了 {span - written} 張影格 —— "
              "影格編號不連續，跑 inspect_dataset.py 看是哪裡斷的")
        return 1
    if missing:
        print(f"✗ 有 {missing} 幀在 actions.jsonl 裡找不到對應的紀錄 —— "
              "影格和動作對不齊，先跑 inspect_dataset.py")
        return 1
    print("用播放器打開它，逐格看按鍵有沒有跟畫面對上。")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
