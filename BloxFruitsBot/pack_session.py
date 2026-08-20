"""把錄好的示範資料打包成影片，省下大部分硬碟空間。

用法：
    python pack_session.py pack   <session資料夾>    # frames/ -> frames.mp4
    python pack_session.py verify <session資料夾>    # 檢查影片跟原始幀對不對得上
    python pack_session.py unpack <session資料夾>    # frames.mp4 -> frames/

pack 預設「不會」刪掉原始的 frames/。要刪必須明確加 --delete-frames，
而且只有在同一次執行裡驗證通過才會真的刪 —— 張數對不上的資料集，
錯位是靜默的：模型會學到「看到這張畫面要按上一幀的鍵」，訓練照樣收斂，
只是學到的東西是錯的。所以刪除永遠排在驗證後面。

編碼器：有 ffmpeg 就用 ffmpeg（H.264，壓得最好又能控品質），
沒有就退回 OpenCV 內建的 mp4v（不用另外安裝，但壓縮率和品質都差一截）。

離開碼： 0 成功  1 有問題  2 路徑或參數錯誤
"""

import argparse
import json
import os
import shutil
import subprocess
import sys

import cv2
import numpy as np

FRAME_GLOB = "%06d.jpg"      # 錄製端從 000000 開始，不是 000001
FIRST_INDEX = 0
VIDEO_NAME = "frames.mp4"

# 抽樣比對用的門檻。這裡量的是「在 JPEG 已經有損之上，又額外損失多少」，
# 所以標準要比一般影片壓縮嚴格 —— 原始檔一刪就回不來了。
#
# 36dB 不是隨便挑的：實測同一段素材，crf 20 量到最低 39dB（畫面完好），
# crf 40 量到 32dB（已經明顯糊掉但檔案小了兩百多倍，很容易讓人誤以為賺到）。
# 門檻放在兩者中間，才擋得住「壓太兇」這種會靜默毀掉資料集的設定。
PSNR_MIN_DB = 36.0
PSNR_SAMPLES = 24


def die(msg, code=2):
    print(f"✗ {msg}")
    sys.exit(code)


def session_paths(session):
    if not os.path.isdir(session):
        die(f"找不到資料夾: {session}")
    return (
        os.path.join(session, "frames"),
        os.path.join(session, "actions.jsonl"),
        os.path.join(session, "meta.json"),
        os.path.join(session, VIDEO_NAME),
    )


def read_meta(meta_path):
    if not os.path.exists(meta_path):
        return {}
    try:
        with open(meta_path, encoding="utf-8-sig") as fh:
            return json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"⚠️  meta.json 讀不起來（{exc}），改用預設值")
        return {}


def write_meta(meta_path, meta):
    with open(meta_path, "w", encoding="utf-8") as fh:
        json.dump(meta, fh, ensure_ascii=False, indent=2)


def list_frames(frames_dir):
    if not os.path.isdir(frames_dir):
        return []
    names = sorted(n for n in os.listdir(frames_dir) if n.lower().endswith(".jpg"))
    return [os.path.join(frames_dir, n) for n in names]


def count_actions(actions_path):
    if not os.path.exists(actions_path):
        return None
    with open(actions_path, encoding="utf-8-sig") as fh:
        return sum(1 for line in fh if line.strip())


def dir_size(path):
    total = 0
    for root, _, files in os.walk(path):
        for name in files:
            total += os.path.getsize(os.path.join(root, name))
    return total


def human(n):
    for unit in ("B", "KB", "MB", "GB"):
        if n < 1024 or unit == "GB":
            return f"{n:.0f}{unit}" if unit == "B" else f"{n:.1f}{unit}"
        n /= 1024.0


def find_ffmpeg():
    return shutil.which("ffmpeg")


# ── 打包 ──────────────────────────────────────────────────────────

def pack_ffmpeg(ffmpeg, frames_dir, out_path, fps, crf):
    """H.264。scale 濾鏡是必要的：yuv420p 要求長寬都是偶數，
    而影格高度是由客戶區長寬比算出來的，很容易是奇數。"""
    for codec, extra in (("libx264", ["-crf", str(crf), "-preset", "slow"]),
                         ("mpeg4", ["-q:v", "3"])):
        cmd = [
            ffmpeg, "-y", "-loglevel", "error",
            "-framerate", str(fps),
            "-start_number", str(FIRST_INDEX),
            "-i", os.path.join(frames_dir, FRAME_GLOB),
            "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-c:v", codec, *extra,
            "-pix_fmt", "yuv420p",
            out_path,
        ]
        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode == 0:
            return codec
        print(f"⚠️  {codec} 失敗，換下一個編碼器")
        print("   " + (result.stderr.strip().splitlines() or ["(無輸出)"])[-1])
    return None


