@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0..\.."
if errorlevel 1 goto failed
echo Publishing SysAdmin Assistant 0.46.14
if exist publish\Release_0.46.14 (
 echo Папка publish\Release_0.46.14 уже существует. Переместите её перед повторной публикацией.
 pause
 exit /b 1
)
echo [Publish] Preparing the publisher key...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\release\PREPARE_RELEASE_KEY.ps1"
if errorlevel 1 goto failed
choice /C 123 /N /M "Формат: 1 - setup.exe, 2 - папки, 3 - оба варианта [1/2/3]: "
set "publish_mode=%errorlevel%"
if %publish_mode% GTR 3 goto failed
if %publish_mode% LSS 1 goto failed
set "include_wix=0"
if "%publish_mode%"=="2" goto check_wix
choice /C YN /N /M "Включить WiX в установщик клиента? [Y/N]: "
if errorlevel 3 goto failed
if errorlevel 2 goto check_wix
if not errorlevel 1 goto failed
set "include_wix=1"
:check_wix
echo Preparing private build tools for the workstation MSI...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\build\BUILD_WORKSTATION_MSI.ps1" -PrepareOnly
if errorlevel 1 goto failed
echo [Publish] Building solution and running checks...
call VERIFY.cmd --no-pause
if errorlevel 1 goto failed
set "include_runtimes=0"
choice /C YN /N /M "Скачать отсутствующие установщики .NET Runtime? [Y/N]: "
if errorlevel 3 goto failed
if errorlevel 2 goto publish_apps
if not errorlevel 1 goto failed
call scripts\build\FETCH_RUNTIMES.cmd
if errorlevel 1 goto runtime_download_failed
set "include_runtimes=1"
goto publish_apps
:runtime_download_failed
choice /C YN /N /M "Загрузка runtime не удалась. Продолжить публикацию без установщиков .NET? [Y/N]: "
if errorlevel 3 goto failed
if errorlevel 2 goto failed
if not errorlevel 1 goto failed
:publish_apps
echo [Publish] Publishing application files...
if exist RuntimePackages\vc_redist.x64.exe goto invoice_runtime_fetch
choice /C YN /N /M "Include Microsoft VC++ runtime installer for local OCR? [Y/N]: "
if errorlevel 3 goto failed
if errorlevel 2 goto invoice_runtime_ready
:invoice_runtime_fetch
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\build\FETCH_INVOICE_RUNTIME.ps1"
if errorlevel 1 echo OCR runtime was not downloaded; PDF text recognition remains available.
:invoice_runtime_ready
dotnet publish src\SysAdminAssistant.Uninstall\SysAdminAssistant.Uninstall.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\Uninstall
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.Uninstall\SysAdminAssistant.Uninstall.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\WorkstationTools
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.Desktop\SysAdminAssistant.Desktop.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\Client
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.ProductMaintenance\SysAdminAssistant.ProductMaintenance.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\Client\ProductMaintenance
if errorlevel 1 goto failed

