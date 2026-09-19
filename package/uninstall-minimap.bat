@echo off
setlocal
cd /d "%~dp0"

rem Removes Dimraeth Minimap and the BepInEx mod loader. Game files are not touched.

if not exist "Dimraeth.exe" (
    echo This file must be in the Dimraeth game folder ^(next to Dimraeth.exe^).
    echo Nothing was removed.
    pause
    exit /b 1
)

tasklist /fi "imagename eq Dimraeth.exe" 2>nul | find /i "Dimraeth.exe" >nul
if not errorlevel 1 (
    echo Please close Dimraeth first, then run this again.
    pause
    exit /b 1
)

echo This will remove the minimap mod and BepInEx from:
echo   %cd%
choice /c YN /m "Continue"
if errorlevel 2 exit /b 0

if exist "BepInEx" rmdir /s /q "BepInEx"
if exist "dotnet" rmdir /s /q "dotnet"
if exist "winhttp.dll" del /q "winhttp.dll"
if exist "doorstop_config.ini" del /q "doorstop_config.ini"
if exist ".doorstop_version" del /q ".doorstop_version"
if exist "changelog.txt" del /q "changelog.txt"
if exist "README-minimap.txt" del /q "README-minimap.txt"
if exist "licenses-minimap" rmdir /s /q "licenses-minimap"

echo Done. The game is back to its original state.
pause
(goto) 2>nul & del "%~f0"
