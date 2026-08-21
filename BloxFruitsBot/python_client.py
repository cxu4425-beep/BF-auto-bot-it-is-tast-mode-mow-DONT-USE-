import json
import sys
import os
import time
import random
import cv2
import numpy as np
import requests
import subprocess

# Named Pipe client for Windows
# This script processes screenshots using OpenCV to detect HP/Energy and sends back decisions.

# AI Configuration (本地 AI 使用 Ollama)
OLLAMA_URL = "http://localhost:11434/api/generate"
OLLAMA_MODEL = "llama3"  # 可更改為其他本地模型

def check_ollama_running():
    """檢查 Ollama 是否在運行"""
    try:
        response = requests.get("http://localhost:11434/api/version", timeout=2)
        return response.status_code == 200
    except:
        return False

def get_ai_decision(hp, energy, target_visible, monster_hp=None):
    """
    使用本地 AI (Ollama) 獲取智能決策
    返回: decision_intent, action_details, key_sequence
    """
    if not check_ollama_running():
        print("[AI] Ollama 未運行，使用規則基決策")
        return get_rule_based_decision(hp, energy, target_visible)
    
    # 構造 AI 提示詞 - 讓 AI 直接輸出具體按鍵序列
    prompt = f"""你是Blox Fruits遊戲的AI控制器，請根據遊戲狀態直接輸出具體的按鍵操作序列。

當前遊戲狀態：
- 玩家血量: {int(hp * 100)}%
- 能量值: {int(energy * 100)}%
- 怪物可見: {'是' if target_visible else '否'}
- 怪物血量: {monster_hp if monster_hp else '未知'}

可用按鍵：
- 移動: W(前進), A(左), S(後退), D(右), Space(跳躍)
- 攻擊: LeftClick(普攻), Z(技能1), X(技能2), C(技能3), V(技能4)
- 變身/技能: 1, 2, 3, 4 (數字鍵)
- 互動: E(拾取/對話), F(互動)
- 特殊: Q(衝刺), R(閃避), Shift(衝刺), Tab(背包), M(地圖)

請根據狀態輸出 JSON 格式：
{{
  "intent": "行動意圖 (如: Combat, Flee, Navigate, Farm, Explore, Interact, Collect)",
  "reason": "決策原因",
  "keys": ["按鍵序列，如: ['W', 'LeftClick', 'Z']"],
  "hold_keys": ["需要長按的鍵，如: ['W']"],
  "duration": 1.0
}}

範例：
- 戰鬥: {{"intent": "Combat", "reason": "怪物在視野內且血量充足", "keys": ["LeftClick", "Z"], "hold_keys": ["W"], "duration": 0.5}}
- 逃跑: {{"intent": "Flee", "reason": "血量過低", "keys": ["S", "Space", "Q"], "hold_keys": ["S"], "duration": 1.0}}
- 導航: {{"intent": "Navigate", "reason": "尋找怪物", "keys": ["W", "Space"], "hold_keys": ["W"], "duration": 1.0}}
- 接任務: {{"intent": "Interact", "reason": "無怪物且需接任務", "keys": ["E", "F"], "hold_keys": [], "duration": 0.5}}

只輸出 JSON，不要其他文字。"""

    try:
        payload = {
            "model": OLLAMA_MODEL,
            "prompt": prompt,
            "stream": False,
            "temperature": 0.3
        }
        response = requests.post(OLLAMA_URL, json=payload, timeout=10)
        
        if response.status_code == 200:
            result = response.json()
            ai_response = result.get("response", "").strip()
            print(f"[AI] AI決策結果: {ai_response}")
            
            # 解析 JSON 回覆
            import json
            try:
                decision_data = json.loads(ai_response)
                intent = decision_data.get("intent", "Navigate")
                reason = decision_data.get("reason", "")
                keys = decision_data.get("keys", [])
                hold_keys = decision_data.get("hold_keys", [])
                duration = decision_data.get("duration", 1.0)
                
                return intent, reason, keys, hold_keys, duration
            except json.JSONDecodeError:
                print("[AI] JSON 解析失敗，使用備用決策")
                return get_rule_based_decision(hp, energy, target_visible)
        else:
            print(f"[AI] API調用失敗: {response.status_code}")
            return get_rule_based_decision(hp, energy, target_visible)
    except Exception as e:
        print(f"[AI] AI決策失敗: {e}")
        return get_rule_based_decision(hp, energy, target_visible)

