@echo off
setlocal DisableDelayedExpansion
title SysAdmin Assistant - Publish
echo Starting SysAdmin Assistant publication...
pushd "%~dp0"
if errorlevel 1 goto bad_directory
if not exist "scripts\build\PUBLISH.cmd" goto missing
if not exist "Directory.Build.props" goto missing
if not exist "scripts\build\BUILD_WORKSTATION_MSI.ps1" goto missing
if not exist "src\" goto missing
call ".\scripts\build\PUBLISH.cmd"
set "publish_rc=%errorlevel%"
popd
if "%publish_rc%"=="0" exit /b 0
echo.
echo Publisher exit code: %publish_rc%
pause
exit /b %publish_rc%

:missing
echo Publication files are missing. Extract the entire ZIP, then run PUBLISH.bat from the extracted folder.
popd
pause
exit /b 2

:bad_directory
echo Cannot open the project folder. Extract the ZIP to a local folder and retry.
pause
exit /b 2
