import os
import re

import requests
from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI()

# ── 可調參數（用環境變數覆寫，不必改程式碼）────────────────────────
# 換模型只要設 BOT_MODEL，例如： $env:BOT_MODEL="qwen2.5vl:3b"
OLLAMA_URL = os.getenv("OLLAMA_URL", "http://localhost:11434")
MODEL = os.getenv("BOT_MODEL", "qwen2.5vl:3b")

# 生成長度上限。THOUGHT 一句 + ACTION 一個字大約 30~40 token，
# 給 80 已經很寬鬆。不設上限的話模型可能長篇大論，每多 26 個 token 就多花 1 秒。
NUM_PREDICT = int(os.getenv("BOT_NUM_PREDICT", "80"))

# Context window。這是 8GB 顯卡上最關鍵的一個參數。
# Ollama 對 qwen2.5vl:3b 預設開 128000，光 KV cache 就吃掉約 5GB，
# 整個模型變成 8.4GB 塞不進 8GB VRAM，導致 36% 的層被丟到 CPU 跑
# （實測 eval 只剩 30 tok/s，全上 GPU 應有 80~120）。
#
# 這個 Bot 每輪都是獨立請求、沒有對話歷史，用量是：
#   圖片 ~460 token（800px 寬）+ 提示 ~150 + 輸出 ~80 = 不到 700
# 4096 已有五倍餘裕，KV cache 縮到約 150MB，模型就能完全放進 GPU。
NUM_CTX = int(os.getenv("BOT_NUM_CTX", "4096"))

# 取樣參數。原本寫死 temperature=0.2，但實測 qwen2.5vl:3b 在複雜的遊戲畫面上
# 會退化成不斷重複同一個字元（整串 "@@@@@@"）。溫度太低是小模型陷入
# 重複迴圈的典型成因，配合 repeat_penalty 一起調才穩。
# 格式遵循（THOUGHT/ACTION）在 0.5 附近仍然可靠。
TEMPERATURE = float(os.getenv("BOT_TEMPERATURE", "0.5"))
REPEAT_PENALTY = float(os.getenv("BOT_REPEAT_PENALTY", "1.15"))


def looks_degenerate(text: str) -> bool:
    """偵測模型是否陷入重複迴圈。

    典型症狀是同一個字元連續出現一長串。這種回覆解析出來是空的，
    會被當成 IDLE 靜靜吞掉，看起來像「AI 決定不動」，
    實際上是模型壞掉了 —— 必須明確區分這兩種情況。
    """
    stripped = "".join(text.split())
    if len(stripped) < 12:
        return False

    # 連續 12 個以上相同字元
    run = 1
    for prev, cur in zip(stripped, stripped[1:]):
        run = run + 1 if cur == prev else 1
        if run >= 12:
            return True

    # 整段只用了極少數不同字元
    return len(set(stripped)) <= 2

# 讓模型常駐記憶體。Ollama 預設 5 分鐘沒用就卸載，
# 下次請求要重新載入（實測約 12 秒）。Bot 是持續運轉的，不該付這個成本。
#
# 型別很重要：Ollama 的 keep_alive 只吃「數字（秒，-1 = 永久）」或
# 「帶單位的時間字串（"5m"、"1h"）」。送出沒有單位的字串 "-1" 會讓
# Go 的 duration parser 解析失敗，整個請求回 400。
def _parse_keep_alive(raw: str):
    try:
        return int(raw)      # 純數字 -> 當成秒數送出（-1 代表永久常駐）
    except ValueError:
        return raw           # 例如 "10m"、"1h"，交給 Ollama 自己解析


KEEP_ALIVE = _parse_keep_alive(os.getenv("BOT_KEEP_ALIVE", "-1"))


ACTION_LIST = (
    "MOVE_FORWARD, TURN_LEFT, TURN_RIGHT, JUMP, ATTACK, "
    "TALK_TO_NPC, SWITCH_CHANNEL, RECONNECT, IDLE"
)

VALID_ACTIONS = {a.strip() for a in ACTION_LIST.split(",")}


def normalise_action(raw: str) -> str:
    """把模型吐出來的動作整理成 C# 動作表認得的字串。

    模型很常在後面多加標點（"IDLE."）或補上說明
    （"MOVE_FORWARD (to reach the NPC)"）。這些都對不上動作表，
    C# 端會整個當成未知動作丟掉 —— 一個原本有效的決策就這樣消失。
    """
    if not raw:
        return ""

    # 先取第一個詞，把 "MOVE_FORWARD (to reach the NPC)" 的說明切掉
    first = raw.strip().upper().split()[0] if raw.strip() else ""
    cleaned = re.sub(r"[^A-Z_]", "", first)  # 去掉句點、逗號等標點
    if cleaned in VALID_ACTIONS:
        return cleaned

    # 退一步：整句裡找得到哪個合法動作就用它
    upper = raw.upper()
    for action in VALID_ACTIONS:
        if action in upper:
            return action

    return ""


class DecisionRequest(BaseModel):
    # images 是多影格版本（由舊到新）。image_base64 是單張的舊欄位，
    # 兩個都留著，C# 端不論新舊版本都能對接。
    images: list[str] = []
    image_base64: str = ""
    last_action: str = ""
    last_result: str = ""

    def frames(self) -> list[str]:
        if self.images:
            return self.images
        return [self.image_base64] if self.image_base64 else []


