import ollama
import base64
import json

class GameBrain:
    def __init__(self, model="llava:7b"):
        self.model = model
        self.ollama_url = "http://localhost:11434/api/generate"
        # 定義嚴格的 System Prompt
        self.system_prompt = """
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

    def encode_image(self, image_path):
        with open(image_path, "rb") as image_file:
            return base64.b64encode(image_file.read()).decode('utf-8')

    def get_decision(self, image_path, last_action_feedback):
        """
        image_path: 截圖路徑
        last_action_feedback: 來自 C# 的回報 (例如: "Action 'MOVE_FORWARD' failed: No movement detected")
        """
        base64_image = self.encode_image(image_path)
        
        prompt = f"""
        Current Game State Feedback: {last_action_feedback}
        Analyze the image and decide the next action.
        """

        try:
            response = ollama.generate(
                model=self.model,
                prompt=prompt,
                system=self.system_prompt,
                images=[base64_image],
                format="json",  # 強制 JSON 模式
                options={
                    "temperature": 0.4, # 保留一點變通與推理能力
                    "num_predict": 256
                }
            )
            
            # 解析回傳的 JSON
            decision = json.loads(response['response'])
            return decision
        except Exception as e:
            return {
                "thought": f"Error during AI reasoning: {str(e)}",
                "action": "IDLE"
            }

# --- 測試代碼 ---
if __name__ == "__main__":
    brain = GameBrain()
    # 模擬 C# 傳來的截圖與回報
    test_image = "test_screenshot.png" 
    test_feedback = "Previous action 'ATTACK' failed: No target detected in range."
    
    result = brain.get_decision(test_image, test_feedback)
    print(json.dumps(result, indent=2, ensure_ascii=False))