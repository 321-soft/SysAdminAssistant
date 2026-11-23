using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;
public sealed record AgentReleaseRequest(string AgentId,long Timestamp,string Nonce,string Signature,string? Scope=null);
public sealed record AgentReleaseFile(long Length,string Sha256);
public sealed record AgentReleaseDocument(string AgentId,string Nonce,string Version,DateTime ExpiresUtc,Dictionary<string,AgentReleaseFile> Agent,Dictionary<string,AgentReleaseFile> Setup,AgentSignedTrust Trust,int FrameworkMajor=10)
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
 public AgentReleaseFile? Runtime {get;init;}
}
public sealed record AgentSignedRelease(string Payload,string Signature);
public sealed record AgentReleaseDownload(AgentReleaseRequest Request,string Group,string Name);
public static class AgentRelease
{
 public const string WindowsRuntimeScope="windows-runtime";
 // The two bridge releases can read Runtime but do not yet advertise that capability.
 public static bool SupportsWindowsRuntime(string? scope,string? reportedVersion)=>
     scope==WindowsRuntimeScope || scope==null && Version.TryParse(reportedVersion,out var version) &&
     version.Major==0&&version.Minor==42&&version.Build is 12 or 13;
 public static byte[] Challenge(AgentReleaseRequest r)=>Encoding.UTF8.GetBytes((r.Scope==null?"SAA-AGENT-UPDATE-1\n":"SAA-AGENT-UPDATE-2\n"+r.Scope+"\n")+r.AgentId+"\n"+r.Timestamp+"\n"+r.Nonce);
 public static byte[] Content(string s)=>Encoding.UTF8.GetBytes("SAA-AGENT-RELEASE-1\n"+s);
 public static bool FileName(string s){if(s==null||s.Length is <1 or >150||Path.GetFileName(s)!=s||s.StartsWith('.')||s.EndsWith('.')||s.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('.' or '-' or '_')))return false;var stem=s.Split('.')[0].ToUpperInvariant();if(new[]{"CON","PRN","AUX","NUL"}.Contains(stem)||stem.Length==4&&(stem.StartsWith("COM")||stem.StartsWith("LPT"))&&stem[3] is >= '1' and <= '9')return false;return s.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)||s.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||s.EndsWith(".deps.json",StringComparison.OrdinalIgnoreCase)||s.EndsWith(".runtimeconfig.json",StringComparison.OrdinalIgnoreCase);}
 public static void Validate(AgentReleaseDocument r){if(!Guid.TryParseExact(r.AgentId,"N",out _)||!Guid.TryParseExact(r.Nonce,"N",out _)||!System.Version.TryParse(r.Version,out var v)||r.FrameworkMajor!=10||r.ExpiresUtc.Kind!=DateTimeKind.Utc||r.ExpiresUtc<=DateTime.UtcNow||r.ExpiresUtc>DateTime.UtcNow.AddMinutes(15)||r.Agent==null||r.Setup==null||r.Agent.Count is <1 or >128||r.Setup.Count is <1 or >128||r.Agent.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Agent.Count||r.Setup.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Setup.Count)throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Неверный выпуск агента"));if(r.Runtime is {} runtime&&(runtime.Length is <1 or >AgentDesktopRuntime.MaxBytes||runtime.Sha256?.Length!=64||!runtime.Sha256.All(Uri.IsHexDigit)))throw new CryptographicException("Недопустимое описание установщика .NET в выпуске агента.");long total=0;foreach(var f in r.Agent.Concat(r.Setup)){if(!FileName(f.Key)||f.Value==null||f.Value.Sha256==null||f.Value.Length is <1 or >30000000||f.Value.Sha256.Length!=64||!f.Value.Sha256.All(Uri.IsHexDigit))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Недопустимый состав выпуска"));total+=f.Value.Length;}if(total>160000000||new[]{"SysAdminAssistant.Agent","SysAdminAssistant.UpdateProbe"}.Any(name=>new[]{".exe",".dll",".deps.json",".runtimeconfig.json"}.Any(ext=>!r.Agent.ContainsKey(name+ext)))||new[]{".exe",".dll",".deps.json",".runtimeconfig.json"}.Any(ext=>!r.Setup.ContainsKey("SysAdminAssistant.AgentSetup"+ext)))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Неполный выпуск агента"));}
 public static AgentReleaseDocument Verify(AgentSignedRelease signed,string issuer,string agentId,string nonce,string current){if(signed.Payload==null||signed.Signature==null||signed.Payload.Length>100000||signed.Signature.Length>256)throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Выпуск слишком велик"));using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(issuer));using var key=cert.GetECDsaPublicKey()??throw new CryptographicException();if(!key.VerifyData(Content(signed.Payload),Convert.FromBase64String(signed.Signature),HashAlgorithmName.SHA256))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Выпуск подписан другим сервером или повреждён"));var r=JsonSerializer.Deserialize<AgentReleaseDocument>(signed.Payload,AgentWire.Json)??throw new CryptographicException();Validate(r);if(r.AgentId!=agentId||r.Nonce!=nonce||System.Version.Parse(r.Version)<System.Version.Parse(current))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Подмена, повтор или понижение версии выпуска"));_=AgentTrust.Verify(r.Trust,issuer);return r;}
 public static void VerifyFile(byte[] bytes,AgentReleaseFile file){if(bytes.LongLength!=file.Length||!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Файл выпуска изменён или скачан не полностью"));}
}
