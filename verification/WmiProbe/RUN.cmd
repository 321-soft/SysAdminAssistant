@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
 echo Microsoft .NET 10 x64 runtime is required for this standalone probe.
 pause
 exit /b 2
)
dotnet SAA.HardwareProbe.dll > hardware-probe.txt 2>&1
set "probe_result=%errorlevel%"
type hardware-probe.txt
echo.
echo Result saved to hardware-probe.txt
pause
exit /b %probe_result%
