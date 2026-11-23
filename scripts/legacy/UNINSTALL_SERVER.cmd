@echo off
chcp 65001 >nul
cd /d "%~dp0"
if exist CLEANUP_SERVER.ps1 (
 powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CLEANUP_SERVER.ps1"
 exit /b
)
if exist publish\Release_0.12.2\ServerSetup\CLEANUP_SERVER.ps1 (
 powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish\Release_0.12.2\ServerSetup\CLEANUP_SERVER.ps1"
 exit /b
)
echo Не найден CLEANUP_SERVER.ps1. Распакуйте архив целиком.
pause
exit /b 1
