param([string]$Thumbprint=$env:SAA_SIGNING_CERT_THUMBPRINT,[string]$ReleaseDirectory='',[string]$TimestampServer='')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($ReleaseDirectory)){
    [xml]$props=Get-Content -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Directory.Build.props') -Raw
    $version=[string]$props.Project.PropertyGroup.Version
    if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Некорректная версия в Directory.Build.props'}
    $ReleaseDirectory=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ("publish\Release_$version\Client\AgentTemplate")
}
$normalized=$Thumbprint.Replace(' ','').ToUpperInvariant()
if($normalized -notmatch '^[0-9A-F]{40}$'){throw 'Неверный отпечаток сертификата подписи кода'}
$cert=Get-Item -LiteralPath "Cert:\CurrentUser\My\$normalized" -ErrorAction SilentlyContinue
if(-not $cert){$cert=Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalized" -ErrorAction Stop}
if(-not $cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date)){throw 'Нет действительного закрытого ключа подписи кода'}
if(-not ($cert.EnhancedKeyUsageList | Where-Object {$_.ObjectId.Value -eq '1.3.6.1.5.5.7.3.3'})){throw 'Сертификат не предназначен для подписи кода'}
$files=Get-ChildItem -LiteralPath $ReleaseDirectory -Recurse -File | Where-Object {$_.Name -ne 'AgentSetupLauncher.exe' -and $_.Extension -in @('.exe','.dll','.ps1')}
if(-not $files){throw 'Нет опубликованных файлов агента'}
foreach($file in $files){
    $parameters=@{FilePath=$file.FullName;Certificate=$cert;HashAlgorithm='SHA256'}
    if($TimestampServer){$parameters.TimestampServer=$TimestampServer}
    $signature=Set-AuthenticodeSignature @parameters
    if($signature.Status -ne 'Valid'){throw "Подпись не прошла проверку доверия: $($file.Name) / $($signature.Status)"}
    Write-Host "Signed: $($file.Name)"
}
Write-Host 'Подпись издателя проверена. Создавай персональные пакеты из клиента после этого шага.'
