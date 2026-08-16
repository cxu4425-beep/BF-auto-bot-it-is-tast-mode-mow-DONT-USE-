@echo off
title Blox Fruits Bot Launcher
color 0A

echo ============================================
echo    Blox Fruits Bot Launcher (Ollama ReAct)
echo ============================================
echo.

set "BOT_DIR=%~dp0"

REM ─── 調校參數（子程序會繼承這些環境變數）────────────────────────

REM 視覺模型。qwen2.5vl:3b 是為 GUI/螢幕代理訓練的，約 3.2GB，
REM 在 8GB 顯卡上留得下空間給遊戲本身。要換模型改這一行即可
REM （main.py 與 fastapi_server.py 都讀 BOT_MODEL 這個環境變數）。
set "OLLAMA_MODEL=qwen2.5vl:3b"
set "BOT_MODEL=%OLLAMA_MODEL%"

REM 每輪之間的額外延遲（毫秒）。注意這是「加」在推論時間上的，不是週期目標：
REM 一輪的總時間 = 推論 + 這個值。實測 8GB 顯卡上推論約 1.8 秒，
REM 所以這裡設小一點就好，設 2000 會讓每輪拖到近 4 秒，反應太鈍。
set "BOT_LOOP_DELAY_MS=200"

REM 送給模型前的截圖寬度上限。這是推論速度最大的槓桿：
REM 1920x1080 約 2600 個 vision token，800 寬只剩約 460 個。
REM 嫌慢可以再降到 640。
set "BOT_MAX_IMAGE_WIDTH=800"

REM Context window。預設的 128000 會讓 KV cache 吃掉約 5GB，
REM 模型撐到 8.4GB 而塞不進 8GB 顯卡，導致部分層跑在 CPU 上（實測慢 3 倍）。
set "BOT_NUM_CTX=4096"

REM 遊戲視窗標題（模糊比對，不分大小寫）。
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
