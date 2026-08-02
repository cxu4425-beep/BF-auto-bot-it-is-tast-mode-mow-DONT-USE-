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
    
    ollama_response = requests.post(
        "http://localhost:11434/api/generate",
        json={
            "model": "llava:7b",
            "prompt": prompt,
            "images": [request.image_base64],
            "format": "json",
            "temperature": 0.4
        }
    )
    
    if ollama_response.status_code != 200:
        raise HTTPException(status_code=500, detail="Ollama API error")
    
    try:
        generated_text = ollama_response.json().get("response", "")
        decision = json.loads(generated_text)
        thought = decision.get("thought", "")
        action = decision.get("action", "")
    except Exception as e:
        thought = ""
        action = ""
    
    return {"thought": thought, "action": action}