def pack_opencv(frames_dir, out_path, fps):
    """沒有 ffmpeg 時的退路。mp4v 壓縮率差一截，而且 OpenCV 沒有給
    可靠的品質參數，所以只在沒得選的時候用。"""
    frames = list_frames(frames_dir)
    if not frames:
        return None
    first = cv2.imread(frames[0])
    if first is None:
        die(f"讀不了影格: {frames[0]}", 1)
    h, w = first.shape[:2]
    # 長寬轉成偶數，maintain 相容性；奇數在部分播放器上會出問題
    w, h = w - (w % 2), h - (h % 2)

    writer = cv2.VideoWriter(out_path, cv2.VideoWriter_fourcc(*"mp4v"), fps, (w, h))
    if not writer.isOpened():
        die("OpenCV 開不了 VideoWriter，請改裝 ffmpeg", 1)
    try:
        for path in frames:
            img = cv2.imread(path)
            if img is None:
                die(f"讀不了影格: {path}", 1)
            writer.write(img[:h, :w])
    finally:
        writer.release()
    return "mp4v"


def cmd_pack(args):
    frames_dir, actions_path, meta_path, video_path = session_paths(args.session)

    frames = list_frames(frames_dir)
    if not frames:
        die(f"{frames_dir} 裡沒有影格，沒東西可以打包")

    meta = read_meta(meta_path)
    fps = args.fps or meta.get("sample_rate_hz") or 10
    n_actions = count_actions(actions_path)

    print(f"影格 {len(frames)} 張 · {fps}Hz · 來源 {human(dir_size(frames_dir))}")
    if n_actions is not None and n_actions != len(frames):
        print(f"⚠️  actions.jsonl 有 {n_actions} 行，影格卻有 {len(frames)} 張 —— "
              "這個 session 本來就對不齊，先跑 inspect_dataset.py 查清楚再打包")

    ffmpeg = None if args.encoder == "opencv" else find_ffmpeg()
    if args.encoder == "ffmpeg" and not ffmpeg:
        die("指定了 --encoder ffmpeg，但 PATH 上找不到 ffmpeg")

    if ffmpeg:
        print(f"用 ffmpeg 編碼（crf {args.crf}）...")
        codec = pack_ffmpeg(ffmpeg, frames_dir, video_path, fps, args.crf)
    else:
        print("PATH 上沒有 ffmpeg，改用 OpenCV mp4v（壓縮率較差，品質不可調）...")
        print("   想壓得更小的話，裝 ffmpeg 之後重跑一次。")
        codec = pack_opencv(frames_dir, video_path, fps)

    if not codec or not os.path.exists(video_path):
        die("編碼失敗，原始影格原封不動保留著", 1)

    src, dst = dir_size(frames_dir), os.path.getsize(video_path)
    ratio = src / dst if dst else 0
    print(f"\n✓ {VIDEO_NAME}  {human(src)} -> {human(dst)}  （小了 {ratio:.1f} 倍，codec {codec}）")

    ok = verify(args.session, quiet=False)

    meta["packed"] = {
        "video": VIDEO_NAME,
        "codec": codec,
        "fps": fps,
        "frames": len(frames),
        "source_bytes": src,
        "video_bytes": dst,
        "verified": ok,
    }
    write_meta(meta_path, meta)

    if args.delete_frames:
        if not ok:
            print("\n✗ 驗證沒過，frames/ 保留不刪。")
            return 1
        shutil.rmtree(frames_dir)
        print(f"\n✓ 已刪除 frames/，省下 {human(src)}")
    else:
        print(f"\nframes/ 保留著（{human(src)}）。確認影片沒問題後，"
              "加 --delete-frames 重跑一次就會刪掉。")
    return 0 if ok else 1


# ── 驗證 ──────────────────────────────────────────────────────────

