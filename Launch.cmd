@echo off
title WoW Fishbot
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-FishingController.ps1"
if errorlevel 1 (
    echo.
    echo Fishbot stopped because startup or runtime failed. Review the error above.
    pause
)
