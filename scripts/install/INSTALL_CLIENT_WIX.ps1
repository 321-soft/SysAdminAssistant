param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$version = '4.0.6'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet) -or -not (@(& $dotnet --list-sdks) -match '^10\.')) {
    throw 'WiX requires the x64 .NET 10 SDK.'
}
$root = Join-Path $env:ProgramFiles 'SysAdmin Assistant Build Tools'
$target = Join-Path $root "wix-$version"
foreach ($path in @($root, $target)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'WiX installation directory is a filesystem link.'
    }
}
$wix = Join-Path $target 'wix.exe'
if (Test-Path -LiteralPath $wix -PathType Leaf) {
    & $wix --version
    if ($LASTEXITCODE -eq 0) { return }
    throw 'The existing WiX installation cannot start.'
}
if (Test-Path -LiteralPath $target) { throw 'The WiX installation folder already exists without wix.exe.' }
[IO.Directory]::CreateDirectory($root) | Out-Null
$stage = Join-Path $root ('wix-stage-' + [Guid]::NewGuid().ToString('N'))
$config = Join-Path $PSScriptRoot ('wix-nuget-' + [Guid]::NewGuid().ToString('N') + '.config')
try {
    $package = Join-Path $PackageDirectory "wix.$version.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw 'WiX package is missing.' }
    $feed = [Security.SecurityElement]::Escape($PackageDirectory)
    [IO.File]::WriteAllText($config, ('<configuration><packageSources><clear/><add key="BundledWiX" value="' + $feed + '"/></packageSources></configuration>'))
    & $dotnet tool install wix --version $version --tool-path $stage --configfile $config --allow-roll-forward
    if ($LASTEXITCODE -ne 0) { throw 'WiX installation failed.' }
    & (Join-Path $stage 'wix.exe') --version
    if ($LASTEXITCODE -ne 0) { throw 'WiX could not start after installation.' }
    [IO.Directory]::Move($stage, $target)
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    if (Test-Path -LiteralPath $config) { Remove-Item -LiteralPath $config -Force }
}
