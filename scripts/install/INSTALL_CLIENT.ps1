param([Parameter(Mandatory=$true)][string]$Source,[Parameter(Mandatory=$true)][string]$SetupExe,[ValidateSet('ru','en')][string]$Language='ru',[ValidateSet(0,1)][int]$StartMenu=1,[ValidateSet(0,1)][int]$Desktop=1,[ValidateSet(0,1)][int]$Startup=0,[ValidateSet(0,1)][int]$InstallWix=0)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$installLease=New-Object Threading.Mutex($false,'Global\SysAdminAssistantClientInstall')
$leaseHeld=$false
try {
 try{$leaseHeld=$installLease.WaitOne(0)}catch [Threading.AbandonedMutexException]{$leaseHeld=$true}
 if(-not $leaseHeld){throw 'Another client installation or removal is already running.'}
Add-Type -AssemblyName System.Windows.Forms
function T([string]$ru,[string]$en){if($Language -eq 'en'){$en}else{$ru}}
$target=Join-Path $env:ProgramFiles 'SysAdmin Assistant'
foreach($path in @($Source,$target)){for($parent=New-Object IO.DirectoryInfo($path);$null -ne $parent;$parent=$parent.Parent){if($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Installation path contains a filesystem link.'}}}
$targetPrefix=$target+[IO.Path]::DirectorySeparatorChar
if([IO.Path]::GetFullPath($SetupExe).StartsWith($targetPrefix,[StringComparison]::OrdinalIgnoreCase)){
 throw (T 'Запустите новый Setup.exe из папки загрузок. Установщик внутри установленной папки мешает её замене.' 'Run the new Setup.exe from Downloads. A setup running inside the installed folder prevents its replacement.')
}
function Move-ClientDirectory([string]$from,[string]$to){
 for($attempt=0;$attempt -lt 16;$attempt++){
  try{[IO.Directory]::Move($from,$to);return}
  catch{
   $cause=$_.Exception.GetBaseException()
   if(($cause -isnot [IO.IOException] -and $cause -isnot [UnauthorizedAccessException]) -or $attempt -eq 15){throw}
   Start-Sleep -Milliseconds 500
  }
 }
}
function Close-InstalledClient {
$expectedExe=Join-Path $target 'SysAdminAssistant.exe'
foreach($client in @(Get-Process -Name SysAdminAssistant -ErrorAction SilentlyContinue)){
 if($client.Path -ne $expectedExe){continue}
 if($client.SessionId -ne (Get-Process -Id $PID).SessionId){throw (T 'Клиент открыт в другом сеансе Windows. Завершите его в этом сеансе.' 'A client is running in another Windows session. Close it in that session.')}
 while(-not $client.HasExited){
  try{$signal=[Threading.EventWaitHandle]::OpenExisting(('Local\SAA.Client.Close.'+$client.Id),[Security.AccessControl.EventWaitHandleRights]::Modify);try{$null=$signal.Set()}finally{$signal.Dispose()}}catch [Threading.WaitHandleCannotBeOpenedException]{$null=$client.CloseMainWindow()}catch [UnauthorizedAccessException]{$null=$client.CloseMainWindow()}
  if($client.WaitForExit(10000)){break}
  $choice=[Windows.Forms.MessageBox]::Show((T 'Клиент ещё открыт. Сохраните изменения и закройте диалоги. Да — повторить; Нет — запросить принудительное завершение; Отмена — выйти без установки.' 'The client is still open. Save changes and close dialogs. Yes: retry; No: request forced exit; Cancel: cancel setup.'),'SysAdmin Assistant','YesNoCancel','Question','Button3')
  if($choice -eq 'Cancel'){exit 2}
  if($choice -eq 'No' -and [Windows.Forms.MessageBox]::Show((T 'Завершить клиент принудительно? Несохранённые изменения будут потеряны.' 'Force the client to exit? Unsaved changes will be lost.'),'SysAdmin Assistant','YesNo','Warning','Button2') -eq 'Yes'){if(-not $client.HasExited){$client.Kill();if(-not $client.WaitForExit(10000)){throw (T 'Клиент не завершился. Закройте его и повторите установку.' 'The client did not exit. Close it and run setup again.')}}}
 }
}
}
if((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force)){
 $own=Join-Path $target 'SysAdminAssistant.dll'
 if(-not(Test-Path -LiteralPath $own) -or (Get-Item -LiteralPath $own).VersionInfo.ProductName -ne 'SysAdmin Assistant'){throw 'Target directory does not contain a recognized SysAdmin Assistant installation.'}
}
$hostExe=Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$runtimes=@();if(Test-Path -LiteralPath $hostExe){$runtimes=@(& $hostExe --list-runtimes)}
$pattern='Microsoft.WindowsDesktop.App 10\.0\.(1[4-9]|[2-9][0-9]|[1-9][0-9]{2,}) '
if(-not($runtimes -match $pattern)){
 $installer=Join-Path $Source 'Prerequisites\windowsdesktop-runtime-10.0.14-win-x64.exe'
 if(-not(Test-Path -LiteralPath $installer)){Start-Process 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0';throw (T 'Нужен .NET 10 Desktop Runtime x64 10.0.14 или новее. Он не включён в пакет; SDK не требуется.' '.NET 10 Desktop Runtime x64 10.0.14 or later is required and not bundled; the SDK is not required.')}
 $signature=Get-AuthenticodeSignature -LiteralPath $installer
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'Invalid Microsoft runtime signature.'}
 $process=Start-Process -FilePath $installer -ArgumentList '/install','/passive','/norestart' -Wait -PassThru
 if($process.ExitCode -eq 3010){throw (T 'Перезагрузите Windows и повторите установку.' 'Restart Windows and run setup again.')}
 if($process.ExitCode -ne 0){throw 'Runtime installation failed.'}
}
if ($InstallWix -eq 1) {
 $tools = Join-Path $Source 'BuildTools'
 $wixSetup = Join-Path $tools 'INSTALL_CLIENT_WIX.ps1'
 try {
  if (-not (Test-Path -LiteralPath $wixSetup -PathType Leaf)) { throw 'WiX package is missing.' }
  & $wixSetup -PackageDirectory $tools
 } catch {
  $warning = T 'Не удалось установить WiX. Для него требуется .NET 10 SDK x64. Установка клиента продолжится.' 'Could not install WiX. It requires the x64 .NET 10 SDK. Client setup will continue.'
  [void][Windows.Forms.MessageBox]::Show($warning+"$([Environment]::NewLine)$($_.Exception.Message)",'SysAdmin Assistant','OK','Warning')
 }
}
Close-InstalledClient
$backup=$null
if(Test-Path -LiteralPath $target){
 $backup=$target+'.backup.'+[Guid]::NewGuid().ToString('N')
 try{Move-ClientDirectory $target $backup}catch{
  $reason=$_.Exception.GetBaseException().Message
  throw ((T 'Не удалось освободить папку клиента. Закройте старый обновлятор, выберите «Выход» в трее и закройте окна, открытые в папке установки, затем повторите. Старая версия не заменена.' 'Could not move the client folder. Close the old updater, choose Exit in the system tray, close windows using the installation folder, and retry. The previous version was not replaced.')+"$([Environment]::NewLine)$target$([Environment]::NewLine)$reason")
 }
}
try{
 New-Item -ItemType Directory -Path $target -Force | Out-Null
 Get-ChildItem -LiteralPath $Source | Where-Object {$_.Name -notin @('INSTALL_CLIENT.ps1','BuildTools')} | Copy-Item -Destination $target -Recurse -Force
 Copy-Item -LiteralPath $SetupExe -Destination (Join-Path $target 'Setup.exe')
 $probe=Start-Process -FilePath (Join-Path $target 'SysAdminAssistant.exe') -ArgumentList '--preflight' -PassThru
 if(-not $probe.WaitForExit(30000)){try{$probe.Kill();$probe.WaitForExit()}catch{};throw 'Client startup verification timed out.'}
 if($probe.ExitCode -ne 0){throw 'Client startup verification failed.'}
 $shell=New-Object -ComObject WScript.Shell
 foreach($choice in @(@{Folder='CommonPrograms';Enabled=$StartMenu},@{Folder='CommonDesktopDirectory';Enabled=$Desktop},@{Folder='CommonStartup';Enabled=$Startup})){
  $link=Join-Path ([Environment]::GetFolderPath($choice.Folder)) 'SysAdmin Assistant.lnk'
  if($choice.Enabled -eq 1){$shortcut=$shell.CreateShortcut($link);$shortcut.TargetPath=Join-Path $target 'SysAdminAssistant.exe';$shortcut.WorkingDirectory=$target;$shortcut.IconLocation=(Join-Path $target 'SysAdminAssistant.exe')+',0';$shortcut.Save()}
  elseif(Test-Path -LiteralPath $link){Remove-Item -LiteralPath $link -Force}
 }
 $reg='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantClient';New-Item -Path $reg -Force|Out-Null
 $removal=Join-Path $target 'Uninstall.exe';if(-not(Test-Path -LiteralPath $removal)){throw 'Uninstaller package is missing.'}
 $uninstall='"'+$removal+'" --component client'
 $version=(Get-Item (Join-Path $target 'SysAdminAssistant.dll')).VersionInfo.FileVersion
 foreach($preference in @{StartMenu=$StartMenu;Desktop=$Desktop;Startup=$Startup}.GetEnumerator()){New-ItemProperty -Path $reg -Name $preference.Key -Value $preference.Value -PropertyType DWord -Force | Out-Null}
 foreach($item in @{DisplayName='SysAdmin Assistant';Publisher='321.su';DisplayVersion=$version;URLInfoAbout='https://assistant.321.su';HelpLink='https://321.su';InstallLocation=$target;UninstallString=$uninstall}.GetEnumerator()){New-ItemProperty -Path $reg -Name $item.Key -Value $item.Value -PropertyType String -Force | Out-Null}
 $removeLink=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'SysAdmin Assistant — Uninstall.lnk'));$removeLink.TargetPath=$removal;$removeLink.Arguments='--component client';$removeLink.WorkingDirectory=Split-Path $removal;$removeLink.Save()
 $local=[Environment]::GetFolderPath('LocalApplicationData')
 $maintenanceRoot=Join-Path $env:ProgramFiles 'SysAdmin Assistant Client Maintenance'
 for($parent=New-Object IO.DirectoryInfo($maintenanceRoot);$null -ne $parent;$parent=$parent.Parent){if($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Maintenance path contains a filesystem link.'}}
 [IO.Directory]::CreateDirectory($maintenanceRoot)|Out-Null
 $acl=New-Object Security.AccessControl.DirectorySecurity;$acl.SetAccessRuleProtection($true,$false)
 $admins=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544');$acl.SetOwner($admins)
 foreach($sid in @('S-1-5-18','S-1-5-32-544')){$identity=New-Object Security.Principal.SecurityIdentifier($sid);$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
 $users=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545');$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($users,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
 Set-Acl -LiteralPath $maintenanceRoot -AclObject $acl
 $helperVersion=(Get-Item (Join-Path $target 'SysAdminAssistant.dll')).VersionInfo.FileVersion
 $helperSource=Join-Path $target 'ProductMaintenance';$helperTarget=Join-Path $maintenanceRoot ('versions\'+$helperVersion)
 if(-not(Test-Path -LiteralPath (Join-Path $helperSource 'SysAdminAssistant.ProductMaintenance.exe'))){throw 'Protected client update helper is missing from the package.'}
 [IO.Directory]::CreateDirectory($helperTarget)|Out-Null
 foreach($file in Get-ChildItem -LiteralPath $helperSource -File){$destination=Join-Path $helperTarget $file.Name;if(Test-Path -LiteralPath $destination){if((Get-FileHash $file.FullName).Hash -ne (Get-FileHash $destination).Hash){throw 'Client maintenance version exists with different contents.'}}else{Copy-Item -LiteralPath $file.FullName -Destination $destination}}
 $locale=Join-Path $local 'SysAdminAssistantClient\language.json';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($locale))|Out-Null
 [IO.File]::WriteAllText($locale,(@{Language=$Language}|ConvertTo-Json -Compress),[Text.Encoding]::UTF8);[IO.File]::WriteAllText((Join-Path $target 'language.json'),(@{Language=$Language}|ConvertTo-Json -Compress),[Text.Encoding]::UTF8)
 if($env:SAA_EULA_ACCEPTED){
  $ru=[IO.File]::ReadAllText((Join-Path $target 'USER_AGREEMENT_RU.txt')).Replace("`r`n","`n").Replace("`r","`n").TrimEnd();$en=[IO.File]::ReadAllText((Join-Path $target 'USER_AGREEMENT_EN.txt')).Replace("`r`n","`n").Replace("`r","`n").TrimEnd()
  $hash=[Security.Cryptography.SHA256]::Create();try{$digest=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($ru+"`n"+$en)))).Replace('-','')}finally{$hash.Dispose()}
  if($digest -eq $env:SAA_EULA_ACCEPTED){$record=Join-Path $local 'SysAdminAssistant\legal\acceptance.json';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($record))|Out-Null;[IO.File]::WriteAllText($record,(@{Version='1.2';Sha256=$digest;Language=$Language.ToUpperInvariant();AcceptedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json -Compress),[Text.Encoding]::UTF8)}
 }
 [void][System.Windows.Forms.MessageBox]::Show((T 'Клиент установлен. Запустите его из меню Пуск.' 'Client installed. Start it from the Start menu.'))
}catch{if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force};if($backup){Move-ClientDirectory $backup $target;$previousVersion=(Get-Item (Join-Path $target 'SysAdminAssistant.dll')).VersionInfo.FileVersion;Set-ItemProperty -LiteralPath 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantClient' -Name DisplayVersion -Value $previousVersion -ErrorAction SilentlyContinue}else{Remove-Item -LiteralPath 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SysAdminAssistantClient' -Force -ErrorAction SilentlyContinue;Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'SysAdmin Assistant.lnk') -ErrorAction SilentlyContinue};throw}
} catch { [Console]::Error.WriteLine($_.Exception.GetBaseException().Message); exit 1 } finally {if($leaseHeld){$installLease.ReleaseMutex()};$installLease.Dispose()}
