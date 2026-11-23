$ErrorActionPreference='Stop'
# Windows PowerShell 5.1 / Server 2016: explicit TLS 1.2, no curl.exe dependency.
[Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
function Valid-Package($path,$hash) {
 if(-not (Test-Path -LiteralPath $path -PathType Leaf)) {return $false}
 return (Get-FileHash -LiteralPath $path -Algorithm SHA512).Hash -eq $hash
}
function Download-Package([string]$url,[string]$path) {
 $uri=[Uri]$url
 if($uri.Scheme -ne 'https' -or $uri.Host -notin @('dotnetcli.blob.core.windows.net','builds.dotnet.microsoft.com')) {throw 'Untrusted runtime URL'}
 $request=[Net.HttpWebRequest]::Create($uri)
 $request.Timeout=30000; $request.ReadWriteTimeout=30000
 $request.UserAgent='SysAdminAssistant-RuntimeFetcher/0.6.1'
 $response=$null; $inputStream=$null; $outputStream=$null
 try {
  $response=$request.GetResponse()
  $length=$response.ContentLength
  $inputStream=$response.GetResponseStream()
  $outputStream=[IO.File]::Create($path)
  $buffer=New-Object byte[] 65536
  $received=[long]0; $watch=[Diagnostics.Stopwatch]::StartNew()
  while(($count=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0) {
   if($watch.Elapsed.TotalMinutes -gt 10) {throw 'Runtime download exceeded 10 minutes'}
   $outputStream.Write($buffer,0,$count);$received+=$count
   if($length -gt 0) {Write-Progress -Activity 'Downloading Microsoft .NET Runtime' -Status ([IO.Path]::GetFileName($path)) -PercentComplete ([Math]::Min(100,[int](100*$received/$length)))}
  }
  if($length -gt 0 -and $received -ne $length) {throw 'Incomplete runtime download'}
 } finally {
  if($outputStream) {$outputStream.Dispose()}
  if($inputStream) {$inputStream.Dispose()}
  if($response) {$response.Dispose()}
  Write-Progress -Activity 'Downloading Microsoft .NET Runtime' -Completed
 }
}
try {
 $manifest=Get-Content -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'runtime-packages.json') -Raw | ConvertFrom-Json
 $directory=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'RuntimePackages'
 [void][IO.Directory]::CreateDirectory($directory)
 foreach($package in $manifest.packages) {
  if($package.file -notmatch '^(windowsdesktop|aspnetcore)-runtime-10\.0\.\d+-win-x64\.exe$' -or $package.sha512 -notmatch '^[a-fA-F0-9]{128}$') {throw 'Invalid runtime manifest'}
  $target=Join-Path $directory $package.file
  if(Valid-Package $target $package.sha512) {Write-Host ('Verified cached runtime: '+$package.file);continue}
  $temporary=$target+'.download';$downloaded=$false
  foreach($url in $package.urls) {
   try {
    Write-Host ('Downloading '+$package.file+' from '+([Uri]$url).Host)
    Download-Package $url $temporary
    if(-not (Valid-Package $temporary $package.sha512)) {throw 'SHA-512 mismatch: incomplete or unexpected installer'}
    Move-Item -LiteralPath $temporary -Destination $target -Force
    $downloaded=$true;break
   } catch {
    Write-Host ('Download failed: '+$_.Exception.Message) -ForegroundColor Yellow
    if(Test-Path -LiteralPath $temporary) {Remove-Item -LiteralPath $temporary -Force}
   }
  }
  if(-not $downloaded) {throw ('All Microsoft download sources failed for '+$package.file)}
 }
 Write-Host 'Runtime installers downloaded and verified.'
 exit 0
} catch {
 Write-Host ('Runtime download failed: '+$_.Exception.Message) -ForegroundColor Red
 Write-Host 'PUBLISH.cmd can continue without bundling runtime installers.'
 exit 1
}
