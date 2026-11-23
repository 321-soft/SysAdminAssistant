@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "setup_mode="
if /i "%~1"=="--uninstall" set "setup_mode=--uninstall"
if /i "%~1"=="--update" set "setup_mode=--update"
set "runtime_host=%ProgramW6432%\dotnet\dotnet.exe"
if not defined ProgramW6432 set "runtime_host=%ProgramFiles%\dotnet\dotnet.exe"
if not exist SysAdminAssistant.ServerSetup.exe (
 if exist publish\Release_0.40.0\ServerSetup\SETUP_SERVER.cmd (
  call publish\Release_0.40.0\ServerSetup\SETUP_SERVER.cmd %setup_mode%
  exit /b
 )
 echo Сначала выполните PUBLISH.cmd.
 pause
 exit /b 1
)
call :desktop
if errorlevel 1 (
 if not exist "Prerequisites\windowsdesktop-runtime-10.0.14-win-x64.exe" goto missing
 echo Установка .NET Desktop Runtime x64 отдельным пакетом Microsoft...
 set "saa_runtime_file=%CD%\Prerequisites\windowsdesktop-runtime-10.0.14-win-x64.exe"
 powershell.exe -NoProfile -NonInteractive -Command "$s=Get-AuthenticodeSignature -LiteralPath $env:saa_runtime_file;if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){exit 1}"
 if errorlevel 1 (echo Microsoft runtime signature check failed. & exit /b 1)
 start /wait "" "Prerequisites\windowsdesktop-runtime-10.0.14-win-x64.exe" /install /norestart
)
call :desktop
if errorlevel 1 goto missing
call :aspnet
if errorlevel 1 (
 if not exist "Prerequisites\aspnetcore-runtime-10.0.14-win-x64.exe" goto missing
 echo Установка ASP.NET Core Runtime x64 отдельным пакетом Microsoft...
 set "saa_runtime_file=%CD%\Prerequisites\aspnetcore-runtime-10.0.14-win-x64.exe"
 powershell.exe -NoProfile -NonInteractive -Command "$s=Get-AuthenticodeSignature -LiteralPath $env:saa_runtime_file;if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){exit 1}"
 if errorlevel 1 (echo Microsoft runtime signature check failed. & exit /b 1)
 start /wait "" "Prerequisites\aspnetcore-runtime-10.0.14-win-x64.exe" /install /norestart
)
call :aspnet
if errorlevel 1 goto missing
start /wait "" "SysAdminAssistant.ServerSetup.exe" %setup_mode%
exit /b %errorlevel%
:aspnet
if not exist "%runtime_host%" exit /b 1
set "runtime_found="
for %%H in ("%runtime_host%") do set "runtime_root=%%~dpH"
for /d %%D in ("%runtime_root%shared\Microsoft.AspNetCore.App\10.0.*") do call :runtimePatch "%%~nxD"
if defined runtime_found exit /b 0
exit /b 1

:desktop
if not exist "%runtime_host%" exit /b 1
set "runtime_found="
for %%H in ("%runtime_host%") do set "runtime_root=%%~dpH"
for /d %%D in ("%runtime_root%shared\Microsoft.WindowsDesktop.App\10.0.*") do call :runtimePatch "%%~nxD"
if defined runtime_found exit /b 0
exit /b 1
:runtimePatch
echo %~1| findstr /r /x /c:"10\.0\.[0-9][0-9]*" >nul
if errorlevel 1 exit /b 0
for /f "tokens=3 delims=." %%P in ("%~1") do if %%P GEQ 14 set "runtime_found=1"
exit /b 0
:missing
echo Не найден .NET 10.0.14 или новее в ветке 10.0 x64. Установка отменена.
echo Скачайте его с https://dotnet.microsoft.com/download/dotnet/10.0
 echo Если установщик просит перезагрузку, перезагрузите Windows и повторите запуск.
pause
exit /b 1
