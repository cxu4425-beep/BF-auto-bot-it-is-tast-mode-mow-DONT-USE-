@echo off
title BloxFruitsBot - C# Bot
color 0E
cd /d "%~dp0"

echo ============================================
echo    C# Game Control Client (ReAct loop)
echo ============================================
echo.
echo [INFO] Building and starting BloxFruitsBot (Release)...
echo [INFO] The loop starts automatically - there is no button to click.
echo.

dotnet run --project "BloxFruitsBot.csproj" -c Release
if errorlevel 1 goto RUN_FAILED
goto DONE

:RUN_FAILED
echo.
echo [ERROR] Build or run failed. Common causes:
echo         - .NET 8.0 SDK not installed
echo         - Project must be built on Windows (WinForms + Win32 APIs)
pause
exit /b 1

:DONE
echo.
echo [INFO] Bot exited. Press any key to close this window.
pause >nul
