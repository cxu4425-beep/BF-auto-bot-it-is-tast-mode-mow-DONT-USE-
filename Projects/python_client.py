import json
import sys
import time
import random
import struct

# Named Pipe client for Windows
# This script simulates the Python AI processing and sends back decisions.

def send_message(pipe, message):
    message_bytes = message.encode('utf-8')
    # NamedPipeServerStream in C# is configured for PipeTransmissionMode.Byte
    # So we just send the raw bytes.
    pipe.write(message_bytes)
    pipe.flush()
    print(f"[Python] Sent: {message}")

def receive_message(pipe):
    # Read until a newline or specific delimiter if C# sends one.
    # For now, assuming C# sends a screenshot path followed by a newline.
    # Or, if C# sends a fixed-size message, we'd read that many bytes.
    # Since C# sends a path, we'll read until a newline.
    buffer = b''
    while True:
        byte = pipe.read(1)
        if not byte:
            break # Pipe closed
        buffer += byte
        if buffer.endswith(b'\n'): # Assuming C# sends newline after path
            break
    return buffer.decode('utf-8').strip()

def main():
    pipe_name = r'\\.\pipe\BloxFruitsPipe'
    print(f"[Python] Connecting to Named Pipe: {pipe_name}")

    try:
        # Open the pipe in binary mode for reading and writing
        pipe = open(pipe_name, 'r+b', 0) # 0 for unbuffered
        print("[Python] Connected to C# server.")

        while True:
            # Simulate receiving screenshot path from C#
            print("[Python] Waiting for screenshot path from C#...")
            screenshot_path = receive_message(pipe)
            if not screenshot_path:
                print("[Python] C# disconnected.")
                break
            print(f"[Python] Received screenshot path: {screenshot_path}")

            # Simulate AI decision making
            decision_intents = ["InteractWithNPC", "CombatMode", "Navigate"]
            decision = {
                "HpRatio": round(random.uniform(0.5, 1.0), 2),
                "EnergyRatio": round(random.uniform(0.3, 1.0), 2),
                "TargetVisible": random.choice([True, False]),
                "TargetId": "Target_Monster" if random.choice([True, False]) else "",
                "DecisionIntent": random.choice(decision_intents)
            }
            json_decision = json.dumps(decision)

            # Send decision back to C#
            send_message(pipe, json_decision + '\n') # Add newline for C# to read line by line
            time.sleep(random.uniform(1, 3)) # Simulate processing time

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
