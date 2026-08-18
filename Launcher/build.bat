@echo off
REM Keep this file pure ASCII - cmd reads batch files in the console OEM
REM codepage, so UTF-8 comments turn into mojibake and any byte landing on
REM an ampersand gets executed as a command.
title Build BloxFruitsLauncher
cd /d "%~dp0"

echo ============================================
echo    Building BloxFruitsLauncher.exe
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto NO_DOTNET

REM Single file, framework-dependent: needs the .NET 8 runtime present, which
REM it already is since the bot itself is built with the SDK. Self-contained
REM would work too but drags in ~70MB of runtime.
dotnet publish Launcher.csproj -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -o publish
if errorlevel 1 goto BUILD_FAILED

echo.
echo [OK] Built: %~dp0publish\BloxFruitsLauncher.exe
echo.
echo The launcher looks for the BloxFruitsBot folder by walking up from its
echo own location, so leave publish\ inside the project, or copy the exe
echo somewhere at or above the BloxFruitsBot folder.
echo.
pause
exit /b 0

:NO_DOTNET
echo [ERROR] dotnet SDK not found. Install the .NET 8 SDK first.
pause
exit /b 1

:BUILD_FAILED
echo.
echo [ERROR] Build failed. The compiler output above says why.
pause
exit /b 1
