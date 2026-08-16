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

# 讓模型常駐記憶體。Ollama 預設 5 分鐘沒用就卸載，
# 下次請求要重新載入（實測約 12 秒）。Bot 是持續運轉的，不該付這個成本。
KEEP_ALIVE = os.getenv("BOT_KEEP_ALIVE", "-1")


class DecisionRequest(BaseModel):
    image_base64: str
    last_action: str = ""
    last_result: str = ""


@app.post("/decide")
def decide(request: DecisionRequest):
    print(f"\n📥 [系統提示] 收到 C# 圖片，大小約 {len(request.image_base64)} 字元，送往 {MODEL}...")

    # 只寫「內容」，不要自己加聊天模板。
    # Ollama 會依模型的 Modelfile 套上正確的模板；
    # 舊版手動塞了 LLaVA 的 "USER: <image> ... ASSISTANT:"，
    # 換成 qwen2.5vl / gemma3 等模型時會變成雙重包裝，反而干擾模型。
    # ACTION 必須是 Program.cs 的 switch 認得的固定指令，
    # 不然 AI 自己造詞（"move_cursor"、"Wait"）永遠對不上，會被當成 IDLE 忽略。
    prompt = (
        f"Analyze this game screen. The last key/action sent to the game was: {request.last_result}.\n"
        "This only tells you which key was pressed, not whether it worked - "
        "judge success or failure yourself from the image.\n"
        "Reply using this exact template, nothing else:\n"
        "THOUGHT: (what you see on screen, one short sentence)\n"
        "ACTION: (exactly ONE word from this list: "
        "MOVE_FORWARD, TURN_LEFT, TURN_RIGHT, JUMP, ATTACK, TALK_TO_NPC, SWITCH_CHANNEL, RECONNECT, IDLE)"
    )

    try:
        ollama_response = requests.post(
            f"{OLLAMA_URL}/api/generate",
            json={
                "model": MODEL,
                "prompt": prompt,
                "images": [request.image_base64],
                # 💥 核心修正：移除 "format": "json"，徹底解放 AI 的嘴巴！
                # temperature 要放在 options 裡，放最外層 Ollama 會直接忽略掉。
                "options": {
                    "temperature": 0.2,
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
            print(f"❌ [錯誤] Ollama 服務異常，狀態碼: {ollama_response.status_code}")
            return {"thought": f"Ollama service error: {ollama_response.status_code}", "action": "IDLE"}

        generated_text = ollama_response.json().get("response", "").strip()
        
        # 🔍 終端機大亮點：這行能讓我們看見 AI 到底有沒有吐字！
        print("-" * 40)
        print(f"🤖 [AI 原始純文字回覆]:\n{generated_text}")
        print("-" * 40)
        
        # 🛠️ 用正則表達式解析 THOUGHT 與 ACTION
        thought = ""
        action = ""
        
        thought_match = re.search(r"THOUGHT:\s*(.*)", generated_text, re.IGNORECASE)
        if thought_match:
            thought = thought_match.group(1).split("\n")[0].strip()
            
        action_match = re.search(r"ACTION:\s*(.*)", generated_text, re.IGNORECASE)
        if action_match:
            action = action_match.group(1).split("\n")[0].strip()
            
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