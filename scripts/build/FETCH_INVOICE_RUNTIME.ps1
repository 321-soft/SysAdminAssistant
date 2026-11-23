$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cache=Join-Path $root 'RuntimePackages'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$target=Join-Path $cache 'vc_redist.x64.exe'
if(!(Test-Path $target)){
 $temp=$target+'.download'
 try{[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;Invoke-WebRequest -UseBasicParsing -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $temp
  $sig=Get-AuthenticodeSignature -FilePath $temp
  if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Microsoft Corporation*'){throw 'VC++ installer signature is not Microsoft / valid'}
  Move-Item $temp $target -Force
 }finally{if(Test-Path $temp){Remove-Item $temp -Force}}
}
$sig=Get-AuthenticodeSignature -FilePath $target
if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike '*Microsoft Corporation*'){Remove-Item $target -Force;throw 'Cached VC++ installer signature is not valid'}
