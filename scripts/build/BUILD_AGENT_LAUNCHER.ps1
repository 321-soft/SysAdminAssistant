param([string]$OutputPath='')
$ErrorActionPreference='Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    [xml]$props=Get-Content -LiteralPath (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Directory.Build.props') -Raw
    $version=[string]$props.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Некорректная версия в Directory.Build.props' }
    $OutputPath=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ("publish\Release_$version\Client\AgentTemplate\AgentSetupLauncher.exe")
}
$out=[IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($out)) | Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc=Join-Path $framework 'csc.exe'
if(-not (Test-Path -LiteralPath $csc)){throw 'Не найден системный компилятор .NET Framework 4. Публикация выполняется на Windows.'}
$manifest=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\launcher.manifest'
$launcher=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\AgentLauncher.cs'
$bundle=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\SysAdminAssistant.Core\AgentSetupBundle.cs'
foreach($source in @($manifest,$launcher,$bundle)){if(-not(Test-Path -LiteralPath $source -PathType Leaf)){throw "Нет исходного файла: $source"}}
& $csc /nologo /target:winexe "/win32icon:$(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\SysAdminAssistant.Desktop\Assets\SysAdminAssistant.ico')" /platform:anycpu /codepage:65001 /optimize+ /warnaserror+ "/out:$out" "/win32manifest:$manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $launcher $bundle
if($LASTEXITCODE -ne 0){throw 'Не удалось собрать оболочку установщика агента'}
if(-not (Test-Path -LiteralPath $out -PathType Leaf)){throw "Компилятор не создал файл: $out"}
