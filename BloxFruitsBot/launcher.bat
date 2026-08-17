@echo off
title Blox Fruits Bot Launcher
color 0A

echo ============================================
echo    Blox Fruits Bot Launcher (Ollama ReAct)
echo ============================================
echo.

set "BOT_DIR=%~dp0"

REM --- Tuning (child processes inherit these) ------------------------
REM Keep this file pure ASCII. Batch files are read using the console's
REM OEM codepage, so UTF-8 comments arrive as mojibake, and any byte that
REM lands on an ampersand is treated as a command separator. REM does not
REM protect against that, so cmd ends up executing the garbage.

REM Vision model. qwen2.5vl:3b is trained for GUI/screen-agent work and is
REM about 3.2GB, which leaves room on an 8GB card for the game itself.
REM Change this one line to switch models - main.py and fastapi_server.py
REM both read BOT_MODEL.
set "OLLAMA_MODEL=qwen2.5vl:3b"
set "BOT_MODEL=%OLLAMA_MODEL%"

REM Extra delay between rounds, in ms. This is ADDED to inference time, it
REM is not a target period: one round = inference + this value. Inference
REM measured about 1.8s on an 8GB card, so keep this small. 2000 would
REM stretch each round to nearly 4 seconds, which is too sluggish.
set "BOT_LOOP_DELAY_MS=200"

REM Screenshot width cap before the frame is sent to the model. This is the
REM single biggest lever on speed: 1920x1080 is roughly 2600 vision tokens,
REM 800 wide is about 460. Drop to 640 if it still feels slow.
set "BOT_MAX_IMAGE_WIDTH=800"

REM Context window. The 128000 default makes the KV cache eat around 5GB,
REM pushing the model to 8.4GB so it no longer fits in 8GB of VRAM and part
REM of it runs on CPU - measured 3x slower.
set "BOT_NUM_CTX=4096"

REM Game window title (case-insensitive substring match).
set "BOT_WINDOW_TITLE=Roblox"

echo [1/4] Checking Python...
where python >nul 2>nul
if errorlevel 1 goto NO_PYTHON
echo [OK] Python found
goto CHECK_OLLAMA

:NO_PYTHON
echo [ERROR] Python not found. Please install Python 3.x first.
pause
exit /b 1

:CHECK_OLLAMA
echo.
echo [2/4] Checking Ollama (needs vision model %OLLAMA_MODEL%)...
where ollama >nul 2>nul
if errorlevel 1 goto NO_OLLAMA

powershell -Command "$t = New-Object System.Net.Sockets.TcpClient; try { $t.Connect('127.0.0.1', 11434); exit 0 } catch { exit 1 }" >nul 2>nul
if %errorlevel% equ 0 goto OLLAMA_RUNNING

echo [INFO] Starting Ollama service...
start "Ollama Server" /B ollama serve
timeout /t 5 /nobreak >nul

:OLLAMA_RUNNING
echo [INFO] Checking model: %OLLAMA_MODEL%
ollama list | findstr "%OLLAMA_MODEL%" >nul 2>nul
if %errorlevel% equ 0 goto OLLAMA_READY

echo [INFO] Pulling model %OLLAMA_MODEL% (first time may take a while)...
ollama pull %OLLAMA_MODEL%

:OLLAMA_READY
echo [OK] Ollama ready
goto START_API

:NO_OLLAMA
echo [ERROR] Ollama not found. This bot needs Ollama with the %OLLAMA_MODEL% vision model.
echo         Install from https://ollama.com then run: ollama pull %OLLAMA_MODEL%
pause
exit /b 1

:START_API
echo.
echo [3/4] Starting Python FastAPI AI server (main.py, port 8000)...
start "BloxFruitsBot - AI Server" "%BOT_DIR%_run_ai_server.bat"
echo [OK] AI server starting...
echo [INFO] Waiting 5 seconds for FastAPI server to initialize...
timeout /t 5 /nobreak >nul
goto START_CS

:START_CS
echo.
echo [4/4] Building and starting C# app (Program.cs runs the loop automatically)...
where dotnet >nul 2>nul
if errorlevel 1 goto NO_DOTNET

start "BloxFruitsBot - C# Bot" "%BOT_DIR%_run_csharp.bat"
echo [OK] C# app starting (it begins the automation loop immediately, no button to click)...
goto DONE

:NO_DOTNET
echo [ERROR] dotnet SDK not found. Please install .NET 8.0 SDK first.
pause
exit /b 1

:DONE
echo.
echo ============================================
echo    All components started!
echo    - AI decision server: http://localhost:8000/decide
echo    - C# Bot: auto screenshot, ask AI, press keys, fully automatic
echo    - If the C# window keeps showing connection errors,
echo      check the AI Server window for Python errors
echo ============================================
echo.
echo Press any key to close this launcher window (the bot keeps running)
pause >nul