def get_rule_based_decision(hp, energy, target_visible):
    """基於規則的決策邏輯 (作為AI無法運行時的備用) - 返回具體按鍵序列"""
    
    if hp < 0.3:
        # 血量極低 - 逃跑
        print("🚨 [AI Decision] Critical HP! Fleeing from danger.")
        return "Flee", "血量極低，緊急撤退", ["S", "Space", "Q"], ["S"], 1.5
    elif hp < 0.5:
        # 血量偏低 - 導航躲避
        print("🚨 [AI Decision] Low HP! Prioritizing Navigation/Evading.")
        return "Navigate", "血量偏低，尋找安全位置", ["W", "A", "Space"], ["W"], 1.0
    elif target_visible:
        if energy > 0.7:
            # 發現怪物且能量充足 - 戰鬥
            print("⚔️ [AI Decision] Monster detected! Engaging in combat.")
            return "Combat", "怪物在視野內且能量充足，開始戰鬥", ["LeftClick", "Z", "X"], ["W"], 0.5
        else:
            # 發現怪物但能量不足 - 撤退
            print("🚨 [AI Decision] Monster detected but low energy! Retreating.")
            return "Flee", "發現怪物但能量不足，暫時撤退", ["S", "Space"], ["S"], 1.0
    else:
        if energy > 0.8:
            # 無怪物且能量高 - 探索/刷怪
            print("🔍 [AI Decision] No monster, but high energy! Exploring area.")
            return "Explore", "無怪物且能量充足，探索尋找目標", ["W", "Space", "A", "D"], ["W"], 1.0
        else:
            # 無怪物且能量低 - 接任務/休息
            print("📜 [AI Decision] No monster visible. Going to interact with NPC.")
            return "Interact", "無怪物且能量較低，尋找NPC接任務", ["E", "F"], [], 0.5