def verify(session, quiet=False):
    """張數、尺寸、抽樣畫質三項都要過。

    注意：這裡「不可能」做逐像素比對 —— 影片是有損壓縮，解出來本來就
    跟原始 JPEG 不同。能做的是量差多少，所以用 PSNR 抽樣。
    """
    frames_dir, actions_path, _, video_path = session_paths(session)

    if not os.path.exists(video_path):
        print(f"✗ 找不到 {video_path}")
        return False

    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"✗ 影片開不起來: {video_path}")
        return False

    originals = list_frames(frames_dir)
    n_actions = count_actions(actions_path)

    # 抽樣要平均散佈在整段，只看開頭會漏掉後段的問題
    sample_at = set()
    if originals:
        step = max(1, len(originals) // PSNR_SAMPLES)
        sample_at = set(range(0, len(originals), step))

    psnrs, video_frames, dims = [], 0, None
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        if dims is None:
            dims = (frame.shape[1], frame.shape[0])
        if video_frames in sample_at and video_frames < len(originals):
            ref = cv2.imread(originals[video_frames])
            if ref is not None:
                h = min(ref.shape[0], frame.shape[0])
                w = min(ref.shape[1], frame.shape[1])
                psnrs.append(cv2.PSNR(ref[:h, :w], frame[:h, :w]))
        video_frames += 1
    cap.release()

    problems = []
    if originals and video_frames != len(originals):
        problems.append(f"影片 {video_frames} 幀，原始影格 {len(originals)} 張 —— 數量對不上")
    if n_actions is not None and video_frames != n_actions:
        problems.append(f"影片 {video_frames} 幀，actions.jsonl {n_actions} 行 —— 數量對不上")

    if originals and dims:
        ref = cv2.imread(originals[0])
        if ref is not None:
            rw, rh = ref.shape[1], ref.shape[0]
            if abs(rw - dims[0]) > 1 or abs(rh - dims[1]) > 1:
                problems.append(f"尺寸不符：原始 {rw}x{rh}，影片 {dims[0]}x{dims[1]}")

    if psnrs:
        lo, avg = min(psnrs), sum(psnrs) / len(psnrs)
        if lo < PSNR_MIN_DB:
            problems.append(
                f"畫質損失偏大：抽樣 {len(psnrs)} 張，最低 PSNR {lo:.1f}dB"
                f"（門檻 {PSNR_MIN_DB}dB）—— 用更低的 --crf 重壓，或先別刪原始幀"
            )

    if not quiet:
        print(f"\n驗證：影片 {video_frames} 幀"
              + (f" · 原始 {len(originals)} 張" if originals else " · 原始影格已刪除")
              + (f" · actions {n_actions} 行" if n_actions is not None else ""))
        if dims:
            print(f"      尺寸 {dims[0]}x{dims[1]}")
        if psnrs:
            print(f"      PSNR 抽樣 {len(psnrs)} 張：平均 {sum(psnrs) / len(psnrs):.1f}dB"
                  f"、最低 {min(psnrs):.1f}dB（越高越接近原圖，40dB 以上肉眼看不出差別）")
        for p in problems:
            print(f"  ✗ {p}")
        if not problems:
            print("  ✓ 通過")

    return not problems


def cmd_verify(args):
    return 0 if verify(args.session) else 1


# ── 解包 ──────────────────────────────────────────────────────────

def cmd_unpack(args):
    frames_dir, actions_path, meta_path, video_path = session_paths(args.session)

    if not os.path.exists(video_path):
        die(f"找不到 {video_path}")
    if os.path.isdir(frames_dir) and list_frames(frames_dir) and not args.force:
        die(f"{frames_dir} 已經有影格了。確定要覆蓋就加 --force")

    os.makedirs(frames_dir, exist_ok=True)
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        die(f"影片開不起來: {video_path}", 1)

    index = FIRST_INDEX
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            cv2.imwrite(os.path.join(frames_dir, FRAME_GLOB % index),
                        frame, [int(cv2.IMWRITE_JPEG_QUALITY), args.quality])
            index += 1
    finally:
        cap.release()

    written = index - FIRST_INDEX
    n_actions = count_actions(actions_path)
    print(f"✓ 解出 {written} 張影格到 {frames_dir}")
    if n_actions is not None and n_actions != written:
        print(f"✗ actions.jsonl 有 {n_actions} 行，卻只解出 {written} 張 —— 對不齊")
        return 1
    return 0


def main(argv):
    parser = argparse.ArgumentParser(
        description="把錄好的 session 在 frames/ 和 frames.mp4 之間轉換",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__)
    sub = parser.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("pack", help="frames/ -> frames.mp4")
    p.add_argument("session")
    p.add_argument("--fps", type=int, default=0, help="預設讀 meta.json 的 sample_rate_hz")
    p.add_argument("--crf", type=int, default=20,
                   help="H.264 品質，數字越小越好越大（預設 20，18 幾乎無損）")
    p.add_argument("--encoder", choices=("auto", "ffmpeg", "opencv"), default="auto")
    p.add_argument("--delete-frames", action="store_true",
                   help="驗證通過後刪掉 frames/（預設保留）")
    p.set_defaults(func=cmd_pack)

    p = sub.add_parser("verify", help="檢查影片跟原始幀 / actions 對不對得上")
    p.add_argument("session")
    p.set_defaults(func=cmd_verify)

    p = sub.add_parser("unpack", help="frames.mp4 -> frames/")
    p.add_argument("session")
    p.add_argument("--quality", type=int, default=92, help="輸出 JPEG 品質（預設 92）")
    p.add_argument("--force", action="store_true", help="覆蓋已存在的 frames/")
    p.set_defaults(func=cmd_unpack)

    args = parser.parse_args(argv[1:])
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
