param([Parameter(Mandatory=$true)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ReleaseDirectory)
$sourceRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path (Join-Path $sourceRoot 'src\SysAdminAssistant.Core\ProductSetupBundle.cs') -ReferencedAssemblies System.IO.Compression,System.IO.Compression.FileSystem
$expected=Join-Path $root 'Uninstall\Uninstall.exe'
if(-not(Test-Path -LiteralPath $expected -PathType Leaf)){throw 'Native uninstaller build is missing.'}
if((Get-Item -LiteralPath $expected).Length -gt 30000000){throw 'Uninstaller exceeds the per-file signed agent update limit.'}
$hash=(Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash
function Confirm-Removal([string]$path,[bool]$allowSigned=$false){
 if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Embedded uninstaller is missing: $path"}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash){
  $valid=$false
  if($allowSigned -and $env:SAA_SIGNING_CERT_THUMBPRINT){
   $signature=Get-AuthenticodeSignature -LiteralPath $path
   $valid=$signature.Status -eq 'Valid' -and $signature.SignerCertificate.Thumbprint -eq $env:SAA_SIGNING_CERT_THUMBPRINT.Replace(' ','') -and (Get-Item -LiteralPath $path).VersionInfo.FileVersion -eq (Get-Item -LiteralPath $expected).VersionInfo.FileVersion
  }
  if(-not $valid){throw "Uninstaller payload differs from this release: $path"}
 }
}
Confirm-Removal (Join-Path $root 'Client\Uninstall.exe')
Confirm-Removal (Join-Path $root 'ServerSetup\Server\Uninstall.exe')
Confirm-Removal (Join-Path $root 'Client\AgentTemplate\Setup\Uninstall.exe') $true
Confirm-Removal (Join-Path $root 'ServerSetup\Server\AgentTemplate\Setup\Uninstall.exe') $true
$standalone=Join-Path $root 'WorkstationTools\Uninstall.exe'
if(-not(Test-Path -LiteralPath $standalone -PathType Leaf)){throw 'Self-contained workstation installer/remover is missing.'}
if((Get-FileHash -LiteralPath (Join-Path $root 'Client\WorkstationSetupLauncher.exe') -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $standalone -Algorithm SHA256).Hash){throw 'Workstation setup launcher does not match the self-contained build.'}
$work=Join-Path ([IO.Path]::GetTempPath()) ('SAA-package-check-'+[guid]::NewGuid().ToString('N'))
try{
 foreach($component in @('Client','Server')){
  $exe=Join-Path $root ('Installers\SysAdminAssistant_'+$component+'_Setup.exe')
  if(-not(Test-Path -LiteralPath $exe -PathType Leaf)){continue}
  $folder=Join-Path $work $component
  [SysAdminAssistant.Core.ProductSetupBundle]::Extract($exe,$folder,$null)
  $relative=if($component -eq 'Client'){'Uninstall.exe'}else{'Server\Uninstall.exe'}
  Confirm-Removal (Join-Path $folder $relative)
 }
 # Check the actual MSI File table, not merely its WiX source.
 $msi=Join-Path $root 'ServerSetup\Server\AgentTemplate\Workstation\SysAdminAssistant_Workstation_x64.msi'
 if(-not(Test-Path -LiteralPath $msi -PathType Leaf)){throw 'Workstation MSI is missing.'}
 $installer=$null;$database=$null;$view=$null;$record=$null
 try{
  $installer=New-Object -ComObject WindowsInstaller.Installer
  $database=$installer.OpenDatabase($msi,0)
  $view=$database.OpenView('SELECT `FileName`,`FileSize` FROM `File` WHERE `File`=''StationUninstallExe''')
  $view.Execute();$record=$view.Fetch()
  if($null -eq $record -or $record.StringData(1) -notmatch '(?i)(^|\|)Uninstall\.exe$' -or [long]$record.StringData(2) -ne (Get-Item -LiteralPath $standalone).Length){throw 'Workstation MSI does not install the self-contained Uninstall.exe.'}
  if((Get-Item -LiteralPath $msi).Length -gt 209715200){throw 'Workstation MSI exceeds the supported signed update size.'}
 }finally{
  foreach($item in @($record,$view,$database,$installer)){if($null -ne $item){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($item)}}
 }
 Write-Host 'PASS: client/server installers, both agent payloads and MSI contain this release uninstaller.'
}finally{if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force}}
