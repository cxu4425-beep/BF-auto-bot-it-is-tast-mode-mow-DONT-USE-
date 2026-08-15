from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import requests
import json

app = FastAPI()

class DecisionRequest(BaseModel):
    image_base64: str
    last_result: str

@app.post("/decide")
def decide(request: DecisionRequest):
    prompt = f"Analyze this game screenshot and make a decision based on the last result: {request.last_result}. Respond with a JSON object containing exactly two keys: 'thought' (string) and 'action' (string)."
    
    try:
        ollama_response = requests.post(
            "http://localhost:11434/api/generate",
            json={
                "model": "llava:7b",
                "prompt": prompt,
                "images": [request.image_base64],
                "format": "json",
                # 沒有 stream=False 的話 Ollama 會逐 token 回傳 NDJSON，
                # 整個 body 不是單一 JSON 物件，下面的 .json() 一定會拋例外。
                "stream": False,
                # temperature 必須放在 options 裡，放在最外層 Ollama 會直接忽略。
                "options": {"temperature": 0.4},
            },
            timeout=60,
        )
    except requests.RequestException as e:
        raise HTTPException(status_code=503, detail=f"無法連線到 Ollama: {e}")

    if ollama_response.status_code != 200:
        raise HTTPException(
            status_code=502,
            detail=f"Ollama API error {ollama_response.status_code}: {ollama_response.text}",
        )

    try:
        generated_text = ollama_response.json().get("response", "")
        decision = json.loads(generated_text)
        thought = str(decision.get("thought", ""))
        action = str(decision.get("action", "")).strip().upper()
    except (ValueError, AttributeError) as e:
        # 原本這裡把 thought/action 都吞成空字串，C# 端會收到一個看起來成功、
        # 實際上沒有動作的回覆。回報錯誤並給明確的 IDLE 才不會誤導呼叫端。
        print(f"[錯誤] 解析 Ollama 回覆失敗: {e}")
        thought = f"Failed to parse model output: {e}"
        action = "IDLE"

    if not action:
        action = "IDLE"

    return {"thought": thought, "action": action}