def detect_hp_energy(image_path):
    """ Processes the Roblox/Blox Fruits screenshot using OpenCV to detect:
    1. Health Bar (Green) -> HP Ratio
    2. Energy Bar (Cyan/Blue) -> Energy Ratio
    3. Enemy Health Bar (Red/Orange) -> Target Visibility
    Returns: (hp_ratio, energy_ratio, target_visible, debug_image_path) """
    if not os.path.exists(image_path):
        print(f"[OpenCV] Warning: Image path does not exist: {image_path}")
        return None, None, False, None
    img = cv2.imread(image_path)
    if img is None:
        print(f"[OpenCV] Error: Failed to load image at {image_path}")
        return None, None, False, None
    h, w, _ = img.shape
    roi_y1 = int(h * 0.7)
    roi_y2 = h
    roi_x1 = 0
    roi_x2 = int(w * 0.45)
    roi = img[roi_y1:roi_y2, roi_x1:roi_x2]
    hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
    debug_img = img.copy()
    cv2.rectangle(debug_img, (roi_x1, roi_y1), (roi_x2, roi_y2), (255, 255, 0), 2)
    
    # 1. Health Bar (Blood Bar) - Enhanced OpenCV detection
    lower_green1 = np.array([35, 70, 50])
    upper_green1 = np.array([85, 255, 255])
    lower_green2 = np.array([25, 50, 40])
    upper_green2 = np.array([75, 200, 200])
    lower_green3 = np.array([45, 80, 60])
    upper_green3 = np.array([95, 255, 255])
    mask_green1 = cv2.inRange(hsv, lower_green1, upper_green1)
    mask_green2 = cv2.inRange(hsv, lower_green2, upper_green2)
    mask_green3 = cv2.inRange(hsv, lower_green3, upper_green3)
    mask_green = cv2.bitwise_or(mask_green1, mask_green2)
    mask_green = cv2.bitwise_or(mask_green, mask_green3)
    
    # 2. Energy Bar (Cyan / Light Blue) - Enhanced OpenCV detection
    lower_cyan1 = np.array([85, 70, 50])
    upper_cyan1 = np.array([125, 255, 255])
    lower_cyan2 = np.array([75, 60, 40])
    upper_cyan2 = np.array([115, 200, 200])
    lower_cyan3 = np.array([95, 80, 60])
    upper_cyan3 = np.array([135, 255, 255])
    mask_cyan1 = cv2.inRange(hsv, lower_cyan1, upper_cyan1)
    mask_cyan2 = cv2.inRange(hsv, lower_cyan2, upper_cyan2)
    mask_cyan3 = cv2.inRange(hsv, lower_cyan3, upper_cyan3)
    mask_cyan = cv2.bitwise_or(mask_cyan1, mask_cyan2)
    mask_cyan = cv2.bitwise_or(mask_cyan, mask_cyan3)
    
    def get_bar_ratio(mask, color_name, draw_color):
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None
        bar_contours = []
        for c in contours:
            rx, ry, rect_w, rect_h = cv2.boundingRect(c)
            aspect_ratio = float(rect_w) / rect_h if rect_h > 0 else 0
            if rect_w > 25 and rect_h > 4 and aspect_ratio > 3.5:
                bar_contours.append((c, rx, ry, rect_w, rect_h))
        if not bar_contours:
            return None
        # 取最寬的那一條當作血條本體
        bar_contours.sort(key=lambda item: item[3], reverse=True)
        _, rx, ry, rect_w, rect_h = bar_contours[0]
        full_x = roi_x1 + rx
        full_y = roi_y1 + ry
        slice_y = ry + rect_h // 2
        if slice_y >= roi.shape[0]:
            slice_y = roi.shape[0] - 1

        # 從「彩色填滿段的右端」開始往右掃描空槽(暗色)像素。
        # 原本從 rx 開始掃是錯的：rx ~ rx+rect_w 整段都是亮色，
        # 連續 20 個非暗色就 break，根本掃不到後面真正的空槽。
        filled_end = rx + rect_w
        scan_limit = min(rx + int(w * 0.4), roi.shape[1])
        bar_end = filled_end
        consecutive_not_dark = 0
        for k in range(filled_end, scan_limit):
            px_hsv = hsv[slice_y, k]
            s_val, v_val = px_hsv[1], px_hsv[2]
            if v_val < 60 and s_val < 50:  # 暗色 = 血條已被消耗的空槽
                consecutive_not_dark = 0
                bar_end = k + 1
            else:
                consecutive_not_dark += 1
                if consecutive_not_dark > 20:  # 連續 20px 都不是空槽，視為血條到此結束
                    break

        # 比例 = 彩色填滿寬度 / 整條血條總寬度。
        # 原本寫成 float(rx) / total_width：rx 是血條左緣座標、total_width 是絕對 x 座標，
        # 量綱完全對不上。實測（合成血條圖）結果是讀數會「反過來」：
        #   真實血量 100% -> 讀成 20%，60% -> 33%，20% -> 讀成 100%。
        # 也就是滿血時瘋狂逃跑、快死時反而衝上去打，行為完全顛倒。
        full_width = bar_end - rx
        ratio = 1.0 if full_width <= 0 else float(rect_w) / full_width
        ratio = max(0.0, min(1.0, ratio))

        cv2.rectangle(debug_img, (full_x, full_y), (full_x + rect_w, full_y + rect_h), draw_color, 2)
        if full_width > rect_w:
            cv2.rectangle(debug_img, (full_x + rect_w, full_y), (full_x + full_width, full_y + rect_h), (50, 50, 50), 1)
        cv2.putText(debug_img, f"{color_name}: {int(ratio * 100)}%", (full_x, full_y - 5),
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, draw_color, 1)
        return ratio
    
    hp_ratio = get_bar_ratio(mask_green, "HP", (0, 255, 0))
    energy_ratio = get_bar_ratio(mask_cyan, "Energy", (255, 255, 0))
    
    # 3. Target Monster HP Bar Detection (Red/Orange in main play area)
    hsv_full = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
    lower_red1 = np.array([0, 100, 100])
    upper_red1 = np.array([10, 255, 255])
    lower_red2 = np.array([170, 100, 100])
    upper_red2 = np.array([180, 255, 255])
    mask_red1 = cv2.inRange(hsv_full, lower_red1, upper_red1)
    mask_red2 = cv2.inRange(hsv_full, lower_red2, upper_red2)
    mask_red = cv2.bitwise_or(mask_red1, mask_red2)
    red_contours, _ = cv2.findContours(mask_red, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    target_visible = False
    for rc in red_contours:
        rx, ry, rw, rh = cv2.boundingRect(rc)
        if rw > 25 and rh > 3 and float(rw)/rh > 4.0 and ry < int(h * 0.75):
            target_visible = True
            cv2.rectangle(debug_img, (rx, ry), (rx + rw, ry + rh), (0, 0, 255), 2)
            cv2.putText(debug_img, "Monster HP", (rx, ry - 5), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.4, (0, 0, 255), 1)
            break
    
    debug_dir = os.path.dirname(image_path)
    debug_path = os.path.join(debug_dir, "debug_detection.png") if debug_dir else "debug_detection.png"
    cv2.imwrite(debug_path, debug_img)
    return hp_ratio, energy_ratio, target_visible, debug_path

def send_message(pipe, message):
    message_bytes = message.encode('utf-8')
    pipe.write(message_bytes)
    pipe.flush()
    print(f"[Python] Sent: {message}")

def receive_message(pipe):
    buffer = b''
    while True:
        byte = pipe.read(1)
        if not byte:
            break
        buffer += byte
        if buffer.endswith(b'\n'):
            break
    return buffer.decode('utf-8').strip()

def main():
    pipe_name = r'\\.\pipe\BloxFruitsPipe'
    print("====== 🤖 Blox Fruits OpenCV AI Client 啟動 ======")
    print(f"[Python] Connecting to Named Pipe: {pipe_name}")
    try:
        pipe = open(pipe_name, 'r+b', 0)
        print("[Python] Connected to C# server successfully.")
        while True:
            print("\n[Python] Waiting for screenshot path from C#...")
            screenshot_path = receive_message(pipe)
            if not screenshot_path:
                print("[Python] C# disconnected.")
                break
            print(f"[Python] Received screenshot path: {screenshot_path}")
            hp, energy, target_visible, debug_img_path = detect_hp_energy(screenshot_path)
            
            # Fallback / Simulation logic
            if hp is None:
                hp = round(random.uniform(0.7, 1.0), 2)
                print(f"[OpenCV] HP bar not detected. Fallback to simulated value: {hp}")
            else:
                print(f"[OpenCV] Detected Player HP: {int(hp * 100)}%")
            if energy is None:
                energy = round(random.uniform(0.6, 1.0), 2)
                print(f"[OpenCV] Energy bar not detected. Fallback to simulated value: {energy}")
            else:
                print(f"[OpenCV] Detected Player Energy: {int(energy * 100)}%")
            print(f"[OpenCV] Detected Target Monster Visible: {target_visible}")
            if debug_img_path:
                print(f"[OpenCV] Debug visualization saved to: {debug_img_path}")
            
            # 使用 AI 判別 - AI 自己決定要做什麼並輸出按鍵序列
            result = get_ai_decision(hp, energy, target_visible)
            
            # 處理兩種回傳格式
            if len(result) == 5:
                # AI 本地模式回傳: intent, reason, keys, hold_keys, duration
                decision_intent, ai_reasoning, keys, hold_keys, duration = result
            else:
                # 備用模式回傳: intent, reason
                decision_intent, ai_reasoning = result
                keys = []
                hold_keys = []
                duration = 1.0
            
            print(f"[AI] 選擇行動: {decision_intent}")
            print(f"[AI] 原因: {ai_reasoning}")
            print(f"[AI] 按鍵序列: {keys}")
            print(f"[AI] 長按鍵: {hold_keys}")
            print(f"[AI] 持續時間: {duration}s")
            
            decision = {
                "HpRatio": round(hp, 2),
                "EnergyRatio": round(energy, 2),
                "TargetVisible": target_visible,
                "TargetId": "Target_Monster" if target_visible else "",
                "DecisionIntent": decision_intent,
                "AIReasoning": ai_reasoning,
                "ActionKeys": keys,
                "HoldKeys": hold_keys,
                "ActionDuration": duration
            }
            json_decision = json.dumps(decision)
            send_message(pipe, json_decision + '\n')
            time.sleep(1.0)
    except FileNotFoundError:
        print(f"[Python] Error: Named Pipe '{pipe_name}' not found. Make sure C# application is running.")
    except Exception as e:
        print(f"[Python] An error occurred: {e}")
    finally:
        if 'pipe' in locals() and not pipe.closed:
            pipe.close()
        print("[Python] Pipe closed.")

if __name__ == "__main__":
    main()