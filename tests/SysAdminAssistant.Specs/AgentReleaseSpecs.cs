using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class AgentReleaseSpecs
{
 // Exact wire fields from the 0.42.10 updater, using the same strict JSON settings.
 private sealed record LegacyRelease(string AgentId,string Nonce,string Version,DateTime ExpiresUtc,
     Dictionary<string,AgentReleaseFile> Agent,Dictionary<string,AgentReleaseFile> Setup,AgentSignedTrust Trust,int FrameworkMajor);
 public static void Run(string folder,Action<string,bool> check){
 bool Reject(Action f){try{f();return false;}catch(Exception e)when(e is CryptographicException or ArgumentException or JsonException or UnauthorizedAccessException or InvalidOperationException){return true;}}
 using var ca=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var cert=new CertificateRequest("CN=Release fixture",ca,HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(1));var issuer=Convert.ToBase64String(cert.RawData);var id=Guid.NewGuid().ToString("N");var nonce=Guid.NewGuid().ToString("N");var bytes=new byte[]{10,20,30};var file=new AgentReleaseFile(bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
 AgentSignedTrust Trust(){var p=JsonSerializer.Serialize(new AgentTrustDocument(2,[new string('A',64)]),AgentWire.Json);return new(p,Convert.ToBase64String(ca.SignData(Encoding.UTF8.GetBytes("SAA-AGENT-TRUST-1\n"+p),HashAlgorithmName.SHA256)));}
 var release=new AgentReleaseDocument(id,nonce,"0.12.3.0",DateTime.UtcNow.AddMinutes(5),new(){["SysAdminAssistant.Agent.exe"]=file,["SysAdminAssistant.Agent.dll"]=file,["SysAdminAssistant.Agent.deps.json"]=file,["SysAdminAssistant.Agent.runtimeconfig.json"]=file,["SysAdminAssistant.UpdateProbe.exe"]=file,["SysAdminAssistant.UpdateProbe.dll"]=file,["SysAdminAssistant.UpdateProbe.deps.json"]=file,["SysAdminAssistant.UpdateProbe.runtimeconfig.json"]=file},new(){["SysAdminAssistant.AgentSetup.exe"]=file,["SysAdminAssistant.AgentSetup.dll"]=file,["SysAdminAssistant.AgentSetup.deps.json"]=file,["SysAdminAssistant.AgentSetup.runtimeconfig.json"]=file},Trust());
 AgentSignedRelease Sign(AgentReleaseDocument d){var p=JsonSerializer.Serialize(d,AgentWire.Json);return new(p,Convert.ToBase64String(ca.SignData(AgentRelease.Content(p),HashAlgorithmName.SHA256)));}
 AgentReleaseDocument Verify(AgentSignedRelease s,string expectedNonce="",string expectedId="",string current="0.12.2.0")=>AgentRelease.Verify(s,issuer,expectedId==""?id:expectedId,expectedNonce==""?nonce:expectedNonce,current);
 var signed=Sign(release);check("Agent release server signature and full manifest accepted",Verify(signed).Version=="0.12.3.0");
 check("Legacy agent release without Desktop Runtime stays valid",Verify(signed).Runtime==null&&
  JsonSerializer.Deserialize<LegacyRelease>(signed.Payload,AgentWire.Json)?.Version==release.Version);
 check("Runtime is limited to authenticated capability or known bridge release",
  !AgentRelease.SupportsWindowsRuntime(null,"0.42.10.0")&&!AgentRelease.SupportsWindowsRuntime(null,null)&&
  AgentRelease.SupportsWindowsRuntime(null,"0.42.12.0")&&AgentRelease.SupportsWindowsRuntime(null,"0.42.13.0")&&
  !AgentRelease.SupportsWindowsRuntime("linux-manifest","0.42.13.0")&&
  AgentRelease.SupportsWindowsRuntime(AgentRelease.WindowsRuntimeScope,""));
 var withRuntime=Sign(release with{Runtime=file with{Length=60_000_000}});
 check("Desktop Runtime uses its own signed and bounded package descriptor",Verify(withRuntime).Runtime?.Length==60_000_000&&
  Reject(()=>Verify(Sign(release with{Runtime=file with{Length=AgentDesktopRuntime.MaxBytes+1}})))&&
  Reject(()=>Verify(withRuntime with{Payload=withRuntime.Payload.Replace("60000000","60000001")})));
 check("Agent signature cannot authorize an arbitrary executable as Microsoft Runtime",!AgentDesktopRuntime.MatchesMicrosoftPackage(bytes));
 var frameworks=Path.Combine(Path.GetTempPath(),"saa-runtime-test-"+Guid.NewGuid().ToString("N"));
 try{
  void Framework(string version,bool installed=true){var path=Directory.CreateDirectory(Path.Combine(frameworks,version)).FullName;if(installed)File.WriteAllText(Path.Combine(path,"framework.dll"),"test");}
  Framework("10.0.13");Framework("11.0.0");Framework("10.0.15-preview");Framework("10.0.16",false);
  check("Desktop Runtime rejects old, preview, incomplete and other major installs",!AgentDesktopRuntime.HasRequiredFramework(frameworks,"framework.dll"));
  Framework("10.0.14");
  check("Desktop Runtime accepts installed required x64 framework patch",AgentDesktopRuntime.HasRequiredFramework(frameworks,"framework.dll"));
 }finally{Directory.Delete(frameworks,true);}
 check("Agent release altered payload rejected",Reject(()=>Verify(signed with{Payload=signed.Payload.Replace("0.12.3.0","0.12.4.0")})));
 using var alien=ECDsa.Create(ECCurve.NamedCurves.nistP256);check("Agent release other signing key rejected",Reject(()=>Verify(signed with{Signature=Convert.ToBase64String(alien.SignData(AgentRelease.Content(signed.Payload),HashAlgorithmName.SHA256))})));
 check("Agent release previous request nonce rejected",Reject(()=>Verify(signed,Guid.NewGuid().ToString("N"))));
 check("Agent release another enrollment rejected",Reject(()=>Verify(signed,expectedId:Guid.NewGuid().ToString("N"))));
 check("Agent release downgrade rejected",Reject(()=>Verify(signed,current:"0.12.4.0")));
 check("Agent release same version allows harmless no-update response",Verify(signed,current:"0.12.3.0").Version=="0.12.3.0");
 check("Agent release expired manifest rejected",Reject(()=>Verify(Sign(release with{ExpiresUtc=DateTime.UtcNow.AddMinutes(-1)}))));
 check("Agent release excessive validity rejected",Reject(()=>Verify(Sign(release with{ExpiresUtc=DateTime.UtcNow.AddHours(1)}))));
 check("Agent release framework migration needs explicit installation",Reject(()=>Verify(Sign(release with{FrameworkMajor=11}))));
 check("Agent release major application version accepted on same runtime",Verify(Sign(release with{Version="1.0.0.0"})).Version=="1.0.0.0");
 check("Agent release rejects traversal reserved names and executable scripts",new[]{"../evil.dll","C:\\evil.exe","folder/evil.dll","CON.exe","COM1.dll","x.ps1","x.cmd",".hidden.exe","x.exe:stream"}.All(n=>!AgentRelease.FileName(n)));
 var unsafeFiles=new Dictionary<string,AgentReleaseFile>(release.Agent){["../evil.dll"]=file};check("Agent release unsafe signed file name still rejected",Reject(()=>Verify(Sign(release with{Agent=unsafeFiles}))));
 check("Agent release incomplete signed package rejected",Reject(()=>Verify(Sign(release with{Setup=new(){["helper.dll"]=file}}))));
 var large=new Dictionary<string,AgentReleaseFile>(release.Agent){["huge.dll"]=file with{Length=30000001}};check("Agent release oversized file rejected before download",Reject(()=>Verify(Sign(release with{Agent=large}))));
 var duplicate=new Dictionary<string,AgentReleaseFile>(release.Agent){["sysadminassistant.agent.dll"]=file};check("Agent release rejects Windows case insensitive file collisions",Reject(()=>Verify(Sign(release with{Agent=duplicate}))));
 AgentRelease.VerifyFile(bytes,file);check("Agent release downloaded bytes hash verified",true);
 check("Agent release modified or truncated file rejected",Reject(()=>AgentRelease.VerifyFile([10,20,31],file))&&Reject(()=>AgentRelease.VerifyFile([10,20],file)));
 var request=new AgentReleaseRequest(id,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),nonce,"");check("Agent release signature cannot be reused for report protocol",!AgentRelease.Challenge(request).SequenceEqual(AgentWire.Bytes(new(id,1,request.Timestamp,"","")))&&Encoding.UTF8.GetString(AgentRelease.Challenge(request)).StartsWith("SAA-AGENT-UPDATE-1\n"));
 check("Installer missing task recognizes CLR and COM HRESULT mapping",InstallerTaskErrors.Missing(new FileNotFoundException())&&InstallerTaskErrors.Missing(new DirectoryNotFoundException())&&InstallerTaskErrors.Missing(new COMException("missing",unchecked((int)0x80070002))));
 check("Installer access denied is never treated as absent task",!InstallerTaskErrors.Missing(new UnauthorizedAccessException())&&!InstallerTaskErrors.Missing(new COMException("denied",unchecked((int)0x80070005))));
 var store=new AgentStore(Path.Combine(folder,"release-auth"),new string('A',64),s=>s,s=>s,new Dictionary<string,string>{["SysAdminAssistant.Agent.exe"]=new string('B',64)});var bootstrap=AgentWire.Verify(store.Package(new(1,"https://192.0.2.1:7443/",["192.0.2.1/32"],new(){["SysAdminAssistant.Agent.exe"]=new string('B',64)}),"Fixture"));using var local=ECDsa.Create(ECCurve.NamedCurves.nistP256);var enrollment=new AgentEnrollment(bootstrap.EnrollmentId,bootstrap.Token,Convert.ToBase64String(local.ExportSubjectPublicKeyInfo()),"","Fixture");enrollment=enrollment with{Proof=Convert.ToBase64String(local.SignData(AgentWire.EnrollmentBytes(enrollment),HashAlgorithmName.SHA256))};var enrolled=store.Enroll(enrollment);
 AgentReleaseRequest Req(long? stamp=null,string? scope=null){var q=new AgentReleaseRequest(enrolled.AgentId,stamp??DateTimeOffset.UtcNow.ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),"",scope);return q with{Signature=Convert.ToBase64String(local.SignData(AgentRelease.Challenge(q),HashAlgorithmName.SHA256))};}
 check("Agent updater unapproved identity rejected before catalog access",Reject(()=>store.Release(Req())));store.Approve(1);
 check("Agent updater forged request rejected",Reject(()=>store.Release(Req() with{Signature=Convert.ToBase64String(alien.SignData(AgentRelease.Challenge(Req()),HashAlgorithmName.SHA256))})));
 check("Agent updater timestamp extreme cannot overflow validation",Reject(()=>store.Release(Req(long.MinValue)))&&Reject(()=>store.Release(Req(long.MaxValue))));
 check("Agent updater expired request rejected",Reject(()=>store.Release(Req(DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds()))));
 var catalog=Path.Combine(AppContext.BaseDirectory,"AgentTemplate");if(Directory.Exists(catalog))throw new IOException("Fixture catalog already exists; refusing to overwrite");
 try{var agentDir=Directory.CreateDirectory(Path.Combine(catalog,"Agent")).FullName;var setupDir=Directory.CreateDirectory(Path.Combine(catalog,"Setup")).FullName;foreach(var name in release.Agent.Keys)File.WriteAllBytes(Path.Combine(agentDir,name),name.EndsWith(".dll")?File.ReadAllBytes(typeof(AgentReport).Assembly.Location):bytes);foreach(var name in release.Setup.Keys)File.WriteAllBytes(Path.Combine(setupDir,name),bytes);
 var q=Req();var manifest=store.Release(q);var verified=AgentRelease.Verify(manifest,bootstrap.IssuerCertificate,enrolled.AgentId,q.Nonce,"0.0");check("Agent updater authenticates enrollment and serves CA signed catalog",verified.AgentId==enrolled.AgentId&&verified.Agent.Count==8&&verified.Setup.Count==4);
 check("0.42.10 strict parser reads the actual signed catalog",
     JsonSerializer.Deserialize<LegacyRelease>(manifest.Payload,AgentWire.Json)?.AgentId==enrolled.AgentId);
 var scoped=Req(scope:AgentRelease.WindowsRuntimeScope);
 check("Windows Runtime capability is authenticated and cannot authorize Linux",
     AgentRelease.Verify(store.Release(scoped),bootstrap.IssuerCertificate,enrolled.AgentId,scoped.Nonce,"0.0").AgentId==enrolled.AgentId&&
     Reject(()=>store.Release(q with{Scope=AgentRelease.WindowsRuntimeScope}))&&
     Reject(()=>store.LinuxRelease(scoped))&&Reject(()=>store.Release(Req(scope:"linux-manifest"))));
 check("Legacy Windows updater cannot request the Runtime executable",
     Reject(()=>store.ReleaseFile(new(Req(),"Runtime",AgentDesktopRuntime.FileName))));
 var actual=store.ReleaseFile(new(Req(),"Agent","SysAdminAssistant.Agent.dll"));AgentRelease.VerifyFile(actual,verified.Agent["SysAdminAssistant.Agent.dll"]);check("Agent updater served file matches signed catalog",actual.SequenceEqual(File.ReadAllBytes(typeof(AgentReport).Assembly.Location)));
 check("Agent updater cannot download another directory",Reject(()=>store.ReleaseFile(new(Req(),"../Setup","SysAdminAssistant.Agent.dll"))));
 var report=new AgentReport{MachineName="Fixture",CpuCount=1,CapturedUtc=DateTime.UtcNow};var envelope=new AgentEnvelope(enrolled.AgentId,1,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),JsonSerializer.Serialize(report,AgentWire.Json),"");envelope=envelope with{Signature=Convert.ToBase64String(local.SignData(AgentWire.Bytes(envelope),HashAlgorithmName.SHA256))};check("Agent updater does not consume telemetry sequence",store.Accept(envelope).DeviceId==1);
 }finally{Directory.Delete(catalog,true);}
 check("Agent updater file path validated before catalog access",Reject(()=>store.ReleaseFile(new(Req(),"Agent","../evil.dll"))));store.Revoke(1);
 check("Agent updater revoked identity cannot fetch package",Reject(()=>store.ReleaseFile(new(Req(),"Agent","SysAdminAssistant.Agent.exe"))));
 }
}
