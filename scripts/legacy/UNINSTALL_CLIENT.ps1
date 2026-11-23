$ErrorActionPreference='Stop'
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$PSCommandPath+'"') -Wait;exit}
$installLease=New-Object Threading.Mutex($false,'Global\SysAdminAssistantClientInstall')
$leaseHeld=$false
try {
 try{$leaseHeld=$installLease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$leaseHeld=$true}
 if(-not $leaseHeld){throw 'Another client installation or removal is already running.'}
Add-Type -AssemblyName System.Windows.Forms
if([System.Windows.Forms.MessageBox]::Show('Удалить клиент? Личные настройки и данные сервера сохраняются. / Remove the client? Personal settings and server data will be kept.','SysAdmin Assistant','YesNo','Question') -ne 'Yes'){exit}
$target=Join-Path $env:ProgramFiles 'SysAdmin Assistant'
if([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ne $target.TrimEnd('\')){throw 'Unexpected installation path.'}
if(Get-Process -Name 'SysAdminAssistant.ProductMaintenance' -ErrorAction SilentlyContinue){throw 'Client update is running. Wait until it has finished.'}
if(Get-Process -Name SysAdminAssistant -ErrorAction SilentlyContinue){throw 'Close the client before uninstalling.'}
if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Installation directory is a link.'}
foreach($folder in @('CommonPrograms','CommonDesktopDirectory','CommonStartup')){Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath($folder)) 'SysAdmin Assistant.lnk') -Force -ErrorAction SilentlyContinue}
Remove-Item -LiteralPath 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantClient' -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $target -Recurse -Force
$maintenanceRoot=Join-Path $env:ProgramFiles 'SysAdmin Assistant Client Maintenance'
if(Test-Path -LiteralPath $maintenanceRoot){if((Get-Item -LiteralPath $maintenanceRoot).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Maintenance directory is a link.'};if(Get-ChildItem -LiteralPath $maintenanceRoot -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Maintenance directory contains a link.'};Remove-Item -LiteralPath $maintenanceRoot -Recurse -Force}
} finally {if($leaseHeld){$installLease.ReleaseMutex()};$installLease.Dispose()}
