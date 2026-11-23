$ErrorActionPreference='Stop'
# Read-only. No command lines or secrets; no process termination or service changes.
$processRows=@(Get-CimInstance Win32_Process)
$roots=@($processRows | Where-Object {$_.Name -like 'SysAdminAssistant*'})
$selectedIds=@($roots | ForEach-Object {$_.ProcessId})
do {
    $oldCount=$selectedIds.Count
    $selectedIds=@($selectedIds+@($processRows | Where-Object {$_.ParentProcessId -in $selectedIds} | ForEach-Object {$_.ProcessId}) | Sort-Object -Unique)
} while ($selectedIds.Count -gt $oldCount)
$serviceRows=@(Get-CimInstance Win32_Service | Where-Object {$_.ProcessId -in $selectedIds -and $_.ProcessId -ne 0})
'=== Processes ==='
$processRows | Where-Object {$_.ProcessId -in $selectedIds} | Select-Object Name,ProcessId,ParentProcessId,ExecutablePath,CreationDate | Format-List
'=== Services for these process IDs ==='
$serviceRows | Select-Object Name,DisplayName,ProcessId,State,StartMode | Format-List
if($roots.Count -eq 0){'No SysAdminAssistant processes found.'}
