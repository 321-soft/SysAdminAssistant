using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.AgentLinux;
using SysAdminAssistant.Core;
if(!OperatingSystem.IsLinux())throw new Exception("Linux required");
void Check(string name,bool ok){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);}
var root=Path.Combine(Path.GetTempPath(),"saa-apply-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
 using var issuer=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var cert=new CertificateRequest("CN=test",issuer,HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
 var id=Guid.NewGuid().ToString("N");var b=new AgentBootstrap{EnrollmentId=id,ServerUrl="https://192.168.1.2:7443/",ServerSha256=new string('A',64),AllowedNetworks=["192.168.1.2/32"],IssuerCertificate=Convert.ToBase64String(cert.RawData),Binaries=new(){{"agent",new string('A',64)}}};
 var payload=JsonSerializer.Serialize(b,AgentWire.Json);var config=Path.Combine(root,"bootstrap.json");File.WriteAllText(config,JsonSerializer.Serialize(new AgentSignedBootstrap(payload,Convert.ToBase64String(issuer.SignData(Encoding.UTF8.GetBytes(payload),HashAlgorithmName.SHA256))),AgentWire.Json));
 var stage=Path.Combine(root,"update-staging");var program=Path.Combine(root,"agent");var good=Encoding.ASCII.GetBytes("new signed executable");
 LinuxAgentSignedRelease Sign(string aid,string version,byte[] data,DateTime? expires=null){var document=new LinuxAgentReleaseDocument(aid,Guid.NewGuid().ToString("N"),version,expires??DateTime.UtcNow.AddMinutes(10),data.Length,Convert.ToHexString(SHA256.HashData(data)));var p=JsonSerializer.Serialize(document,AgentWire.Json);return new(p,Convert.ToBase64String(issuer.SignData(LinuxAgentRelease.Content(p),HashAlgorithmName.SHA256)));}
 void Set(LinuxAgentSignedRelease manifest,byte[] data){Directory.CreateDirectory(stage);File.WriteAllBytes(Path.Combine(stage,"release.bin"),data);File.WriteAllText(Path.Combine(stage,"release.json"),JsonSerializer.Serialize(manifest,AgentWire.Json));}
 void Reset(){File.WriteAllText(program,"old executable");}
 void Reject(string code){try{LinuxUpdateApply.Apply(stage,config,program,"0.9.0",_=>throw new Exception("service must not be touched"));throw new Exception("invalid package accepted");}catch(LinuxReleaseException e){Check("precise rejection "+code,e.Code==code);}Check("rejected staging removed",!Directory.Exists(stage));Check("next tick succeeds without concealing rejection",LinuxUpdateApply.Apply(stage,config,program,"0.9.0",_=>true).Contains("не загружено")&&File.ReadAllText(Path.Combine(root,"update-result.json")).Contains("Rejected"));}
 var signed=Sign(id,"1.0.0",good);Reset();Set(signed,good);var result=LinuxUpdateApply.Apply(stage,config,program,"0.9.0",_=>true);
 Check("valid signed update installed",File.ReadAllBytes(program).SequenceEqual(good)&&result.Contains("1.0.0"));Check("successful transaction consumed queue and kept previous binary",!Directory.Exists(stage)&&File.ReadAllText(Path.Combine(root,"agent.previous"))=="old executable"&&!File.Exists(Path.Combine(root,".update-in-progress")));
 var receipt=File.ReadAllText(Path.Combine(root,"update-result.json"));Check("repeat tick is no-op",LinuxUpdateApply.Apply(stage,config,program,"1.0.0",_=>throw new Exception()).Contains("не загружено")&&receipt==File.ReadAllText(Path.Combine(root,"update-result.json")));
 // Reproduce 0.31.2's order: expiry validation throws before its same-version check.
 var expired=Sign(id,"1.0.0",good,DateTime.UtcNow.AddMinutes(-1));try{LinuxAgentRelease.Verify(expired,b.IssuerCertificate,id,JsonSerializer.Deserialize<LinuxAgentReleaseDocument>(expired.Payload,AgentWire.Json)!.Nonce,"1.0.0");throw new Exception("expiry not detected");}catch(LinuxReleaseException e){Check("legacy validation order reproduces expiry failure for installed version",e.Code=="ManifestExpired");}
 Set(expired,good);Check("authentic expired already-installed package is consumed without reinstall",LinuxUpdateApply.Apply(stage,config,program,"1.0.0",_=>throw new Exception()).Contains("не требуется")&&!Directory.Exists(stage));
 Reset();Set(Sign(id,"1.0.0",good,DateTime.UtcNow.AddMinutes(-1)),good);Reject("ManifestExpired");Check("expiry leaves installed program intact",File.ReadAllText(program)=="old executable");
 Set(signed with{Signature=Convert.ToBase64String(new byte[64])},good);Reject("SignatureMismatch");
 Set(signed with{Signature="invalid-base64!"},good);Reject("EnvelopeEncoding");
 var corrupt=(byte[])good.Clone();corrupt[0]^=1;Set(signed,corrupt);Reject("BinaryHash");
 Set(Sign(Guid.NewGuid().ToString("N"),"1.0.0",good),good);Reject("AgentIdentity");
 Reset();Set(signed,good);int starts=0;try{LinuxUpdateApply.Apply(stage,config,program,"0.9.0",op=>op!="start"||++starts>1);throw new Exception("startup failure accepted");}catch(LinuxReleaseException){}
 Check("startup failure rolled back and restarted previous program",File.ReadAllText(program)=="old executable"&&starts==2);
 Set(signed,good);File.Delete(Path.Combine(stage,"release.bin"));File.CreateSymbolicLink(Path.Combine(stage,"release.bin"),"/etc/passwd");Reject("PackageIO");
 Set(signed,good);File.Delete(Path.Combine(stage,"release.json"));using(var mk=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/mkfifo"){ArgumentList={Path.Combine(stage,"release.json")}})!){mk.WaitForExit();}
 var watch=System.Diagnostics.Stopwatch.StartNew();Reject("PackageIO");Check("FIFO cannot block privileged updater",watch.Elapsed<TimeSpan.FromSeconds(2));
 // Interrupted old-format marker is recovered without depending on an untrusted/expired queue.
 File.WriteAllText(Path.Combine(root,"agent.previous"),"known previous");File.WriteAllText(program,"uncommitted candidate");File.WriteAllText(Path.Combine(root,".update-in-progress"),"1");
 LinuxUpdateApply.Apply(stage,config,program,"1.0.0",_=>true);Check("interrupted transaction restores previous version",File.ReadAllText(program)=="known previous"&&!File.Exists(Path.Combine(root,".update-in-progress")));
 // A durable successful receipt wins over a leftover marker after power loss.
 var claim=stage+".claimed-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(claim);File.WriteAllText(Path.Combine(claim,"release.json"),"consumed fixture");File.WriteAllBytes(program,good);
 var hash=Convert.ToHexString(SHA256.HashData(good));File.WriteAllText(Path.Combine(root,".update-in-progress"),JsonSerializer.Serialize(new {Claimed=claim,Version="1.0.0",Sha256=hash},AgentWire.Json));File.WriteAllText(Path.Combine(root,"update-last-success.json"),JsonSerializer.Serialize(new {Version="1.0.0",Sha256=hash,TimeUtc=DateTime.UtcNow},AgentWire.Json));
 LinuxUpdateApply.Apply(stage,config,program,"1.0.0",op=>op=="is-active"?true:throw new Exception("committed update must not restart"));Check("committed receipt finalizes interrupted cleanup without rollback",File.ReadAllBytes(program).SequenceEqual(good)&&!Directory.Exists(claim)&&!File.Exists(Path.Combine(root,".update-in-progress")));
 var interrupted=stage+".claimed-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(interrupted);File.WriteAllText(program,"uncommitted newer program");File.WriteAllText(Path.Combine(root,".update-in-progress"),JsonSerializer.Serialize(new {Claimed=interrupted,Version="2.0.0",Sha256=new string('B',64)},AgentWire.Json));
 LinuxUpdateApply.Apply(stage,config,program,"2.0.0",_=>true);Check("uncommitted structured transaction rolls back and removes claimed package",File.ReadAllText(program)=="known previous"&&!Directory.Exists(interrupted)&&!File.Exists(Path.Combine(root,".update-in-progress")));
 var retired=stage+".quarantine-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(retired);File.WriteAllText(Path.Combine(retired,"release.bin"),"failed payload");File.WriteAllText(Path.Combine(retired,"release.json"),"failed manifest");
 var unrelated=stage+".quarantine-manual";Directory.CreateDirectory(unrelated);File.WriteAllText(Path.Combine(unrelated,"release.bin"),"keep");
 var linked=stage+".quarantine-"+Guid.NewGuid().ToString("N");Directory.CreateSymbolicLink(linked,unrelated);
 LinuxUpdateApply.Apply(stage,config,program,"2.0.0",_=>throw new Exception("cleanup must not restart service"));
 Check("old quarantine data is reclaimed without following symlinks or touching unrelated entries",!Directory.Exists(retired)&&new DirectoryInfo(linked).LinkTarget==null&&File.Exists(Path.Combine(unrelated,"release.bin")));
 Reset();Set(signed,good);using var entered=new ManualResetEventSlim();using var unblock=new ManualResetEventSlim();var task=Task.Run(()=>LinuxUpdateApply.Apply(stage,config,program,"0.9.0",op=>{if(op=="stop"){entered.Set();unblock.Wait(TimeSpan.FromSeconds(10));}return true;}));
 Check("apply reached locked transaction",entered.Wait(TimeSpan.FromSeconds(5)));var busy=LinuxUpdateApply.Apply(stage,config,program,"0.9.0",_=>throw new Exception("concurrent service operation"));unblock.Set();await task;Check("concurrent appliers serialize without racing queue",busy.Contains("занята")&&!Directory.Exists(stage));
 Reset();Set(signed,good);var victim=Path.Combine(root,"victim");Directory.CreateDirectory(victim);File.WriteAllText(Path.Combine(victim,"release.json"),"must remain");bool raced=false;LinuxUpdateApply.Apply(stage,config,program,"0.9.0",op=>{if(op=="start"&&!raced){raced=true;var claim=Directory.GetDirectories(root,"update-staging.claimed-*").Single();Directory.Move(claim,claim+".moved");Directory.CreateSymbolicLink(claim,victim);}return true;});Check("claimed-directory swap cannot delete privileged files through a symlink",File.ReadAllText(Path.Combine(victim,"release.json"))=="must remain");
 Check("registration/bootstrap was preserved",AgentWire.Verify(JsonSerializer.Deserialize<AgentSignedBootstrap>(File.ReadAllText(config),AgentWire.Json)!).EnrollmentId==id);
}finally{Directory.Delete(root,true);}
