using System.Security.Cryptography;
using SysAdminAssistant.Core;
internal static class UpdateFixSpecs
{
 public static async Task Run(string folder,Action<string,bool> check){var now=DateTime.UtcNow;
 var positive=WindowsUpdateResults.Build("Windows Server",["KB12345"],1,null,true,false,"");check("Missing WUA date does not erase available updates",positive.State=="Available"&&positive.PendingCount==1&&positive.CheckedUtc==null&&positive.HasUpdates);
 check("Empty WUA result without scan date is unknown",WindowsUpdateResults.Build("Windows",[],0,null,true,false,"").State=="Unknown");
 check("Successful dated empty WUA result is current",WindowsUpdateResults.Build("Windows",[],0,now,true,false,"").State=="Current");
 check("Partial WUA empty result cannot prove current",WindowsUpdateResults.Build("Windows",[],0,now,false,false,"").State=="Unknown");
 check("Partial WUA positive result retained",WindowsUpdateResults.Build("Windows",["KB123"],1,null,false,false,"ошибка даты").HasUpdates);
 check("WUA reboot survives missing list",WindowsUpdateResults.Build("Windows",[],0,null,false,true,"").HasUpdates);
 check("WUA UTC variant is not timezone shifted",WindowsUpdateResults.Date(DateTime.SpecifyKind(now,DateTimeKind.Unspecified))==now);
 check("WUA empty variant is optional",WindowsUpdateResults.Date(null)==null&&WindowsUpdateResults.Date(DBNull.Value)==null);
 check("WUA invalid future date is rejected",WindowsUpdateResults.Date(now.AddDays(1))==null);
 var encoded=System.Text.Json.JsonSerializer.Serialize(new WindowsUpdateSnapshot(now,positive),AgentWire.Json);var decoded=System.Text.Json.JsonSerializer.Deserialize<WindowsUpdateSnapshot>(encoded,AgentWire.Json);check("WUA protected snapshot strict JSON roundtrip",decoded?.Status.HasUpdates==true&&decoded.Status.PendingCount==1);
 check("WUA snapshot fresh accepted",WindowsUpdateResults.Snapshot(new(now,positive),now)==positive);
 check("WUA stale snapshot not promoted",WindowsUpdateResults.Snapshot(new(now.AddHours(-1),positive),now)==null);
 check("WUA future snapshot not promoted",WindowsUpdateResults.Snapshot(new(now.AddHours(1),positive),now)==null);
 var limited=WindowsUpdateResults.Build("Windows",Enumerable.Range(0,30).Select(i=>new string('x',300)),30,now,true,false,new string('d',1000));check("WUA metadata bounded for signed reports",limited.Items.Length==20&&limited.Items.All(s=>s.Length==200)&&limited.Detail.Length==500);AgentWire.Validate(new AgentReport{MachineName="fixture",CpuCount=1,Updates=limited});
 var dir=Path.Combine(folder,"runtime-cache");Directory.CreateDirectory(dir);var payload=new byte[]{1,2,3,4,5};var hash=Convert.ToHexString(SHA512.HashData(payload));var bundled=Path.Combine(dir,"bundled.exe");await File.WriteAllBytesAsync(bundled,payload);var cached=Path.Combine(dir,"saved","runtime.exe");int consent=0,downloads=0;
 Task<bool> Confirm(){consent++;return Task.FromResult(true);}async Task Download(string path,CancellationToken ct){downloads++;await File.WriteAllBytesAsync(path,payload,ct);}
 var first=await RuntimePackageCache.EnsureAsync(cached,[bundled],hash,Confirm,Download,null,CancellationToken.None);check("Bundled runtime seeds persistent cache without download",first==cached&&downloads==0&&consent==0&&RuntimePackageCache.Valid(cached,hash));
 File.Delete(bundled);await RuntimePackageCache.EnsureAsync(cached,[],hash,()=>throw new Exception("Must stay offline"),(_,_)=>throw new Exception("Network must not be used"),null,CancellationToken.None);check("Repeated runtime package creation uses cache offline",downloads==0&&consent==0);
 await File.WriteAllTextAsync(cached,"damaged");await RuntimePackageCache.EnsureAsync(cached,[],hash,Confirm,Download,null,CancellationToken.None);check("Corrupt cache downloads once after consent",downloads==1&&consent==1&&RuntimePackageCache.Valid(cached,hash));
 File.Delete(cached);bool cancelled=false;try{await RuntimePackageCache.EnsureAsync(cached,[],hash,()=>Task.FromResult(false),Download,null,CancellationToken.None);}catch(OperationCanceledException){cancelled=true;}check("Runtime download declined creates no file",cancelled&&!File.Exists(cached)&&downloads==1);
 bool rejected=false;try{await RuntimePackageCache.EnsureAsync(cached,[],hash,Confirm,async(path,ct)=>await File.WriteAllTextAsync(path,"bad",ct),null,CancellationToken.None);}catch(CryptographicException){rejected=true;}check("Wrong runtime hash not committed",rejected&&!File.Exists(cached)&&!Directory.GetFiles(Path.GetDirectoryName(cached)!,"*.download").Any());
 downloads=0;var parallel=Enumerable.Range(0,3).Select(_=>RuntimePackageCache.EnsureAsync(cached,[],hash,Confirm,Download,null,CancellationToken.None));await Task.WhenAll(parallel);check("Concurrent package requests fetch runtime once",downloads==1);
 }
}