def build_prompt(frame_count: int, last_result: str) -> str:
    """組出提示詞。單張和多張的措辭必須不同 —— 只有一張畫面時
    叫模型「比較各影格」只會讓它憑空捏造變化。"""

    # 只寫「內容」，不要自己加聊天模板。
    # Ollama 會依模型的 Modelfile 套上正確的模板；
    # 舊版手動塞了 LLaVA 的 "USER: <image> ... ASSISTANT:"，
    # 換成 qwen2.5vl / gemma3 等模型時會變成雙重包裝，反而干擾模型。
    if frame_count > 1:
        head = (
            f"You are given {frame_count} consecutive frames from a game, oldest first, "
            "about one second apart. The LAST image is the current screen.\n"
            f"The last key/action sent to the game was: {last_result}.\n"
            "Compare the frames to judge whether that action actually worked - "
            "whether the player moved, is stuck against an obstacle, took damage, "
            "or nothing changed. Base your decision on the current (last) frame.\n"
        )
    else:
        head = (
            f"Analyze this game screen. The last key/action sent to the game was: {last_result}.\n"
            "This only tells you which key was pressed, not whether it worked - "
            "judge success or failure yourself from the image.\n"
        )

    # ACTION 必須是 Program.cs 的對應表認得的固定指令，
    # 不然 AI 自己造詞（"move_cursor"、"Wait"）永遠對不上，會被當成 IDLE 忽略。
    return (
        head
        + "Reply using this exact template, nothing else:\n"
        "THOUGHT: (what you see, one short sentence)\n"
        f"ACTION: (exactly ONE word from this list: {ACTION_LIST})"
    )


@app.post("/decide")
def decide(request: DecisionRequest):
    frames = request.frames()
    if not frames:
        print("❌ [錯誤] 請求沒有夾帶任何影像")
        return {"thought": "No image supplied", "action": "IDLE"}

    total = sum(len(f) for f in frames)
    print(f"\n📥 [系統提示] 收到 C# {len(frames)} 張影格，共約 {total} 字元，送往 {MODEL}...")

    prompt = build_prompt(len(frames), request.last_result)

    try:
        ollama_response = requests.post(
            f"{OLLAMA_URL}/api/generate",
            json={
                "model": MODEL,
                "prompt": prompt,
                "images": frames,
                # 💥 核心修正：移除 "format": "json"，徹底解放 AI 的嘴巴！
                # temperature 要放在 options 裡，放最外層 Ollama 會直接忽略掉。
                "options": {
                    "temperature": TEMPERATURE,
                    "repeat_penalty": REPEAT_PENALTY,
                    "num_predict": NUM_PREDICT,
                    "num_ctx": NUM_CTX,
                },
                "keep_alive": KEEP_ALIVE,
                "stream": False
            },
            timeout=60
        )
        
        if ollama_response.status_code != 200:
            # 注意：這裡不能用 raise HTTPException，因為下面的 except Exception
            # 會把它當成一般錯誤吃掉，永遠不會真的以 HTTP 500 回給 C# 端，
            # 反而會讓 C# 誤判成功。直接回傳一個安全的 fallback 決策即可。
            # 一定要把回應內容印出來。只印狀態碼的話，像 400 這種「請求本身有問題」
            # 的錯誤完全看不出是哪個欄位不合法，只能瞎猜。
            detail = ollama_response.text.strip()
            print(f"❌ [錯誤] Ollama 服務異常，狀態碼: {ollama_response.status_code}")
            print(f"   回應內容: {detail}")
            return {
                "thought": f"Ollama service error {ollama_response.status_code}: {detail}",
                "action": "IDLE",
            }

        generated_text = ollama_response.json().get("response", "").strip()

        # 🔍 終端機大亮點：這行能讓我們看見 AI 到底有沒有吐字！
        print("-" * 40)
        print(f"🤖 [AI 原始純文字回覆]:\n{generated_text}")
        print("-" * 40)

        # 模型陷入重複迴圈時，正則解析不到 THOUGHT/ACTION，結果會被安全網
        # 當成一般的 IDLE 送回去 —— 看起來像「AI 決定不動」，其實是模型壞了。
        # 明確標記出來，才不會把故障誤讀成決策。
        if looks_degenerate(generated_text):
            print("⚠️  [警告] 模型輸出退化成重複字元，這不是有效決策。")
            print(f"   目前 temperature={TEMPERATURE}, repeat_penalty={REPEAT_PENALTY}")
            print("   可調高 BOT_TEMPERATURE（例如 0.7）或縮小 BOT_MAX_IMAGE_WIDTH 再試。")
            return {
                "thought": "MODEL_DEGENERATED: repeated-token loop, not a real decision",
                "action": "IDLE",
            }

        # 🛠️ 用正則表達式解析 THOUGHT 與 ACTION
        thought = ""
        action = ""
        
        thought_match = re.search(r"THOUGHT:\s*(.*)", generated_text, re.IGNORECASE)
        if thought_match:
            thought = thought_match.group(1).split("\n")[0].strip()
            
        action_match = re.search(r"ACTION:\s*(.*)", generated_text, re.IGNORECASE)
        if action_match:
            raw_action = action_match.group(1).split("\n")[0].strip()
            action = normalise_action(raw_action)
            if not action:
                print(f"⚠️  [警告] 無法對應的動作 {raw_action!r}，改用 IDLE")
                action = "IDLE"
            elif action != raw_action:
                # 例如 "IDLE." -> "IDLE"。留一行紀錄，之後才看得出模型的輸出習慣
                print(f"ℹ️  [提示] 動作已正規化: {raw_action!r} -> {action}")

        # 安全網：如果 AI 真的沒按格式來，就把整段話當作 thought，至少不留白
        if not thought and not action:
            thought = generated_text
            action = "IDLE"

    except Exception as e:
        print(f"❌ [錯誤] 呼叫或解析 Ollama 失敗: {str(e)}")
        thought, action = f"Error: {str(e)}", "IDLE"
        
    print(f"🚀 [傳送決策給 C#] 思考: {thought} | 動作: {action}")
    return {"thought": thought, "action": action}


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)