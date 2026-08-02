from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import requests
import re

app = FastAPI()

class DecisionRequest(BaseModel):
    image_base64: str
    last_result: str

@app.post("/decide")
def decide(request: DecisionRequest):
    print(f"\n📥 [系統提示] 收到 C# 圖片，大小約 {len(request.image_base64)} 字元，準備送往 Ollama...")
    
    # 💡 使用 LLaVA 最標準的對話模版，並用大寫標籤規範輸出
    # 重要：ACTION 必須是 C# 端 Program.cs 的 switch 認得的固定指令之一，
    # 不然 AI 自己亂造詞（例如 "move_cursor"、"Wait"）永遠對不上，會被當成 IDLE 忽略掉。
    prompt = (
        "A chat between a curious user and an artificial intelligence assistant. "
        "The assistant gives helpful, detailed, and polite answers to the user's questions.\n"
        "USER: <image>\n"
        f"Analyze this game screen carefully. The last key/action sent to the game was: {request.last_result}.\n"
        "This only tells you which key was pressed, not whether it worked - judge success or failure yourself from the image.\n"
        "You must reply using this exact template format:\n"
        "THOUGHT: (Describe what you see on the screen in one short sentence)\n"
        "ACTION: (Choose exactly ONE word from this list, nothing else: "
        "MOVE_FORWARD, TURN_LEFT, TURN_RIGHT, JUMP, ATTACK, TALK_TO_NPC, SWITCH_CHANNEL, RECONNECT, IDLE)\n\n"
        "ASSISTANT:"
    )
    
    try:
        ollama_response = requests.post(
            "http://localhost:11434/api/generate",
            json={
                "model": "llava:7b",
                "prompt": prompt,
                "images": [request.image_base64],
                # 💥 核心修正：移除 "format": "json"，徹底解放 AI 的嘴巴！
                "temperature": 0.2, 
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