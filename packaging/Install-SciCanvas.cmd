@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SciCanvas.ps1" %*
exit /b %errorlevel%
