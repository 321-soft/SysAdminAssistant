@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Не найден .NET SDK. В Visual Studio Installer добавьте .NET desktop development и .NET 10 SDK.
  pause
  exit /b 1
)
dotnet restore SysAdminAssistant.sln
if not "%errorlevel%"=="0" goto failed
dotnet build SysAdminAssistant.sln -c Release --no-restore
if not "%errorlevel%"=="0" goto failed
echo Сборка завершена. EXE: src\SysAdminAssistant.Desktop\bin\Release\net10.0-windows\SysAdminAssistant.exe
pause
exit /b 0
:failed
echo Сборка остановлена. Скопируйте сообщения ошибок из окна.
pause
exit /b 1
