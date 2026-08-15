@echo off
title BloxFruitsBot - AI Server
color 0B
cd /d "%~dp0"

echo ============================================
echo    AI Decision Server (FastAPI + Ollama)
echo ============================================
echo.

echo [1/2] Checking Python dependencies...
python -c "import fastapi, uvicorn, requests" >nul 2>nul
if errorlevel 1 goto INSTALL_DEPS
echo [OK] Dependencies already installed
goto RUN_SERVER

:INSTALL_DEPS
echo [INFO] Installing missing packages, this may take a minute...
if exist "requirements.txt" (
    python -m pip install -r requirements.txt
) else (
    python -m pip install fastapi "uvicorn[standard]" requests
)
if errorlevel 1 goto DEPS_FAILED
echo [OK] Dependencies installed
goto RUN_SERVER

:DEPS_FAILED
echo [ERROR] Failed to install Python dependencies.
echo         Try running manually: python -m pip install -r requirements.txt
pause
exit /b 1

:RUN_SERVER
echo.
echo [2/2] Starting FastAPI on http://localhost:8000/decide ...
echo [INFO] Keep this window open. Close it to stop the AI server.
echo.
python main.py

echo.
echo [INFO] AI server exited. Press any key to close this window.
pause >nul
