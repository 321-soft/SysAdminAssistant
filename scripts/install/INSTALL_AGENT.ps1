param([switch]$SingleFile)
$ErrorActionPreference='Stop'
function T([string]$ru,[string]$en){if($env:SAA_LANGUAGE -eq 'en'){$en}else{$ru}}
function Has-DesktopRuntime {
 $root=Join-Path $env:ProgramW6432 'dotnet\shared\Microsoft.WindowsDesktop.App'
 if(-not (Test-Path -LiteralPath $root)){return $false}
 foreach($dir in Get-ChildItem -LiteralPath $root -Directory){
  if($dir.Name -match '^10\.0\.(\d+)$' -and [int]$Matches[1] -ge 14){return $true}
 }
 return $false
}
try {
 if(-not (Has-DesktopRuntime)){
  Write-Host (T 'Для запуска готового агента нужна среда выполнения .NET Desktop Runtime 10 x64. SDK не требуется.' '.NET Desktop Runtime 10 x64 is required. The SDK is not required.')
  $package=Join-Path $PSScriptRoot 'Prerequisites\windowsdesktop-runtime-10.0.14-win-x64.exe'
  if(-not (Test-Path -LiteralPath $package -PathType Leaf)){throw (T 'Пакет неполный: нет включённого установщика Runtime. Создайте пакет агента заново в Assistant 0.11.0.' 'The Runtime installer is missing. Create a complete agent package in Assistant.')}
  $expected='337dacab1636219147b3fe75e451bf5b1c17bca2c86328266d40c4ad1ff0a1e8391c4746ca7a5b702b65b61e8bfe834566cfc1b548cc10aef90b441357b10b69'
  if((Get-FileHash -LiteralPath $package -Algorithm SHA512).Hash -ne $expected){throw (T 'Файл Runtime изменён: SHA-512 не совпадает' 'The Runtime file SHA-512 does not match.')}
  $signature=Get-AuthenticodeSignature -LiteralPath $package
  if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw (T 'Не подтверждена подпись Microsoft у установщика Runtime' 'The Runtime installer Microsoft signature could not be verified.')}
  Write-Host (T 'Установка Runtime из пакета Microsoft. Подтвердите запрос Windows.' 'Installing the Microsoft Runtime package. Confirm the Windows prompt.')
  $process=Start-Process -FilePath $package -ArgumentList '/install','/passive','/norestart' -Verb RunAs -Wait -PassThru
  if($process.ExitCode -eq 3010){Write-Host (T 'Runtime установлен. Требуется перезагрузка Windows. После перезагрузки снова запустите INSTALL_AGENT.cmd. Автоматической перезагрузки не будет.' 'Runtime was installed. Restart Windows and run agent setup again. No automatic restart will occur.');if(-not $SingleFile){Read-Host (T 'Enter для выхода' 'Press Enter to exit')};exit 3010}
  if($process.ExitCode -ne 0){throw ((T 'Установщик Runtime завершился с кодом ' 'Runtime installer returned code: ')+$process.ExitCode)}
  if(-not (Has-DesktopRuntime)){throw (T 'После установки Runtime не обнаружен. Перезагрузите Windows и повторите запуск.' 'Runtime was not detected after installation. Restart Windows and retry.')}
 }
 Write-Host (T 'Runtime готов. Открываю установщик агента.' 'Runtime is ready. Opening agent setup.')
 $setupProcess=Start-Process -FilePath (Join-Path $PSScriptRoot 'SysAdminAssistant.AgentSetup.exe') -Verb RunAs -Wait -PassThru
 if($setupProcess.ExitCode -ne 0){throw ((T 'Установщик агента завершился с кодом ' 'Agent setup returned code: ')+$setupProcess.ExitCode)}
 exit 0
} catch {
 Write-Host ((T 'Установка не завершена: ' 'Installation did not complete: ')+$_.Exception.Message) -ForegroundColor Red
 if($SingleFile){Add-Type -AssemblyName System.Windows.Forms;[System.Windows.Forms.MessageBox]::Show($_.Exception.Message,(T 'Установка агента' 'Agent setup')) | Out-Null}
 exit 1
}
