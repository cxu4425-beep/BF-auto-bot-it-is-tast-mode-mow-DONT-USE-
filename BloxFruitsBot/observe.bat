@echo off
REM Keep this file pure ASCII. Batch files are read in the console OEM
REM codepage, so UTF-8 comments arrive as mojibake and any byte landing on
REM an ampersand becomes a command separator. REM does not protect you.

REM Observation run: watch the AI decide, without letting it touch the game.
REM
REM   observe.bat baseline      - current prompt, no examples
REM   observe.bat fewshot       - same, but with the images in .\fewshot
REM
REM Each run appends to decisions_<tag>.jsonl. Compare two runs with:
REM   python analyze_decisions.py decisions_baseline.jsonl decisions_fewshot.jsonl

setlocal
cd /d "%~dp0"

set "TAG=%~1"
if "%TAG%"=="" set "TAG=baseline"

REM Do not send any keys or mouse clicks, and do not steal window focus.
REM You keep playing; the AI just says what it would have done.
set "BOT_DRY_RUN=1"

set "BOT_RUN_TAG=%TAG%"
set "BOT_DECISION_LOG=decisions_%TAG%.jsonl"
set "BOT_OBSERVE_FRAMES=observe_frames_%TAG%"

if /i "%TAG%"=="fewshot" (
    if not exist "fewshot" (
        echo [ERROR] No .\fewshot folder. Put example .jpg files there, each with
        echo         a matching .txt holding the reply you want the model to copy.
        pause
        exit /b 1
    )
    set "BOT_FEWSHOT_DIR=%CD%\fewshot"
    echo [INFO] Using examples from %CD%\fewshot
    echo [INFO] Restart the AI server ^(main.py^) after changing that folder -
    echo        examples are loaded once at startup.
)

echo.
echo ==========================================================
echo   Observation run: %TAG%
echo   Log    : %BOT_DECISION_LOG%
echo   Frames : %BOT_OBSERVE_FRAMES%
echo   No keys are sent. Play normally and let it watch.
echo   Stop with Ctrl+C after about 5 minutes (150+ steps).
echo ==========================================================
echo.

dotnet run --project BloxFruitsBot.csproj -c Release

echo.
echo Done. Now run:
echo   python analyze_decisions.py %BOT_DECISION_LOG%
pause
