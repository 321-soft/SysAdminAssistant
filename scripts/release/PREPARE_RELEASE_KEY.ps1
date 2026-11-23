$ErrorActionPreference='Stop'
$directory=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SysAdminAssistantPublisher'
[IO.Directory]::CreateDirectory($directory) | Out-Null
$acl=New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,(New-Object Security.Principal.SecurityIdentifier('S-1-5-18')))){$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
Set-Acl -LiteralPath $directory -AclObject $acl
$private=Join-Path $directory 'release-private.xml'
$csp=New-Object Security.Cryptography.CspParameters;$csp.ProviderType=24;$csp.ProviderName='Microsoft Enhanced RSA and AES Cryptographic Provider'
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider -ArgumentList 3072,$csp
$rsa.PersistKeyInCsp=$false
try{
 if(Test-Path -LiteralPath $private){$rsa.FromXmlString([IO.File]::ReadAllText($private))}else{[IO.File]::WriteAllText($private,$rsa.ToXmlString($true))}
 if($rsa.KeySize -lt 3072){throw 'Publisher key must be RSA 3072 or stronger.'}
 $public=Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'packaging\ReleasePublicKey.xml';$xml=$rsa.ToXmlString($false)
 $existing=if(Test-Path -LiteralPath $public){[IO.File]::ReadAllText($public)}else{''}
 if($existing.Contains('<RSAKeyValue>') -and $existing.Trim() -ne $xml){throw 'Publisher key differs from this source. Restore the original private key; do not silently change update trust.'}
 [IO.File]::WriteAllText($public,$xml)
 Write-Host 'Publisher key ready. Back up the private key outside Git.'
}finally{$rsa.Dispose()}
