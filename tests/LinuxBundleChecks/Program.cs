using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using SysAdminAssistant.Server;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
var dir=Path.Combine(Path.GetTempPath(),"saa-bundle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
try{
 var access=new ServerNetworkAccess(dir,["192.168.1.0/24"],_=>"fixture firewall");var peer=System.Net.IPAddress.Parse("192.168.1.2");var initial=access.View(peer);try{access.Save(new(["192.168.2.0/24"],initial.Revision),peer);throw new Exception("lockout accepted");}catch(ArgumentException){}if(!access.Allows(peer))throw new Exception("failed save changed live access");var saved=access.Save(new(["192.168.1.0/24","192.168.2.0/24"],initial.Revision),peer);if(!new ServerNetworkAccess(dir,["10.0.0.0/8"],_=>"fixture").Allows(System.Net.IPAddress.Parse("192.168.2.3")))throw new Exception("network save not persistent");try{access.Save(new(initial.Networks,initial.Revision),peer);throw new Exception("stale overwrite accepted");}catch(ArgumentException){}access.Save(new(["192.168.2.0/24"],saved.Revision,true),peer);if(access.Allows(peer))throw new Exception("confirmed removal not applied");Console.WriteLine("PASS: trusted network persistence revision conflict lockout guard and confirmed removal");
 var binary=Path.Combine(dir,"binary");File.WriteAllText(binary,"fixture");
 var script=Path.Combine(dir,"install.sh");File.WriteAllText(script,"#!/bin/bash\n");
 var hashes=new Dictionary<string,string>{{"SysAdminAssistant.AgentLinux",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary)))}};
 var store=new AgentStore(dir,new string('A',64),v=>v,v=>v,hashes);
 using var rsa=RSA.Create(2048);var req=new CertificateRequest("CN=test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
 using var cert=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(10));
 var result=LinuxInstallBundles.CreateForStore(store,cert,"https://192.168.1.2:7443/",3,"Fixture",binary,script);
 if(!result.Command.Contains("--pinnedpubkey")||!result.Command.Contains("sha256sum --check"))throw new Exception("Missing verification");
 var token=Regex.Match(result.Command,@"X-SAA-Install: ([A-F0-9]{64})").Groups[1].Value;if(token.Length!=64)throw new Exception("No token");
 var bytes=LinuxInstallBundles.Take(token);
 using var ms=new MemoryStream(bytes);using var gz=new GZipStream(ms,CompressionMode.Decompress);using var reader=new TarReader(gz);
 var found=new List<string>();TarEntry? entry;while((entry=reader.GetNextEntry())!=null){found.Add(entry.Name);if(entry.Name=="bootstrap.json"){var signed=JsonSerializer.Deserialize<AgentSignedBootstrap>(entry.DataStream!,AgentWire.Json)!;var boot=AgentWire.Verify(signed);if(boot.DeviceId!=3||boot.Binaries["SysAdminAssistant.AgentLinux"]!=hashes["SysAdminAssistant.AgentLinux"])throw new Exception("Invalid bootstrap");}}
 if(!found.Order().SequenceEqual(new[]{"agent","bootstrap.json","install.sh"}.Order()))throw new Exception("Unexpected tar");
 try{LinuxInstallBundles.Take(token);throw new Exception("Token reused");}catch(UnauthorizedAccessException){}
 var actual=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();if(!result.Command.Contains(actual))throw new Exception("Hash mismatch");
 if(!result.Command.StartsWith("bash -c '")||!result.Command.EndsWith("'"))throw new Exception("Unexpected quoting");
 var inner=result.Command[9..^1].Replace("'\\''","'");
 var syntax=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/bash"){ArgumentList={"-n","-c",inner}})!;syntax.WaitForExit();if(syntax.ExitCode!=0)throw new Exception("Shell syntax");
 Console.WriteLine("PASS: pinned command, signed bundle, single-use key and shell syntax");
}finally{Directory.Delete(dir,true);}
