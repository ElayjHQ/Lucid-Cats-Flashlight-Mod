@echo off
title Lucid Cats Flashlight - Installer
echo.
echo This will install the Flashlight mod for Lucid Cats.
echo It copies a few files into the game folder. Your game stays untouched otherwise.
echo.
pause
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Flashlight.ps1"
echo.
pause
