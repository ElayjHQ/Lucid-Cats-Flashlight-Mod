@echo off
title Lucid Cats Flashlight - Uninstaller
echo.
echo This will remove the Flashlight mod and restore the game to vanilla.
echo.
pause
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-Flashlight.ps1"
echo.
pause
