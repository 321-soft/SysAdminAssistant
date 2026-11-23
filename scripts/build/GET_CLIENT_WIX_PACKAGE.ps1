param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$version = '4.0.6'
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$package = Join-Path $OutputDirectory "wix.$version.nupkg"
$temp = $package + '.partial'
try {
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -Uri "https://api.nuget.org/v3-flatcontainer/wix/$version/wix.$version.nupkg" -OutFile $temp
    Move-Item -LiteralPath $temp -Destination $package -Force
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
