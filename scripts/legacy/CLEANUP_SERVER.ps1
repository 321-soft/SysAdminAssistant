# Interactive cleanup. No mutation before individual and final confirmation.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,System.Data,System.Security
[Windows.Forms.Application]::EnableVisualStyles()
$nl=[Environment]::NewLine
function Ask($text) { return [Windows.Forms.MessageBox]::Show($text,'Подтверждение','YesNo','Warning','Button2') -eq 'Yes' }
function Safe-Path([string]$path) {
 if([string]::IsNullOrWhiteSpace($path) -or -not [IO.Path]::IsPathRooted($path) -or $path.StartsWith('\\')) {throw 'Нужен абсолютный локальный путь.'}
 $full=[IO.Path]::GetFullPath($path).TrimEnd('\')
 foreach($blocked in @([IO.Path]::GetPathRoot($full).TrimEnd('\'),$env:ProgramData,$env:ProgramFiles,[Environment]::GetEnvironmentVariable('ProgramFiles(x86)'),$env:windir,$env:USERPROFILE)) {
  if($blocked -and $full -eq $blocked.TrimEnd('\')) {throw "Защищённый путь: $full"}
 }
 if($full.StartsWith($env:windir.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Системная папка Windows защищена.'}
 $parent=$full
 while($parent) {
  if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw "Ссылка/junction запрещена: $parent"}
  $parent=Split-Path -Parent $parent
 }
 return $full
}
function Check-Tree($path) {
 $null=Safe-Path $path
 if(Test-Path -LiteralPath $path) {
  if(Get-ChildItem -LiteralPath $path -Force -Recurse | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint} | Select-Object -First 1) {throw "В папке есть ссылки: $path"}
 }
}
function File-Hash($path) {return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Sql-Builder($settings) {
 $b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder(($settings.SqlConnectionString -replace '(?i)Encrypt=Mandatory','Encrypt=True'))
 if([string]::IsNullOrWhiteSpace($b.InitialCatalog) -or @('master','model','msdb','tempdb') -contains $b.InitialCatalog -or $b.InitialCatalog -match '[\x00-\x1f]' -or $b.InitialCatalog.Length -gt 128) {throw 'Недопустимое имя SQL-базы.'}
 $b.ConnectTimeout=10; $b.Pooling=$false
 if(-not $b.IntegratedSecurity -and $settings.ProtectedSqlPassword) {
  $value=[string]$settings.ProtectedSqlPassword; $scope=[Security.Cryptography.DataProtectionScope]::CurrentUser
  if($value.StartsWith('machine:')) {$scope=[Security.Cryptography.DataProtectionScope]::LocalMachine; $value=$value.Substring(8)}
  $bytes=[Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($value),[Text.Encoding]::UTF8.GetBytes('SysAdminAssistant.CSharp.Settings.v1'),$scope)
  try {$b.Password=[Text.Encoding]::UTF8.GetString($bytes)} finally {[Array]::Clear($bytes,0,$bytes.Length)}
 }
 return $b
}
function Check-Sql($row) {
 $b=Sql-Builder $row.Settings; $c=New-Object System.Data.SqlClient.SqlConnection($b.ConnectionString)
 try {
  $c.Open(); $cmd=$c.CreateCommand()
  $cmd.CommandText="SELECT CASE WHEN OBJECT_ID(N'dbo.sa2_SchemaInfo',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.sa2_Device',N'U') IS NOT NULL THEN 1 ELSE 0 END"
  if([int]$cmd.ExecuteScalar() -ne 1) {throw 'Таблицы SysAdmin Assistant не найдены. Удаление запрещено.'}
 } finally {$c.Dispose(); $b.Password=''}
}
function Check-Sqlite($target) {
 $null=Safe-Path $target
 $f=[IO.File]::Open($target,'Open','Read','ReadWrite')
 try {$bytes=New-Object byte[] 16; $n=$f.Read($bytes,0,16); if($n -ne 16 -or [Text.Encoding]::ASCII.GetString($bytes) -ne ('SQLite format 3'+[char]0)) {throw 'Файл не является SQLite'}} finally {$f.Dispose()}
}
function Read-Installation($folder) {
 $folder=Safe-Path $folder; $path=Join-Path $folder 'settings.json'
 if(-not (Test-Path -LiteralPath $path) -or -not (Test-Path -LiteralPath (Join-Path $folder 'server.json'))) {throw 'Нужны settings.json и server.json сервера.'}
 $null=Safe-Path $path
 $settings=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
 $config=Get-Content -LiteralPath (Join-Path $folder 'server.json') -Raw | ConvertFrom-Json
 $row=[pscustomobject]@{Folder=$folder;Path=$path;Hash=(File-Hash $path);Settings=$settings;Type='SQLite';Target='';Server='';Status='';Verified=$false;Account=([string]$config.ServiceAccountName)}
 try {
  if($settings.DatabaseProvider -eq 1 -or $settings.DatabaseProvider -eq 'SqlServer') {
   $row.Type='SQL Server'
   $b=New-Object System.Data.SqlClient.SqlConnectionStringBuilder(($settings.SqlConnectionString -replace '(?i)Encrypt=Mandatory','Encrypt=True'))
   $row.Target=$b.InitialCatalog; $row.Server=$b.DataSource; Check-Sql $row
  } else {
   $row.Target=Safe-Path ([string]$settings.SqlitePath)
   Check-Sqlite $row.Target
  }
  $row.Verified=$true; $row.Status='Найдена; удаление только после выбора'
 } catch {$row.Status='Не проверена: '+$_.Exception.Message}
 return $row
}
function Delete-Database($row) {
 if((File-Hash $row.Path) -ne $row.Hash -or -not $row.Verified) {throw 'Настройки изменились или база не проверена. Повторите поиск.'}
 if($row.Type -eq 'SQL Server') {
  Check-Sql $row
  $b=Sql-Builder $row.Settings; $name=$b.InitialCatalog; $b.InitialCatalog='master'
  $c=New-Object System.Data.SqlClient.SqlConnection($b.ConnectionString)
  try {
   $c.Open(); $cmd=$c.CreateCommand(); $cmd.CommandTimeout=60; $q='['+$name.Replace(']',']]')+']'
   $cmd.CommandText='ALTER DATABASE '+$q+' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE '+$q+';'
   [void]$cmd.ExecuteNonQuery()
  } finally {$c.Dispose();$b.Password=''}
 } else {
  $null=Safe-Path $row.Target
  $f=[IO.File]::Open($row.Target,'Open','ReadWrite','None'); $f.Dispose()
  foreach($file in @($row.Target,($row.Target+'-wal'),($row.Target+'-shm'),($row.Target+'-journal'))) {
   $null=Safe-Path $file; if(Test-Path -LiteralPath $file) {Remove-Item -LiteralPath $file -Force}
  }
 }
}
try {
 $admin=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
 if(-not $admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'Запустите UNINSTALL_SERVER.cmd от имени администратора.'}
 $service=Get-CimInstance Win32_Service -Filter "Name='SysAdminAssistantServer'"
 $script:programs=@();$folders=@()
 if($service) {
  if($service.PathName -notmatch '^"([^"]+SysAdminAssistant\.Server\.exe)"\s+--data-dir\s+"([^"]+)"\s*$') {throw 'Нельзя безопасно определить путь службы.'}
  $script:programs+=Safe-Path (Split-Path $matches[1]);$folders+=Safe-Path $matches[2]
 }
 foreach($base in @($env:ProgramData,$env:LOCALAPPDATA)) {
  if(Test-Path -LiteralPath $base) {$folders+=@(Get-ChildItem -LiteralPath $base -Directory | Where-Object {$_.Name -match '^SysAdminAssistantServer(?:-0\.[3-6])?$'} | ForEach-Object {$_.FullName})}
 }
 foreach($base in @($env:ProgramFiles,[Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))) {
  if($base -and (Test-Path -LiteralPath $base)) {
   $script:programs+=@(Get-ChildItem -LiteralPath $base -Directory | Where-Object {$_.Name -match '^SysAdmin Assistant Server(?: 0\.[3-6](?:\.\d+)?)?$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'SysAdminAssistant.Server.exe'))} | ForEach-Object {$_.FullName})
  }
 }
 $script:programs=@($script:programs|Select-Object -Unique)
 $script:rows=New-Object System.Collections.Generic.List[object]
 $form=New-Object Windows.Forms.Form; $form.Text='SysAdmin Assistant · Удаление установок';$form.Size=New-Object Drawing.Size(1050,650);$form.StartPosition='CenterScreen';$form.Font=New-Object Drawing.Font('Segoe UI',10)
 $grid=New-Object Windows.Forms.DataGridView;$grid.Dock='Fill';$grid.AllowUserToAddRows=$false;$grid.AllowUserToDeleteRows=$false;$grid.AutoSizeColumnsMode='Fill'
 $check=New-Object Windows.Forms.DataGridViewCheckBoxColumn;$check.Name='Delete';$check.HeaderText='Удалить базу';[void]$grid.Columns.Add($check)
 foreach($pair in @(@('Type','Тип'),@('Server','SQL-сервер'),@('Target','База / файл'),@('Folder','Папка настроек'),@('Status','Проверка'))) {
  $col=New-Object Windows.Forms.DataGridViewTextBoxColumn;$col.Name=$pair[0];$col.HeaderText=$pair[1];$col.ReadOnly=$true;[void]$grid.Columns.Add($col)
 }
 function Add-Installation($folder) {
  if($script:rows|Where-Object {$_.Folder -eq $folder}) {return}
  $row=Read-Installation $folder;$script:rows.Add($row)
  $index=$grid.Rows.Add($false,$row.Type,$row.Server,$row.Target,$row.Folder,$row.Status)
  if(-not $row.Verified) {$grid.Rows[$index].Cells[0].ReadOnly=$true}
  foreach($file in @(Get-ChildItem -LiteralPath $folder -File | Where-Object {$_.Extension -in @('.db','.sqlite','.sqlite3') -and $_.FullName -ne $row.Target})) {
   $extra=$row.PSObject.Copy();$extra.Type='SQLite';$extra.Server='';$extra.Target=$file.FullName;$extra.Verified=$false
   try {Check-Sqlite $extra.Target;$extra.Verified=$true;$extra.Status='Дополнительная база в папке установки'} catch {$extra.Status='Не проверена: '+$_.Exception.Message}
   $script:rows.Add($extra);$index=$grid.Rows.Add($false,$extra.Type,$extra.Server,$extra.Target,$extra.Folder,$extra.Status)
   if(-not $extra.Verified) {$grid.Rows[$index].Cells[0].ReadOnly=$true}
  }
 }
 foreach($folder in @($folders|Select-Object -Unique)) {if((Test-Path -LiteralPath (Join-Path $folder 'settings.json')) -and (Test-Path -LiteralPath (Join-Path $folder 'server.json'))) {Add-Installation $folder}}
 $panel=New-Object Windows.Forms.FlowLayoutPanel;$panel.Dock='Top';$panel.Height=175;$panel.FlowDirection='TopDown';$panel.WrapContents=$false
 $label=New-Object Windows.Forms.Label;$label.AutoSize=$true;$label.MaximumSize=New-Object Drawing.Size(990,0)
 $label.Text="Ни одна база не выбрана. Отметьте только базы для безвозвратного удаления. Непроверенные базы сохраняются.$nl Служба SysAdminAssistantServer и её firewall-правило будут удалены. .NET и клиент сохраняются.$nl Базы без конфигурации не ищутся. Добавьте папку настроек вручную, если её нет в списке."
 $add=New-Object Windows.Forms.Button;$add.Text='Добавить папку настроек…';$add.AutoSize=$true
 $files=New-Object Windows.Forms.CheckBox;$files.Text='Также удалить серверные программы и папки данных выбранных баз';$files.AutoSize=$true;$files.Checked=$false
 $accounts=New-Object Windows.Forms.CheckBox;$accounts.Text='Удалить также служебные Windows-учётки выбранных установок (если не используются)';$accounts.AutoSize=$true;$accounts.Checked=$false
 $panel.Controls.AddRange(@($label,$add,$files,$accounts))
 $bottom=New-Object Windows.Forms.FlowLayoutPanel;$bottom.Dock='Bottom';$bottom.Height=50
 $remove=New-Object Windows.Forms.Button;$remove.Text='Просмотреть и удалить…';$remove.AutoSize=$true
 $cancel=New-Object Windows.Forms.Button;$cancel.Text='Закрыть';$cancel.AutoSize=$true;$bottom.Controls.AddRange(@($remove,$cancel))
 $form.Controls.Add($grid);$form.Controls.Add($panel);$form.Controls.Add($bottom)
 $cancel.Add_Click({$form.Close()})
 $add.Add_Click({$d=New-Object Windows.Forms.FolderBrowserDialog;if($d.ShowDialog() -eq 'OK') {try {Add-Installation $d.SelectedPath} catch {[void][Windows.Forms.MessageBox]::Show($_.Exception.Message)}};$d.Dispose()})
 $remove.Add_Click({
  try {
   [void]$grid.EndEdit();$selected=@()
   for($i=0;$i -lt $script:rows.Count;$i++) {if($grid.Rows[$i].Cells[0].Value -eq $true) {$selected+=$script:rows[$i]}}
   foreach($chosen in $selected) {
    if($script:rows | Where-Object {$_ -notin $selected -and $_.Type -eq $chosen.Type -and $_.Server -eq $chosen.Server -and $_.Target -eq $chosen.Target}) {throw 'Эту же базу использует невыбранная установка. Выберите все её конфигурации или сохраните базу.'}
   }
   foreach($row in $selected) {if(-not (Ask ("Найдена база: "+$row.Target+$nl+"Сервер: "+$row.Server+$nl+"Настройки: "+$row.Folder+$nl+$nl+"Удалить эту базу безвозвратно?"))) {return}}
   $deleteFolders=@()
   if($files.Checked) {
    $deleteFolders=@(@($script:programs)+@($selected|ForEach-Object {$_.Folder})|Select-Object -Unique)
    foreach($folder in $deleteFolders) {
     Check-Tree $folder
     foreach($file in @(Get-ChildItem -LiteralPath $folder -Force -Recurse -File | Where-Object {$_.Extension -in @('.db','.sqlite','.sqlite3','.mdf','.ldf','.bak')})) {
      if(-not ($selected|Where-Object {$_.Type -eq 'SQLite' -and $_.Target -eq $file.FullName})) {throw ('Найдена дополнительная база или резервная копия: '+$file.FullName+'. Она не выбрана. Снимите удаление папок и сохраните этот файл отдельно.')}
     }
     if($PSScriptRoot -eq $folder -or $PSScriptRoot.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase)) {throw 'Запустите мастер из отдельной папки, вне удаляемой установки.'}
     foreach($kept in @($script:rows|Where-Object {$_ -notin $selected})) {
      if($kept.Folder -eq $folder -or $kept.Folder.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase) -or ($kept.Type -eq 'SQLite' -and $kept.Target.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase))) {throw 'Папка содержит невыбранную базу. Снимите удаление файлов.'}
     }
    }
   }
   $accountNames=@()
   if($accounts.Checked) {
    foreach($row in $selected) {
     if($row.Account -match '(?i)\\(SysAdminSvc(?:04|05|06)?)$') {
      $name=$matches[1]
      if($script:rows|Where-Object {$_ -notin $selected -and $_.Account -eq $row.Account}) {throw 'Учётка используется сохранённой установкой.'}
      $user=Get-CimInstance Win32_UserAccount -Filter "LocalAccount=True AND Name='$name'"
      if($user -and $user.Description -ne 'SysAdmin Assistant managed service account') {throw 'Учётка не принадлежит программе.'}
      if($user) {$accountNames+=$name}
     }
    }
   }
   $accountNames=@($accountNames|Select-Object -Unique)
   foreach($name in $accountNames) {
    if(Get-CimInstance Win32_Service|Where-Object {$_.Name -ne 'SysAdminAssistantServer' -and $_.StartName -match ('(?i)\\'+[regex]::Escape($name)+'$')}) {throw 'Учётка используется другой службой.'}
   }
   $review="Удалить службу и firewall-правило."+$nl+"Базы:"+$nl+(($selected|ForEach-Object {$_.Server+' / '+$_.Target}) -join $nl)+$nl+"Папки:"+$nl+($deleteFolders -join $nl)+$nl+'Учётки:'+$nl+($accountNames -join $nl)
   if(-not (Ask ($review+$nl+$nl+'Продолжить?'))) {return}
   $remove.Enabled=$false
   $now=Get-CimInstance Win32_Service -Filter "Name='SysAdminAssistantServer'"
   if(($service -and (-not $now -or $now.PathName -ne $service.PathName)) -or (-not $service -and $now)) {throw 'Служба изменилась. Повторите поиск.'}
   foreach($row in $selected) {if((File-Hash $row.Path) -ne $row.Hash) {throw 'Настройки изменились. Повторите поиск.'}}
   $task=Get-ScheduledTask -TaskName 'SysAdminAssistantMaintenance' -ErrorAction SilentlyContinue
   if($task){if($task.State -eq 'Running'){throw 'Дождитесь завершения обслуживания сервера перед удалением.'};Unregister-ScheduledTask -TaskName 'SysAdminAssistantMaintenance' -Confirm:$false}
   $maintenance=Join-Path $env:ProgramFiles 'SysAdmin Assistant Maintenance'
   if(Test-Path -LiteralPath $maintenance){Check-Tree $maintenance;Remove-Item -LiteralPath $maintenance -Recurse -Force}
   foreach($folder in @('CommonPrograms','CommonDesktopDirectory')){Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath($folder)) 'SysAdmin Assistant Server.lnk') -Force -ErrorAction SilentlyContinue}
   if($now) {$running=Get-Service SysAdminAssistantServer;if($running.Status -ne 'Stopped') {Stop-Service SysAdminAssistantServer;$running.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(45))}}
   $deleted=@{}
   foreach($row in $selected) {
    $key=$row.Type+'|'+$row.Server+'|'+$row.Target
    if(-not $deleted.ContainsKey($key)) {Delete-Database $row;$deleted[$key]=$true}
   }
   if($now) {& sc.exe delete SysAdminAssistantServer|Out-Null;if($LASTEXITCODE -ne 0) {throw 'Не удалось удалить службу.'}}
   if($now) {
    for($attempt=0;$attempt -lt 30;$attempt++) {
     if(-not (Get-CimInstance Win32_Service -Filter "Name='SysAdminAssistantServer'")) {break}
     Start-Sleep -Milliseconds 500
    }
    if(Get-CimInstance Win32_Service -Filter "Name='SysAdminAssistantServer'") {throw 'Служба помечена для удаления. Закройте services.msc или перезагрузите сервер; файлы сохранены.'}
   }
   @(Get-NetFirewallRule -DisplayName 'SysAdminAssistantServer-LAN' -ErrorAction SilentlyContinue)|Remove-NetFirewallRule -ErrorAction Stop
   foreach($name in $accountNames) {
    $user=Get-CimInstance Win32_UserAccount -Filter "LocalAccount=True AND Name='$name'"
    if($user -and $user.Description -ne 'SysAdmin Assistant managed service account') {throw 'Владелец учётки изменился.'}
    if(Get-CimInstance Win32_Service|Where-Object {$_.StartName -match ('(?i)\\'+[regex]::Escape($name)+'$')}) {throw 'Учётка ещё используется службой. Закройте services.msc и повторите после перезагрузки.'}
    if($user) {& net.exe user $name /delete|Out-Null;if($LASTEXITCODE -ne 0) {throw 'Не удалось удалить служебную учётку.'}}
   }
   foreach($folder in $deleteFolders) {Check-Tree $folder;if(Test-Path -LiteralPath $folder) {Remove-Item -LiteralPath $folder -Recurse -Force}}
   [void][Windows.Forms.MessageBox]::Show("Выбранные базы и файлы удалены. Невыбранные сохранены.$nl Выбранные служебные учётки удалены; остальные сохранены. Если служба помечена для удаления, закройте services.msc или перезагрузите Windows.",'Готово')
   $form.Close()
  } catch {
   [void][Windows.Forms.MessageBox]::Show("Остановлено: "+$_.Exception.Message+$nl+'Удаление могло выполниться частично. Повторно откройте мастер для поиска.','Удаление не завершено')
   $remove.Enabled=$false
  }
 })
 [void]$form.ShowDialog()
} catch {[void][Windows.Forms.MessageBox]::Show($_.Exception.Message,'Удаление не начато');exit 1}
