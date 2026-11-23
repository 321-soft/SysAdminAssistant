$ErrorActionPreference='Stop'
$service=Get-CimInstance Win32_Service -Filter "Name='SysAdminAssistantAgent'"
if(-not $service){throw 'Служба агента не найдена'}
if($service.StartName -ne 'NT AUTHORITY\LocalService'){throw 'Агент работает не под LocalService'}
if($service.StartMode -ne 'Auto' -or $service.State -ne 'Running'){throw 'Служба не запущена или не настроен автозапуск'}
$expected=Join-Path $env:ProgramFiles 'SysAdmin Assistant Agent\SysAdminAssistant.Agent.exe'
if($service.PathName.Trim('"') -ne $expected){throw 'Неожиданный путь службы'}
$tcp=Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object {$_.OwningProcess -eq $service.ProcessId}
$udp=Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object {$_.OwningProcess -eq $service.ProcessId}
if($tcp -or $udp){throw 'Процесс агента открыл входящий endpoint'}
$data=Join-Path $env:ProgramData 'SysAdminAssistantAgent'
$agentSid=(New-Object System.Security.Principal.NTAccount('NT SERVICE','SysAdminAssistantAgent')).Translate([System.Security.Principal.SecurityIdentifier]).Value
foreach($path in @((Split-Path $expected),$data,(Join-Path $data 'bootstrap.json'))){
    $acl=Get-Acl -LiteralPath $path
    if(-not $acl.AreAccessRulesProtected){throw "Наследование ACL не отключено: $path"}
    $allowed=@('S-1-5-18','S-1-5-32-544',$agentSid)
    foreach($rule in $acl.Access){$sid=$rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value;if($rule.AccessControlType -eq 'Allow' -and $allowed -notcontains $sid){throw "Лишний субъект ACL: $sid / $path"}}
}
$status=Get-Content -LiteralPath (Join-Path $data 'status.json') -Raw | ConvertFrom-Json
Write-Host "Agent status: $($status.Status)"
Write-Host "Public key SHA256: $($status.PublicKeySha256)"
Write-Host 'PASS: LocalService, autostart, expected binary, no inbound endpoints, isolated ACL.'
Write-Host 'Это проверка текущей установки; она не доказывает отсутствие уязвимостей.'

$task=Get-ScheduledTask -TaskPath '\' -TaskName 'SysAdminAssistantAgentUpdateRead' -ErrorAction Stop
$probe=Join-Path (Split-Path $expected) 'SysAdminAssistant.UpdateProbe.exe'
if($task.Actions.Count -ne 1 -or $task.Actions[0].Execute -ne $probe -or $task.Actions[0].Arguments){throw 'Неожиданное действие задачи обновлений'}
$principal=$task.Principal.UserId
if($principal -ne 'S-1-5-18'){$principal=(New-Object System.Security.Principal.NTAccount($principal)).Translate([System.Security.Principal.SecurityIdentifier]).Value}
if($principal -ne 'S-1-5-18'){throw 'Локальный сборщик обновлений работает не под SYSTEM'}
$cache=Join-Path (Split-Path $expected) 'UpdateCache'
if(Test-Path -LiteralPath $cache){
 foreach($rule in (Get-Acl -LiteralPath $cache).Access){
  $sid=$rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
  if($rule.AccessControlType -eq 'Allow' -and $sid -notin @('S-1-5-18','S-1-5-32-544') -and (($rule.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Write) -ne 0 -or ($rule.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Delete) -ne 0)){throw "Лишние права записи в снимок: $sid"}
 }
}
Write-Host 'PASS: fixed local update probe task, SYSTEM identity, read-only agent access to snapshot.'

$maintenance=Join-Path (Split-Path $expected) 'Maintenance'
$config=Join-Path $maintenance 'automatic.json'
if(Test-Path -LiteralPath $config){
 $automatic=Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
 foreach($path in @($maintenance,$config)+(Get-ChildItem -LiteralPath $maintenance -Recurse -Force | Select-Object -ExpandProperty FullName)){
  $item=Get-Item -LiteralPath $path -Force
  if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Ссылка в обслуживании агента: $path"}
  foreach($rule in (Get-Acl -LiteralPath $path).Access){
   $sid=$rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
   $write=[System.Security.AccessControl.FileSystemRights]::Write -bor [System.Security.AccessControl.FileSystemRights]::Delete -bor [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor [System.Security.AccessControl.FileSystemRights]::TakeOwnership -bor [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles
   if($rule.AccessControlType -eq 'Allow' -and $sid -notin @('S-1-5-18','S-1-5-32-544') -and ($rule.FileSystemRights -band $write)){throw "Лишние права обслуживания: $sid / $path"}
  }
 }
 if($automatic.Enabled){
  $auto=Get-ScheduledTask -TaskPath '\' -TaskName 'SysAdminAssistantAgentAutoUpdate' -ErrorAction Stop
  $slots=@((Join-Path $maintenance 'A\SysAdminAssistant.AgentSetup.exe'),(Join-Path $maintenance 'B\SysAdminAssistant.AgentSetup.exe'))
  if($auto.Actions.Count -ne 1 -or $auto.Actions[0].Execute -notin $slots -or $auto.Actions[0].Arguments -ne '--auto-update'){throw 'Неожиданное действие автообновления'}
  $who=$auto.Principal.UserId
  if($who -ne 'S-1-5-18'){$who=(New-Object System.Security.Principal.NTAccount($who)).Translate([System.Security.Principal.SecurityIdentifier]).Value}
  if($who -ne 'S-1-5-18'){throw 'Обслуживание работает не под SYSTEM'}
  Write-Host 'PASS: own fixed auto update task and protected maintenance files.'
 }else{Write-Host 'Автообновление отключено при локальной установке.'}
}else{Write-Host 'Механизм автообновления ещё не установлен.'}
