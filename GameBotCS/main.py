from fastapi import FastAPI
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

    # 注意：llava 這類視覺模型搭配 "format": "json" 常常會吐出空字串或非合法 JSON，
    # 導致 json.loads 失敗。改用自由文字 + 正則解析 THOUGHT / ACTION 標籤，穩定很多。
    prompt = (
        "A chat between a curious user and an artificial intelligence assistant. "
        "The assistant gives helpful, detailed, and polite answers to the user's questions.\n"
        "USER: <image>\n"
        f"Analyze this game screenshot. The result of the last action was: {request.last_result}.\n"
        "You must reply using this exact template format:\n"
        "THOUGHT: (Describe what you see on the screen in one short sentence)\n"
        "ACTION: (Output a single command describing what to do next)\n\n"
        "ASSISTANT:"
    )

    thought, action = "", "wait"

    try:
        ollama_response = requests.post(
            "http://localhost:11434/api/generate",
            json={
                "model": "llava:7b",
                "prompt": prompt,
                "images": [request.image_base64],
                "temperature": 0.2,
                "stream": False
            },
            timeout=60
        )

        if ollama_response.status_code != 200:
            print(f"❌ [錯誤] Ollama 服務異常，狀態碼: {ollama_response.status_code}, 內容: {ollama_response.text}")
            return {"thought": f"Ollama service error: {ollama_response.status_code}", "action": "wait"}

        generated_text = ollama_response.json().get("response", "").strip()

        print("-" * 40)
        print(f"🤖 [AI 原始純文字回覆]:\n{generated_text}")
        print("-" * 40)

        thought_match = re.search(r"THOUGHT:\s*(.*)", generated_text, re.IGNORECASE)
        if thought_match:
            thought = thought_match.group(1).split("\n")[0].strip()

        action_match = re.search(r"ACTION:\s*(.*)", generated_text, re.IGNORECASE)
        if action_match:
            action = action_match.group(1).split("\n")[0].strip()

        if not thought and not action_match:
            # AI 完全沒照格式回答時，至少把整段話留下來，方便除錯
            thought = generated_text
            action = "wait"

    except Exception as e:
        print(f"❌ [錯誤] 呼叫或解析 Ollama 失敗: {str(e)}")
        thought, action = f"Error: {str(e)}", "wait"

    print(f"🚀 [傳送決策給 C#] 思考: {thought} | 動作: {action}")
    return {"thought": thought, "action": action}


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
