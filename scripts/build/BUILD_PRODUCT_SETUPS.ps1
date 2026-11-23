param([Parameter(Mandatory=$true)][string]$ReleaseDirectory,[string]$PublicBaseUrl='')
$ErrorActionPreference='Stop'
[xml]$props=Get-Content (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'Directory.Build.props') -Raw;$version=[string]$props.Project.PropertyGroup.Version
if([string]::IsNullOrWhiteSpace($PublicBaseUrl)){$PublicBaseUrl=$env:SAA_RELEASE_BASE_URL};if([string]::IsNullOrWhiteSpace($PublicBaseUrl)){$PublicBaseUrl='https://assistant.321.su/releases/'+$version}
$root=[IO.Path]::GetFullPath($ReleaseDirectory);$output=Join-Path $root 'Installers';[IO.Directory]::CreateDirectory($output)|Out-Null
$work=Join-Path $root ('setup-build-'+[Guid]::NewGuid().ToString('N'));[IO.Directory]::CreateDirectory($work)|Out-Null
$csc=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';if(-not(Test-Path -LiteralPath $csc)){throw 'Windows .NET Framework compiler unavailable.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\SysAdminAssistant.Core\ProductSetupBundle.cs') -ReferencedAssemblies System.IO.Compression,System.IO.Compression.FileSystem
$packages=@{}
try{
 $metadata=Join-Path $work 'Metadata.cs'
 [IO.File]::WriteAllText($metadata,('[assembly:System.Reflection.AssemblyCompany("321.su")][assembly:System.Reflection.AssemblyProduct("SysAdmin Assistant")][assembly:System.Reflection.AssemblyCopyright("Copyright 2026 Орлов Александр Юрьевич · 321.su")][assembly:System.Reflection.AssemblyDescription("SysAdmin Assistant · assistant.321.su")][assembly:System.Reflection.AssemblyVersion("'+$version+'.0")]'),[Text.Encoding]::UTF8)
 foreach($component in @('Client','Server')){
  $folder=if($component -eq 'Client'){Join-Path $root 'Client'}else{Join-Path $root 'ServerSetup'}
  if($component -eq 'Client'){Copy-Item (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'scripts\install\INSTALL_CLIENT.ps1') -Destination $folder}else{Copy-Item (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'scripts\install\INSTALL_SERVER_BOOTSTRAP.ps1') -Destination $folder}
  $stub=Join-Path $work ($component+'.exe');$zip=Join-Path $work ($component+'.zip');$target=Join-Path $output ('SysAdminAssistant_'+$component+'_Setup.exe')
  $defines=$component.ToUpperInvariant()
  if($component -eq 'Client' -and (Test-Path -LiteralPath (Join-Path $folder 'BuildTools\wix.4.0.6.nupkg') -PathType Leaf)){$defines+=';BUNDLED_WIX'}
  & $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ /warnaserror+ "/define:$defines" "/out:$stub" "/win32manifest:$(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\launcher.manifest')" "/win32icon:$(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\SysAdminAssistant.Desktop\Assets\SysAdminAssistant.ico')" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'docs\legal\USER_AGREEMENT_RU.txt'),EULA.RU" "/resource:$(Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'docs\legal\USER_AGREEMENT_EN.txt'),EULA.EN" (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\ProductLauncher.cs') (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'src\SysAdminAssistant.Core\ProductSetupBundle.cs') $metadata
  if($LASTEXITCODE -ne 0){throw "Cannot build $component setup bootstrap."}
  [IO.Compression.ZipFile]::CreateFromDirectory($folder,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
  if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target}
  [SysAdminAssistant.Core.ProductSetupBundle]::Pack($stub,$zip,$target)
  $ru=[IO.File]::ReadAllText((Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'docs\legal\USER_AGREEMENT_RU.txt')).Replace("`r`n","`n").Replace("`r","`n").TrimEnd();$en=[IO.File]::ReadAllText((Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'docs\legal\USER_AGREEMENT_EN.txt')).Replace("`r`n","`n").Replace("`r","`n").TrimEnd()
  $h=[Security.Cryptography.SHA256]::Create();try{$eula=([BitConverter]::ToString($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($ru+"`n"+$en)))).Replace('-','')}finally{$h.Dispose()}
  $packages[$component]=@{Component=$component.ToLowerInvariant();Version=$version+'.0';MinimumVersion='0.40.0.0';Url=$PublicBaseUrl.TrimEnd('/')+'/'+[IO.Path]::GetFileName($target);Bytes=(Get-Item $target).Length;Sha256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash;EulaSha256=$eula;DatabaseSchema=7}
  if($env:SAA_RELEASE_MIRROR_BASE_URL){$packages[$component].Mirrors=@($env:SAA_RELEASE_MIRROR_BASE_URL.TrimEnd('/')+'/'+[IO.Path]::GetFileName($target))}
 }
 $payload=@{Product='SysAdminAssistant';PublishedUtc=[DateTime]::UtcNow.ToString('o');ExpiresUtc=[DateTime]::UtcNow.AddDays(90).ToString('o');Client=$packages.Client;Server=$packages.Server}|ConvertTo-Json -Depth 5 -Compress
 $bytes=[Text.Encoding]::UTF8.GetBytes($payload);$csp=New-Object Security.Cryptography.CspParameters;$csp.ProviderType=24;$csp.ProviderName='Microsoft Enhanced RSA and AES Cryptographic Provider';$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider -ArgumentList 3072,$csp;$rsa.PersistKeyInCsp=$false
 try{$rsa.FromXmlString([IO.File]::ReadAllText((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SysAdminAssistantPublisher\release-private.xml')));$signature=$rsa.SignData($bytes,'SHA256')}finally{$rsa.Dispose()}
 $manifest=@{Payload=[Convert]::ToBase64String($bytes);Signature=[Convert]::ToBase64String($signature)}|ConvertTo-Json -Compress
 [IO.File]::WriteAllText((Join-Path $output 'release.json'),$manifest,(New-Object Text.UTF8Encoding($false)))
 Write-Host "Installers and signed manifest: $output"
}finally{if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force}}
