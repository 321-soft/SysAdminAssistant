$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
function T([string]$ru,[string]$en){if($env:SAA_LANGUAGE -eq 'en'){$en}else{$ru}}
try{
 $hostExe=Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
 foreach($requirement in @(@{Name='Microsoft.WindowsDesktop.App';File='windowsdesktop-runtime-10.0.14-win-x64.exe'},@{Name='Microsoft.AspNetCore.App';File='aspnetcore-runtime-10.0.14-win-x64.exe'})){
  $runtimes=@();if(Test-Path -LiteralPath $hostExe){$runtimes=@(& $hostExe --list-runtimes)}
  $pattern=[regex]::Escape($requirement.Name)+' 10\.0\.(1[4-9]|[2-9][0-9]|[1-9][0-9]{2,}) '
  if(-not($runtimes -match $pattern)){
   $installer=Join-Path $PSScriptRoot ('Prerequisites\'+$requirement.File)
   if(-not(Test-Path -LiteralPath $installer)){Start-Process 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0';throw (T 'Нужен .NET 10 Runtime x64 10.0.14 или новее. Он не включён в пакет. Открыта официальная страница; SDK не требуется.' '.NET 10 Runtime x64 10.0.14 or later is required and is not bundled. The official page was opened; the SDK is not required.')}
   $signature=Get-AuthenticodeSignature -LiteralPath $installer
   if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Invalid Microsoft runtime installer signature.'}
   $process=Start-Process -FilePath $installer -ArgumentList '/install','/passive','/norestart' -Wait -PassThru
   if($process.ExitCode -eq 3010){throw (T 'Перезагрузите Windows и повторите установку.' 'Restart Windows and run setup again.')}
   if($process.ExitCode -ne 0){throw 'Microsoft runtime installation failed.'}
   $runtimes=@(& $hostExe --list-runtimes);if(-not($runtimes -match $pattern)){throw 'Runtime not detected after installation.'}
  }
 }
 $process=Start-Process -FilePath (Join-Path $PSScriptRoot 'SysAdminAssistant.ServerSetup.exe') -WorkingDirectory $PSScriptRoot -Wait -PassThru
 exit $process.ExitCode
}catch{[void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message,(T 'Установка сервера' 'Server setup'),'OK','Error');exit 1}
