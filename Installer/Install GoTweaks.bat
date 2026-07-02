@echo off
REM GoTweaks Lite installer launcher.
REM Double-click this file to install. It runs the PowerShell installer
REM next to it (which will request administrator rights via one UAC prompt).
setlocal
set "SCRIPT=%~dp0Install GoTweaks.ps1"

where pwsh >nul 2>nul
if %ERRORLEVEL%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
)

endlocal
