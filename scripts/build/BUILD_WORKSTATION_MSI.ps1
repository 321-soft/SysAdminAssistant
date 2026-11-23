param(
    [string]$OutputDirectory = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'publish\Workstation'),
    [string]$Uninstaller,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$wixVersion = '4.0.6'
$candidates = @(
    (Join-Path $env:ProgramFiles "SysAdmin Assistant Build Tools\wix-$wixVersion\wix.exe"),
    (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ".tools\wix-$wixVersion\wix.exe")
)
$command = Get-Command wix.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($command) { $candidates += $command.Source }
$wix = $null
foreach ($candidate in $candidates) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $detected = & $candidate --version
    if ($LASTEXITCODE -eq 0 -and ($detected -match '^4\.0\.6(?:$|[+.-])')) { $wix = $candidate; break }
}
if (-not $wix) {
    # Private build tool: never install into Program Files or the global tool store.
    $toolDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ".tools\wix-$wixVersion"
    $stageTool = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) (".tools\wix-stage-" + [guid]::NewGuid().ToString('N'))
    try {
        Write-Host "Preparing private WiX $wixVersion for this project..."
        & dotnet tool install wix --version $wixVersion --tool-path $stageTool --configfile (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'NuGet.config') --allow-roll-forward
        if ($LASTEXITCODE -ne 0) { throw 'WiX download failed. Check access to NuGet and rerun PUBLISH.bat.' }
        $prepared = Join-Path $stageTool 'wix.exe'
        $detected = & $prepared --version
        if ($LASTEXITCODE -ne 0 -or -not ($detected -match '^4\.0\.6(?:$|[+.-])')) {
            throw 'The downloaded WiX tool failed verification.'
        }
        if (Test-Path -LiteralPath $toolDir) {
            throw "The project tool folder is incomplete. Remove $toolDir and retry."
        }
        [IO.Directory]::Move($stageTool, $toolDir)
        $wix = Join-Path $toolDir 'wix.exe'
    } finally {
        if (Test-Path -LiteralPath $stageTool) { Remove-Item -LiteralPath $stageTool -Recurse -Force }
    }
}
if ($PrepareOnly) { return }
[xml]$props = Get-Content (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$stage = Join-Path ([IO.Path]::GetTempPath()) ('saa-station-' + [guid]::NewGuid().ToString('N'))
try {
    $null = New-Item -ItemType Directory -Force -Path $OutputDirectory
    $project = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\SysAdminAssistant.Agent\SysAdminAssistant.Agent.csproj'
    & dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Agent publish failed.' }
    $exe = Join-Path $stage 'SysAdminAssistant.Agent.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Agent executable not found.' }
    $output = Join-Path $OutputDirectory 'SysAdminAssistant_Workstation_x64.msi'
    if([string]::IsNullOrWhiteSpace($Uninstaller)){$Uninstaller=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('publish\Release_'+$version+'\WorkstationTools\Uninstall.exe')}
    if(-not(Test-Path -LiteralPath $Uninstaller -PathType Leaf)){throw 'Native workstation uninstaller is missing. Run PUBLISH.bat to build the complete release.'}
    & $wix build -arch x64 -d "StationVersion=$version" -d "AgentExe=$exe" -d "UninstallExe=$Uninstaller" -o $output (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\Workstation.wxs')
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
    Write-Output $output
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
