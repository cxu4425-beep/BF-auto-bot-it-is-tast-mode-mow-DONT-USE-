"""
替代版 AI 決策伺服器：使用 ollama Python 套件 + JSON 模式。

與 main.py 的差別：
  - main.py  : 走 requests + 自由文字，用正則解析 THOUGHT / ACTION（launcher.bat 預設啟動這支）
  - 本檔案   : 走 ollama 套件 + format="json"，由模型直接吐 JSON

兩者對 C# 端 (Program.cs) 的介面完全相同：
  POST /decide  {image_base64, last_action, last_result} -> {thought, action}
"""

import json

from fastapi import FastAPI
from pydantic import BaseModel

import ollama

app = FastAPI()

# C# 端 Program.cs 的 DecisionRequest 送出的欄位名是 image_base64 / last_action / last_result，
# 欄位名對不上 FastAPI 會直接回 422，AI 永遠收不到圖。
class DecisionRequest(BaseModel):
    image_base64: str
    last_action: str = ""
    last_result: str = ""


class DecisionResponse(BaseModel):
    thought: str
    action: str


# ACTION 必須是 Program.cs 的 switch 認得的指令，否則會被當成 IDLE 忽略掉
VALID_ACTIONS = {
    "MOVE_FORWARD",
    "TURN_LEFT",
    "TURN_RIGHT",
    "JUMP",
    "ATTACK",
    "TALK_TO_NPC",
    "SWITCH_CHANNEL",
    "RECONNECT",
    "IDLE",
}

SYSTEM_PROMPT = """
You are an advanced AI Game Bot Controller using ReAct (Reasoning + Acting) logic.
Your goal is to analyze the game screenshot and the previous action's result to decide the next best move.

RULES:
1. You MUST respond ONLY in valid JSON format.
2. Your JSON must contain exactly two keys: "thought" and "action".
3. "thought": A detailed reasoning process. Analyze the visual elements (HP, enemies, obstacles) and the
   feedback from the previous action. If the previous action failed (e.g., "hit a wall"), explain why and
   how to fix it.
4. "action": One of the following commands exactly as written:
   [MOVE_FORWARD, TURN_LEFT, TURN_RIGHT, JUMP, ATTACK, TALK_TO_NPC, SWITCH_CHANNEL, RECONNECT, IDLE].
5. If you are stuck, suggest a corrective action like "TURN_LEFT" or "JUMP".

JSON FORMAT EXAMPLE:
{
  "thought": "I see an NPC in front of me, but my previous 'MOVE_FORWARD' failed because I hit an obstacle. I need to turn left first.",
  "action": "TURN_LEFT"
}
"""


@app.post("/decide", response_model=DecisionResponse)
def decide(request: DecisionRequest):
    prompt = (
        f"Current Game State Feedback: last action was {request.last_action!r}, "
        f"result was {request.last_result!r}.\n"
        "Analyze the image and decide the next action."
    )

    try:
        response = ollama.generate(
            model="llava:7b",
            prompt=prompt,
            system=SYSTEM_PROMPT,
            images=[request.image_base64],
            format="json",
            options={"temperature": 0.4, "num_predict": 256},
        )
    except Exception as exc:
        # Ollama 沒開 / 模型沒載 -> 回一個安全的 IDLE，不要讓 C# 端整個迴圈掛掉
        print(f"[錯誤] 呼叫 Ollama 失敗: {exc}")
        return DecisionResponse(thought=f"Ollama call failed: {exc}", action="IDLE")

    raw = (response.get("response") or "").strip()
    print("-" * 40)
    print(f"[AI 原始回覆]:\n{raw}")
    print("-" * 40)

    try:
        decision = json.loads(raw)
        if not isinstance(decision, dict):
            raise ValueError("模型回傳的 JSON 不是物件")
    except (json.JSONDecodeError, ValueError) as exc:
        # 模型沒照格式吐 JSON 時，至少把原文留下來方便除錯
        print(f"[錯誤] 解析模型 JSON 失敗: {exc}")
        return DecisionResponse(thought=raw or f"Invalid JSON from model: {exc}", action="IDLE")

    thought = str(decision.get("thought", "")).strip()
    action = str(decision.get("action", "")).strip().upper()

    if action not in VALID_ACTIONS:
        print(f"[警告] 模型給了未知動作 {action!r}，改用 IDLE")
        thought = f"{thought} (unknown action {action!r} coerced to IDLE)".strip()
        action = "IDLE"

    print(f"[傳送決策給 C#] 思考: {thought} | 動作: {action}")
    return DecisionResponse(thought=thought, action=action)


@app.get("/health")
def health():
    return {"status": "ok"}


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(app, host="0.0.0.0", port=8000)
