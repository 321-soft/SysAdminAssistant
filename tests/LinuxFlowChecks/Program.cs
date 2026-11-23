using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
var temp=Path.Combine(Path.GetTempPath(),"saa-linux-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try {
 var binaries=new Dictionary<string,string>{{"SysAdminAssistant.AgentLinux",new string('A',64)}};
 var store=new AgentStore(temp,new string('A',64),v=>v,v=>v,binaries);
 var signed=store.Package(new AgentPackageRequest(1,"https://192.168.1.2:7443/",["192.168.1.2/32"],binaries,Platform:"Linux"),"Fixture");
 var bootstrap=AgentWire.Verify(signed);if(bootstrap.Binaries.Count!=1)throw new Exception("Invalid package");
 using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
 var req=new AgentEnrollment(bootstrap.EnrollmentId,bootstrap.Token,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),"","fixture");
 req=req with {Proof=Convert.ToBase64String(key.SignData(AgentWire.EnrollmentBytes(req),HashAlgorithmName.SHA256))};
 var result=store.Enroll(req);if(result.Policy.StandardServices)throw new Exception("Windows standard enabled");
 store.Approve(1);
 if(store.Get(1)?.Platform!="Linux")throw new Exception("Linux platform was not exposed before first report");
 try{
  store.Package(new AgentPackageRequest(1,"https://192.168.1.2:7443/",["192.168.1.2/32"],binaries,Upgrade:true),"Fixture");
  throw new Exception("Wrong platform accepted");
 }catch(ArgumentException e)when(e.Message.Contains("платформе")){}
 Console.WriteLine("PASS: Registered Linux agent cannot receive a Windows upgrade package");
 var report=new AgentReport{MachineName="fixture",Os="Linux Debian",CpuCount=2,CpuPercent=10,MemoryTotalBytes=100,MemoryAvailableBytes=80,UptimeSeconds=3600,CapturedUtc=DateTime.UtcNow,AppliedPolicyVersion=result.Policy.Version};
 var env=new AgentEnvelope(result.AgentId,1,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),JsonSerializer.Serialize(report,AgentWire.Json),"");
 env=env with {Signature=Convert.ToBase64String(key.SignData(AgentWire.Bytes(env),HashAlgorithmName.SHA256))};
 var accepted=store.Accept(env);if(accepted.Policy.StandardServices||AgentChecks.Evaluate(accepted.Report,accepted.Policy).Any(f=>f.Key.StartsWith("service:")))throw new Exception("Incorrect Linux policy");
 try{store.Accept(env with {Sequence=2,Signature=Convert.ToBase64String(new byte[64])});throw new Exception("Invalid signature accepted");}catch(UnauthorizedAccessException){}
 var catalog=Path.Combine(AppContext.BaseDirectory,"AgentTemplate","Linux");Directory.CreateDirectory(catalog);var catalogBinary=Path.Combine(catalog,"SysAdminAssistant.AgentLinux");
 if(File.Exists(catalogBinary))throw new Exception("Existing release template: test refused to overwrite");
 try{
  File.WriteAllText(catalogBinary,"signed Linux binary fixture");
  AgentReleaseRequest Proof(string? scope=null){
   var proof=new AgentReleaseRequest(result.AgentId,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),"",scope);
   return proof with {Signature=Convert.ToBase64String(key.SignData(AgentRelease.Challenge(proof),HashAlgorithmName.SHA256))};
  }
  void Denied(Action request){try{request();throw new Exception("Unauthorized proof accepted");}catch(UnauthorizedAccessException){}}
  var windowsScope=Proof(AgentRelease.WindowsRuntimeScope);
  Denied(()=>store.Release(windowsScope));
  Denied(()=>store.LinuxRelease(windowsScope));
  Denied(()=>{using var ignored=store.OpenLinuxReleaseFile(windowsScope);});
  var updateRequest=Proof("linux-manifest");
  Denied(()=>{using var ignored=store.OpenLinuxReleaseFile(updateRequest);});
  var manifest=store.LinuxRelease(updateRequest);
  Denied(()=>store.LinuxRelease(updateRequest));
  var release=LinuxAgentRelease.Verify(manifest,bootstrap.IssuerCertificate,result.AgentId,updateRequest.Nonce,"0.0.0");
  var download=Proof("linux-file");
  using(var stream=store.OpenLinuxReleaseFile(download))LinuxAgentRelease.VerifyFile(stream,release);
  Denied(()=>{using var ignored=store.OpenLinuxReleaseFile(download);});
  var legacy=Proof();
  Denied(()=>store.Release(legacy));
  using(var stream=store.OpenLinuxReleaseFile(legacy))LinuxAgentRelease.VerifyFile(stream,release);
  Denied(()=>store.LinuxRelease(legacy));
  using(var stream=store.OpenLinuxReleaseFile(Proof()))LinuxAgentRelease.VerifyFile(stream,release);
  Denied(()=>{using var ignored=store.OpenLinuxReleaseFile(Proof());});
  File.AppendAllText(catalogBinary," changed");
  var changedRequest=Proof("linux-manifest");
  var changedRelease=LinuxAgentRelease.Verify(store.LinuxRelease(changedRequest),bootstrap.IssuerCertificate,result.AgentId,changedRequest.Nonce,"0.0.0");
  if(changedRelease.Length<=release.Length||changedRelease.Sha256==release.Sha256)throw new Exception("Stale release cache");
  try{using var changed=new MemoryStream(new byte[release.Length]);LinuxAgentRelease.VerifyFile(changed,release);throw new Exception("Modified update accepted");}catch(CryptographicException){}
  Console.WriteLine("PASS: Separate Windows/Linux scopes, one-time proofs, legacy agents, download budget and refreshed artifact hash");
  try{LinuxAgentRelease.Verify(manifest,bootstrap.IssuerCertificate,result.AgentId,Guid.NewGuid().ToString("N"),"0.0.0");throw new Exception("Replay nonce accepted");}catch(CryptographicException){}
  try{LinuxAgentRelease.Verify(manifest,bootstrap.IssuerCertificate,result.AgentId,updateRequest.Nonce,"999.0.0");throw new Exception("Downgrade accepted");}catch(CryptographicException){}
 }finally{File.Delete(catalogBinary);}
 Console.WriteLine("PASS: Linux signed update, altered binary, nonce and downgrade rejection");
 Console.WriteLine("PASS: signed Linux package, enrollment, approval, report, signature rejection");
}finally{Directory.Delete(temp,true);}
