import ollama
import base64
import json
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

class DecisionRequest(BaseModel):
    image: str
    last_action: str
    last_result: str

class DecisionResponse(BaseModel):
    thought: str
    action: str

system_prompt = """
You are an advanced AI Game Bot Controller using ReAct (Reasoning + Acting) logic.
Your goal is to analyze the game screenshot and the previous action's result to decide the next best move.

RULES:
1. You MUST respond ONLY in valid JSON format.
2. Your JSON must contain exactly two keys: "thought" and "action".
3. "thought": A detailed reasoning process. Analyze the visual elements (HP, enemies, obstacles) and the feedback from the previous action. If the previous action failed (e.g., "hit a wall"), explain why and how to fix it.
4. "action": One of the following commands: [TALK_TO_NPC, ATTACK, JUMP, TURN_LEFT, TURN_RIGHT, RECONNECT, IDLE, MOVE_FORWARD].
5. If you are stuck, suggest a corrective action like "TURN_LEFT" or "JUMP".

JSON FORMAT EXAMPLE:
{
  "thought": "I see an NPC in front of me, but my previous 'MOVE_FORWARD' failed because I hit an obstacle. I need to turn left first.",
  "action": "TURN_LEFT"
}
"""

@app.post("/decide", response_model=DecisionResponse)
async def decide(request: DecisionRequest):
    try:
        base64_image = request.image
        prompt = f"""
        Current Game State Feedback: {request.last_action} - {request.last_result}
        Analyze the image and decide the next action.
        """

        try:
            response = ollama.generate(
                model="llava:7b",
                prompt=prompt,
                system=system_prompt,
                images=[base64_image],
                format="json",
                options={"temperature": 0.4, "num_predict": 256}
            )
            decision = json.loads(response['response'])
            return decision
        except Exception as e:
            return {
                "thought": f"Error during AI reasoning: {str(e)}",
                "action": "IDLE"
            }
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
</parameter>
<task_progress>
- [x] 了解您的需求
- [x] 讀取並分析 python_client.py 文件
- [x] 增強 OpenCV 血條檢測
- [x] 加上 AI 判別功能 (本地 Ollama)
- [x] 製作一鍵啟動器 (launcher.bat)
- [x] 更新 README.md 說明文件
- [x] 實作 BotCore 接收 AI 按鍵序列邏輯
- [ ] 測試和驗證
- [ ] 實作 Python FastAPI 伺服器與 C# 通訊 (ReAct 架構)
</task_progress>
</write_to_file>