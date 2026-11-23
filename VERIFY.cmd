@echo off
chcp 65001 >nul
cd /d "%~dp0"
dotnet build SysAdminAssistant.sln -c Release -m:1 -nr:false
if not "%errorlevel%"=="0" goto failed
dotnet run --project tests\SysAdminAssistant.Specs\SysAdminAssistant.Specs.csproj -c Release --no-build
if not "%errorlevel%"=="0" goto failed
dotnet run --project tests\SysAdminAssistant.NetworkSpecs\SysAdminAssistant.NetworkSpecs.csproj -c Release --no-build
if not "%errorlevel%"=="0" goto failed
dotnet run --project tests\SysAdminAssistant.ServerMigrationSpecs\SysAdminAssistant.ServerMigrationSpecs.csproj -c Release
if not "%errorlevel%"=="0" goto failed
dotnet run --project tests\SysAdminAssistant.WindowsUiSpecs\SysAdminAssistant.WindowsUiSpecs.csproj -c Release --no-build
if not "%errorlevel%"=="0" goto failed
echo Проверки и сборка прошли.
if /i not "%~1"=="--no-pause" pause
exit /b 0
:failed
echo Проверки или сборка не прошли. Скопируйте сообщения ошибок.
if /i not "%~1"=="--no-pause" pause
exit /b 1
