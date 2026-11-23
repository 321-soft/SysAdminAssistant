using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;

internal static class ProductReleaseSpecs
{
 public static void Run(Action<string,bool> check)
 {
  using var key=RSA.Create(3072);using var other=RSA.Create(3072);
  var now=new DateTime(2026,11,2,12,0,0,DateTimeKind.Utc);
  var p=new ProductPackage("client","0.41.0.0","0.40.0.0","https://example.org/client.exe",42,new string('A',64),LicenseAgreement.Hash,7);
  var release=new ProductRelease("SysAdminAssistant",now.AddHours(-1),now.AddDays(7),p,p with{Component="server"});
  string Sign(ProductRelease r){var bytes=JsonSerializer.SerializeToUtf8Bytes(r);return JsonSerializer.Serialize(new ProductReleaseEnvelope(Convert.ToBase64String(bytes),Convert.ToBase64String(key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))));}
  bool Reject(Action action){try{action();return false;}catch(Exception e)when(e is CryptographicException or InvalidDataException or InvalidOperationException or JsonException or FormatException){return true;}}
  var signed=Sign(release);
  check("Product release verifies publisher signature",ProductReleases.Verify(signed,key.ToXmlString(false),now)==release);
  check("Product release rejects another signing key",Reject(()=>ProductReleases.Verify(signed,other.ToXmlString(false),now)));
  using var envelope=JsonDocument.Parse(signed);var payload=Convert.FromBase64String(envelope.RootElement.GetProperty("Payload").GetString()!);payload[0]^=1;
  var changed=JsonSerializer.Serialize(new ProductReleaseEnvelope(Convert.ToBase64String(payload),envelope.RootElement.GetProperty("Signature").GetString()!));
  check("Product release rejects modified payload before parsing",Reject(()=>ProductReleases.Verify(changed,key.ToXmlString(false),now)));
  check("Product release rejects expired manifest",Reject(()=>ProductReleases.Verify(Sign(release with{ExpiresUtc=now}),key.ToXmlString(false),now)));
  check("Product release rejects future manifest",Reject(()=>ProductReleases.Verify(Sign(release with{PublishedUtc=now.AddHours(1)}),key.ToXmlString(false),now)));
  check("Product release rejects wrong product",Reject(()=>ProductReleases.Verify(Sign(release with{Product="Other"}),key.ToXmlString(false),now)));
  check("Product release rejects insecure package URL",Reject(()=>ProductReleases.Verify(Sign(release with{Client=p with{Url="http://example.org/client.exe"}}),key.ToXmlString(false),now)));
  check("Product release rejects insecure package mirror",Reject(()=>ProductReleases.Verify(Sign(release with{Client=p with{Mirrors=["http://example.org/client.exe"]}}),key.ToXmlString(false),now)));
  check("Product release rejects URL credentials",Reject(()=>ProductReleases.Https("https://user:secret@example.org/client.exe")));
  check("Product release rejects excessive package size",Reject(()=>ProductReleases.Verify(Sign(release with{Client=p with{Bytes=ProductReleases.MaximumBytes+1}}),key.ToXmlString(false),now)));
  check("Product release rejects unsupported schema",Reject(()=>ProductReleases.Verify(Sign(release with{Client=p with{DatabaseSchema=99}}),key.ToXmlString(false),now)));
  check("Product release prevents downgrade",Reject(()=>ProductReleases.RequireUpgrade(p,new Version("0.42.0.0"),"client")));
  check("Product release enforces minimum installed version",Reject(()=>ProductReleases.RequireUpgrade(p,new Version("0.38.1.0"),"client")));
  check("Product release requires interactive changed EULA",Reject(()=>ProductReleases.RequireUpgrade(p with{EulaSha256=new string('0',64)},new Version("0.40.0.0"),"client")));
  ProductReleases.RequireUpgrade(p,new Version("0.40.0.0"),"client");check("Product release accepts compatible explicit upgrade",true);
  check("EULA hash canonicalization ignores platform line endings",LicenseAgreement.CanonicalText("a\r\nb\r\n")==LicenseAgreement.CanonicalText("a\nb\n"));
  var folder=Path.Combine(Path.GetTempPath(),"SAA-Product-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{
   var stub=Path.Combine(folder,"stub");File.WriteAllText(stub,"MZ-STUB");
   string Bundle(string name,params string[] entries){var zip=Path.Combine(folder,name+".zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){foreach(var entry in entries){using var writer=new StreamWriter(archive.CreateEntry(entry).Open());writer.Write("test");}}var exe=Path.Combine(folder,name+".exe");ProductSetupBundle.Pack(stub,zip,exe);return exe;}
   var good=Bundle("good","Client/app.dll","Client/app.runtimeconfig.json");var target=Path.Combine(folder,"out");ProductSetupBundle.Extract(good,target,null!);
   check("Product setup bundle preserves nested files",File.ReadAllText(Path.Combine(target,"Client","app.dll"))=="test");
   var bad=Bundle("traversal","ok.txt","../outside.txt");var invalidTarget=Path.Combine(folder,"bad");
   check("Product setup rejects traversal before any extraction",Reject(()=>ProductSetupBundle.Extract(bad,invalidTarget,null!))&&!Directory.Exists(invalidTarget));
   check("Product setup rejects Windows case aliases",Reject(()=>ProductSetupBundle.Extract(Bundle("alias","a.txt","A.TXT"),Path.Combine(folder,"alias"),null!)));
   check("Product setup rejects reserved device names",Reject(()=>ProductSetupBundle.Extract(Bundle("reserved","NUL.txt"),Path.Combine(folder,"reserved"),null!)));
   var tampered=File.ReadAllBytes(good);tampered[12]^=1;File.WriteAllBytes(good,tampered);
   check("Product setup rejects corrupted embedded archive",Reject(()=>ProductSetupBundle.Extract(good,Path.Combine(folder,"corrupted"),null!)));
  }finally{Directory.Delete(folder,true);}
 }
}
