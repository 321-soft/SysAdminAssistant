using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException("Linux required");
var root=Path.Combine(Path.GetTempPath(),"saa-live-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var agentDir=Path.Combine(root,"agent");Directory.CreateDirectory(agentDir);File.SetUnixFileMode(agentDir,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(10));
var pin=cert.GetCertHashString(HashAlgorithmName.SHA256);
var binary=Path.GetFullPath(args.Length>0?args[0]:throw new ArgumentException("Укажи путь к опубликованному Linux-агенту"));
var hashes=new Dictionary<string,string>{{"SysAdminAssistant.AgentLinux",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary)))}};
var store=new AgentStore(root,pin,v=>v,v=>v,hashes);
var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(cert)));
var app=builder.Build();var received=new TaskCompletionSource<AgentReport>(TaskCreationOptions.RunContinuationsAsynchronously);
app.MapPost("/agent/enroll",(AgentEnrollment e)=>{var result=store.Enroll(e);store.Approve(1);return Results.Json(result,AgentWire.Json);});
app.MapPost("/agent/report-v3",(AgentEnvelope e)=>{var accepted=store.Accept(e);if(accepted.Report.EventBatch!=null)received.TrySetResult(accepted.Report);return Results.Json(new AgentReplyV3(accepted.Policy,accepted.Reply.Certificate,store.Trust(),new AgentEventPolicy{Enabled=true,Updates=false,Rules=AgentEvents.StandardLinux()},accepted.Report.EventBatch?.Id??""),AgentWire.Json);});
await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
var signed=store.Package(new AgentPackageRequest(1,address,["127.0.0.1/32"],hashes,Platform:"Linux"),"Linux fixture");File.WriteAllText(Path.Combine(agentDir,"bootstrap.json"),JsonSerializer.Serialize(signed,AgentWire.Json));File.SetUnixFileMode(Path.Combine(agentDir,"bootstrap.json"),UnixFileMode.UserRead|UnixFileMode.UserWrite);var policy=store.Get(1)!.Policy;policy.IntervalSeconds=15;store.Save(new AgentPolicySave(1,policy,policy.Version));
var pi=new System.Diagnostics.ProcessStartInfo(binary){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};pi.ArgumentList.Add("--data-dir");pi.ArgumentList.Add(agentDir);
using var process=System.Diagnostics.Process.Start(pi)??throw new Exception("Agent did not start");var error=process.StandardError.ReadToEndAsync();
try{
 var report=await received.Task.WaitAsync(TimeSpan.FromSeconds(45));
 if(!report.Os.StartsWith("Linux ")||report.MemoryTotalBytes<=0)throw new Exception("Invalid telemetry");
 if(report.EventBatch?.Health.All(h=>h.Channel!="Linux")!=false)throw new Exception("Missing Linux journal health");
 if(report.AgentVersion!="0.38.0.0"||report.Linux==null||report.Linux.Volumes.Length==0)throw new Exception("Missing 0.33 Linux telemetry");LinuxPolicy.ValidateTelemetry(report.Linux);
 if(!File.Exists(Path.Combine(agentDir,"identity.json")))throw new Exception("Identity missing");
 Console.WriteLine("PASS: Linux binary enrolled, sent signed report and 0.33 filesystem/journal telemetry via pinned HTTPS");
}finally{
 if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();var err=await error;if(!received.Task.IsCompletedSuccessfully)Console.WriteLine(err);
 await app.StopAsync();Directory.Delete(root,true);
}
