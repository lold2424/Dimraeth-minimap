@echo off
setlocal
cd /d "%~dp0"

rem Dimraeth Minimap installer. Finds the game through Steam and copies the mod into it.
rem The real work is in install.ps1 next to this file - open it in Notepad to see what it does.

if not exist "%~dp0install.ps1" (
    echo install.ps1 is missing. Extract the whole zip first, then run install.bat again.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