dotnet publish src\SysAdminAssistant.ServerSetup\SysAdminAssistant.ServerSetup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\ServerSetup
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.Server\SysAdminAssistant.Server.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\ServerSetup\Server
if errorlevel 1 goto failed
if exist RuntimePackages\vc_redist.x64.exe (
 if not exist publish\Release_0.46.14\ServerSetup\InvoiceRuntimePackages mkdir publish\Release_0.46.14\ServerSetup\InvoiceRuntimePackages
 copy /y RuntimePackages\vc_redist.x64.exe publish\Release_0.46.14\ServerSetup\InvoiceRuntimePackages\vc_redist.x64.exe >nul
)
if exist publish\Release_0.46.14\ServerSetup\Server\x86 rmdir /s /q publish\Release_0.46.14\ServerSetup\Server\x86
echo [Publish] Building workstation MSI...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\build\BUILD_WORKSTATION_MSI.ps1" -OutputDirectory "%~dp0..\..\publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Workstation"
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.AgentLinux\SysAdminAssistant.AgentLinux.csproj -c Release -r linux-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Linux
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.Agent\SysAdminAssistant.Agent.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\Client\AgentTemplate\Agent
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.AgentSetup\SysAdminAssistant.AgentSetup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\Client\AgentTemplate\Setup
if errorlevel 1 goto failed
dotnet publish src\SysAdminAssistant.UpdateProbe\SysAdminAssistant.UpdateProbe.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -m:1 -nr:false -o publish\Release_0.46.14\Client\AgentTemplate\Agent
if errorlevel 1 goto failed
copy /y scripts\install\INSTALL_AGENT.cmd publish\Release_0.46.14\Client\AgentTemplate\INSTALL_AGENT.cmd >nul
if errorlevel 1 goto failed
copy /y verification\VerifyAgentWindows.ps1 publish\Release_0.46.14\Client\AgentTemplate\VerifyAgentWindows.ps1 >nul
if errorlevel 1 goto failed
copy /y scripts\install\INSTALL_AGENT.ps1 publish\Release_0.46.14\Client\AgentTemplate\INSTALL_AGENT.ps1 >nul
if errorlevel 1 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\build\BUILD_AGENT_LAUNCHER.ps1" -OutputPath "%~dp0..\..\publish\Release_0.46.14\Client\AgentTemplate\AgentSetupLauncher.exe"
if errorlevel 1 goto failed
copy /y publish\Release_0.46.14\Uninstall\Uninstall.exe publish\Release_0.46.14\Client\AgentTemplate\Setup\Uninstall.exe >nul
if errorlevel 1 goto failed
if not defined SAA_SIGNING_CERT_THUMBPRINT goto agent_signing_done
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\release\SIGN_AGENT_RELEASE.ps1" -ReleaseDirectory "%~dp0..\..\publish\Release_0.46.14\Client\AgentTemplate"
if errorlevel 1 goto failed
:agent_signing_done
xcopy /E /I /Y publish\Release_0.46.14\Client\AgentTemplate\Agent publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Agent >nul
if errorlevel 1 goto failed
xcopy /E /I /Y publish\Release_0.46.14\Client\AgentTemplate\Setup publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Setup >nul
if errorlevel 1 goto failed
copy /y scripts\legacy\CLEANUP_SERVER.ps1 publish\Release_0.46.14\ServerSetup\CLEANUP_SERVER.ps1 >nul
if errorlevel 1 goto failed
copy /y scripts\install\INSTALL_SERVER_BOOTSTRAP.ps1 publish\Release_0.46.14\ServerSetup\INSTALL_SERVER_BOOTSTRAP.ps1 >nul
if errorlevel 1 goto failed
copy /y scripts\install\SETUP_SERVER.cmd publish\Release_0.46.14\ServerSetup\SETUP_SERVER.cmd >nul
if errorlevel 1 goto failed
copy /y scripts\legacy\REMOVE_SYSADMIN_SERVER_ALL.bat publish\Release_0.46.14\ServerSetup\REMOVE_SYSADMIN_SERVER_ALL.bat >nul
if errorlevel 1 goto failed
copy /y scripts\install\INSTALL_CLIENT.ps1 publish\Release_0.46.14\Client\INSTALL_CLIENT.ps1 >nul
if errorlevel 1 goto failed
copy /y scripts\install\START_CLIENT.cmd publish\Release_0.46.14\Client\START_CLIENT.cmd >nul
if errorlevel 1 goto failed
if exist RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe (
 if not exist publish\Release_0.46.14\Client\Prerequisites mkdir publish\Release_0.46.14\Client\Prerequisites
 copy /y RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\Client\Prerequisites\ >nul
 if errorlevel 1 goto failed
)
if exist RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe (
 if not exist publish\Release_0.46.14\ServerSetup\Prerequisites mkdir publish\Release_0.46.14\ServerSetup\Prerequisites
 copy /y RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\ServerSetup\Prerequisites\ >nul
 if errorlevel 1 goto failed
 if not exist publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Runtime mkdir publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Runtime
 copy /y RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\ServerSetup\Server\AgentTemplate\Runtime\ >nul
 if errorlevel 1 goto failed
)
if exist RuntimePackages\aspnetcore-runtime-10.0.14-win-x64.exe (
 if not exist publish\Release_0.46.14\ServerSetup\Prerequisites mkdir publish\Release_0.46.14\ServerSetup\Prerequisites
 copy /y RuntimePackages\aspnetcore-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\ServerSetup\Prerequisites\ >nul
 if errorlevel 1 goto failed
)
if "%include_runtimes%"=="0" goto runtime_done
if not exist publish\Release_0.46.14\Client\Prerequisites mkdir publish\Release_0.46.14\Client\Prerequisites
if errorlevel 1 goto failed
if not exist publish\Release_0.46.14\ServerSetup\Prerequisites mkdir publish\Release_0.46.14\ServerSetup\Prerequisites
if errorlevel 1 goto failed
copy /y RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\Client\Prerequisites\ >nul
if errorlevel 1 goto failed
copy /y RuntimePackages\windowsdesktop-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\ServerSetup\Prerequisites\ >nul
if errorlevel 1 goto failed
copy /y RuntimePackages\aspnetcore-runtime-10.0.14-win-x64.exe publish\Release_0.46.14\ServerSetup\Prerequisites\ >nul
if errorlevel 1 goto failed
:runtime_done
if errorlevel 1 goto failed
xcopy /E /I /Y WebWatchdog publish\Release_0.46.14\Client\WebWatchdog >nul
if errorlevel 1 goto failed
xcopy /E /I /Y WebWatchdog publish\Release_0.46.14\ServerSetup\WebWatchdog >nul
if errorlevel 1 goto failed
copy /y scripts\diagnostics\DIAG_AGENT_PROCESSES.ps1 publish\Release_0.46.14\Client\DIAG_AGENT_PROCESSES.ps1 >nul
if errorlevel 1 goto failed
copy /y THIRD_PARTY_NOTICES.md publish\Release_0.46.14\Client\THIRD_PARTY_NOTICES.md >nul
if errorlevel 1 goto failed
copy /y THIRD_PARTY_NOTICES.md publish\Release_0.46.14\ServerSetup\THIRD_PARTY_NOTICES.md >nul
if errorlevel 1 goto failed
copy /y CHANGELOG.md publish\Release_0.46.14\Client\CHANGELOG.md >nul
if errorlevel 1 goto failed
copy /y docs\legal\USER_AGREEMENT_RU.txt publish\Release_0.46.14\Client\USER_AGREEMENT_RU.txt >nul
if errorlevel 1 goto failed
copy /y docs\legal\USER_AGREEMENT_EN.txt publish\Release_0.46.14\Client\USER_AGREEMENT_EN.txt >nul
if errorlevel 1 goto failed
copy /y CHANGELOG.md publish\Release_0.46.14\ServerSetup\CHANGELOG.md >nul
if errorlevel 1 goto failed
copy /y docs\legal\USER_AGREEMENT_RU.txt publish\Release_0.46.14\ServerSetup\USER_AGREEMENT_RU.txt >nul
if errorlevel 1 goto failed
copy /y docs\legal\USER_AGREEMENT_EN.txt publish\Release_0.46.14\ServerSetup\USER_AGREEMENT_EN.txt >nul
if errorlevel 1 goto failed
copy /y publish\Release_0.46.14\WorkstationTools\Uninstall.exe publish\Release_0.46.14\Client\WorkstationSetupLauncher.exe >nul
if errorlevel 1 goto failed
copy /y publish\Release_0.46.14\Uninstall\Uninstall.exe publish\Release_0.46.14\Client\Uninstall.exe >nul
if errorlevel 1 goto failed
copy /y publish\Release_0.46.14\Uninstall\Uninstall.exe publish\Release_0.46.14\ServerSetup\Server\Uninstall.exe >nul
if errorlevel 1 goto failed
for %%D in (Client ServerSetup) do (
 copy /y LICENSE.txt publish\Release_0.46.14\%%D\LICENSE.txt >nul
 if errorlevel 1 goto failed
)
if "%publish_mode%"=="2" goto packaging_done
if "%include_wix%"=="0" goto build_product_setups
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\build\GET_CLIENT_WIX_PACKAGE.ps1" -OutputDirectory "%~dp0..\..\publish\Release_0.46.14\Client\BuildTools"
if errorlevel 1 goto failed
copy /y scripts\install\INSTALL_CLIENT_WIX.ps1 publish\Release_0.46.14\Client\BuildTools\INSTALL_CLIENT_WIX.ps1 >nul
if errorlevel 1 goto failed
:build_product_setups
echo [Publish] Building product installers...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\scripts\build\BUILD_PRODUCT_SETUPS.ps1" -ReleaseDirectory "%~dp0..\..\publish\Release_0.46.14"
if errorlevel 1 goto failed
goto packaging_done
:packaging_done
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\verification\VerifyInstallerContents.ps1" -ReleaseDirectory "%~dp0..\..\publish\Release_0.46.14"
if errorlevel 1 goto failed
if not "%publish_mode%"=="1" goto keep_product_folders
rmdir /s /q publish\Release_0.46.14\Client
rmdir /s /q publish\Release_0.46.14\ServerSetup
:keep_product_folders
if exist publish\Release_0.46.14\WorkstationTools rmdir /s /q publish\Release_0.46.14\WorkstationTools
if exist publish\Release_0.46.14\Uninstall rmdir /s /q publish\Release_0.46.14\Uninstall
echo Готово: publish\Release_0.46.14. Формат: %publish_mode%.
if "%include_runtimes%"=="0" echo Загрузка .NET пропущена. Имеющиеся RuntimePackages включены; отсутствующие runtime установите отдельно.
if "%publish_mode%"=="1" (echo Запустите нужный Setup.exe из папки Installers.) else (echo Перенесите ServerSetup целиком и запустите SETUP_SERVER.cmd.)
pause
exit /b 0
:failed
echo Публикация не завершена. Скопируйте сообщения ошибок.
pause
exit /b 1
