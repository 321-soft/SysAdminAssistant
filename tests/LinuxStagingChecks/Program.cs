using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.AgentLinux;
if(!OperatingSystem.IsLinux())throw new Exception("Linux required");
var root=Path.Combine(Path.GetTempPath(),"saa-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try{
 using var issuerKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);
 var certRequest=new CertificateRequest("CN=test CA",issuerKey,HashAlgorithmName.SHA256);
 using var cert=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(10));
 using var deviceKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);
 var id=Guid.NewGuid().ToString("N");var bootstrap=new AgentBootstrap{IssuerCertificate=Convert.ToBase64String(cert.RawData)};
 var bytes=Encoding.ASCII.GetBytes("signed binary fixture");
 async Task Run(string directory,bool tamper){
  Directory.CreateDirectory(directory);var handler=new Handler(issuerKey,id,bytes,tamper);using var http=new HttpClient(handler){BaseAddress=new Uri("https://127.0.0.1/")};
  var type=typeof(LinuxCollector).Assembly.GetType("SysAdminAssistant.AgentLinux.LinuxAgentUpdater")??throw new Exception("missing updater");
  var instance=Activator.CreateInstance(type,directory)??throw new Exception("cannot create updater");
  var task=(Task)type.GetMethod("CheckAsync")!.Invoke(instance,new object[]{http,bootstrap,id,deviceKey,"0.0.0",CancellationToken.None})!;await task;
  var file=Path.Combine(directory,"update-staging","release.bin");
  if(tamper&&File.Exists(file))throw new Exception("tampered update staged");
  if(!tamper&&(!File.Exists(file)||!File.ReadAllBytes(file).SequenceEqual(bytes)))throw new Exception("valid update not staged");
 }
 await Run(Path.Combine(root,"valid"),false);await Run(Path.Combine(root,"tampered"),true);
 var slowDir=Path.Combine(root,"slow");Directory.CreateDirectory(slowDir);
 using(var slowHttp=new HttpClient(new Handler(issuerKey,id,bytes,false,true)){BaseAddress=new Uri("https://127.0.0.1/")})
 using(var cancel=new CancellationTokenSource()){
  var type=typeof(LinuxCollector).Assembly.GetType("SysAdminAssistant.AgentLinux.LinuxAgentUpdater")!;var instance=Activator.CreateInstance(type,slowDir)!;
  var watch=System.Diagnostics.Stopwatch.StartNew();type.GetMethod("Schedule")!.Invoke(instance,new object[]{slowHttp,bootstrap,id,deviceKey,"0.0.0",cancel.Token});watch.Stop();if(watch.ElapsedMilliseconds>1000)throw new Exception("Update blocked monitoring");cancel.Cancel();await Task.Delay(100);
 }

 Console.WriteLine("PASS: verified Linux release staged, tampered binary rejected");
}finally{Directory.Delete(root,true);}
sealed class Handler(ECDsa key,string agentId,byte[] bytes,bool tamper,bool slow=false):HttpMessageHandler{
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken token){
  if(slow)await Task.Delay(5000,token);
  var request=JsonSerializer.Deserialize<AgentReleaseRequest>(await message.Content!.ReadAsStringAsync(token),AgentWire.Json)!;
  if(message.RequestUri!.AbsolutePath.EndsWith("manifest")){
   var doc=new LinuxAgentReleaseDocument(agentId,request.Nonce,"1.0.0",DateTime.UtcNow.AddMinutes(10),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
   var payload=JsonSerializer.Serialize(doc,AgentWire.Json);var signed=new LinuxAgentSignedRelease(payload,Convert.ToBase64String(key.SignData(LinuxAgentRelease.Content(payload),HashAlgorithmName.SHA256)));
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(signed,AgentWire.Json),Encoding.UTF8,"application/json")};
  }
  var binary=(byte[])bytes.Clone();if(tamper)binary[0]^=1;return new(HttpStatusCode.OK){Content=new ByteArrayContent(binary)};
 }
}
