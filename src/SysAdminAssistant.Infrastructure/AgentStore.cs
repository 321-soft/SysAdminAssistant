using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record AgentTrustConfirmation(int DeviceId,string MachineName,long TrustVersion,DateTime? LastSeenUtc,string AgentVersion);
public sealed record AgentViewResult(AgentView? Agent);
public sealed record AgentDeviceRequest(int DeviceId);
public sealed record AgentPolicySave(int DeviceId,AgentPolicy Policy,int ExpectedVersion);
public sealed record AgentPackageRequest(int DeviceId,string ServerUrl,string[] AllowedNetworks,Dictionary<string,string> Binaries,bool Upgrade=false,string Platform="Windows");
public sealed record AgentView(string AgentId,int DeviceId,bool Approved,bool Revoked,string MachineName,string KeyFingerprint,DateTime? LastSeenUtc,AgentPolicy Policy,AgentReport? Report,AgentFinding[] Findings,string CertificateExpires,DateTime[] RecentReboots,long TrustVersion=0,DateTime? RecoveryUntil=null,string? Platform=null);
public sealed class AgentRecord
{
    public string Id {get;set;}="";public int DeviceId {get;set;}[JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? Platform {get;set;}public bool Approved {get;set;}public bool Revoked {get;set;}
    public DateTime? RecoveryUntil {get;set;}
    public string TokenHash {get;set;}="";public DateTime EnrollmentExpires {get;set;}public string Certificate {get;set;}="";public string MachineName {get;set;}="";
    public string AgentVersion {get;set;}="";
    public long? LastUptimeSeconds {get;set;}public DateTime[] Reboots {get;set;}=[];public long LastSequence {get;set;}public DateTime? LastSeenUtc {get;set;}public AgentPolicy Policy {get;set;}=new();
}
public sealed class AgentIndex {public string ProtectedCa {get;set;}="";public List<AgentRecord> Agents {get;set;}=[];public long TrustVersion {get;set;}=1;public string[] TrustPins {get;set;}=[];}
public sealed partial class AgentStore(string directory,string serverPin,Func<string,string>? protect=null,Func<string,string>? unprotect=null,IReadOnlyDictionary<string,string>? trustedBinaries=null)
{
    private readonly string path=Path.Combine(directory,"agent-index.json");
    private T State<T>(Func<AgentIndex,T> action,bool write=false,Action<T>? afterCommit=null)=>NetworkStore.Locked(path,()=>{
        var state=File.Exists(path)?JsonSerializer.Deserialize<AgentIndex>(File.ReadAllText(path),AgentWire.Json)??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустое состояние агентов")):new();
        var result=action(state);if(write)Atomic(path,state);afterCommit?.Invoke(result);return result;
    });
    private static void Atomic<T>(string path,T value){var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{var bytes=JsonSerializer.SerializeToUtf8Bytes(value,AgentWire.Json);using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){file.Write(bytes);file.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    private string ReportPath(string id)=>Guid.TryParseExact(id,"N",out _)?Path.Combine(directory,"agent-report-"+id+".json"):throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный ID агента"));
    private AgentReport? Report(string id)=>File.Exists(ReportPath(id))?JsonSerializer.Deserialize<AgentReport>(File.ReadAllText(ReportPath(id)),AgentWire.Json):null;
    private X509Certificate2 Ca(AgentIndex state)
    {
        if(state.ProtectedCa.Length>0){var existing=X509CertificateLoader.LoadPkcs12(Convert.FromBase64String((unprotect??LocalSecret.Unprotect)(state.ProtectedCa)),null,X509KeyStorageFlags.EphemeralKeySet);if(existing.NotAfter.ToUniversalTime()>DateTime.UtcNow.AddYears(1))return existing;using(existing)using(var same=existing.GetECDsaPrivateKey()!){var next=AuthorityRequest(same).CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddYears(10));state.ProtectedCa=(protect??LocalSecret.Protect)(Convert.ToBase64String(next.Export(X509ContentType.Pkcs12)));return next;}}
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var request=AuthorityRequest(key);
        var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddYears(10));
        state.ProtectedCa=(protect??LocalSecret.Protect)(Convert.ToBase64String(cert.Export(X509ContentType.Pkcs12)));return cert;
    }
    private static CertificateRequest AuthorityRequest(ECDsa key){var r=new CertificateRequest("CN=SysAdmin Assistant Agent Authority",key,HashAlgorithmName.SHA256);r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));r.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.DigitalSignature,true));return r;}
    public SignedStationRelease SignStationRelease(StationRelease release)=>State(s=>{
        using var ca=Ca(s);
        using var key=ca.GetECDsaPrivateKey()!;
        var payload=JsonSerializer.SerializeToUtf8Bytes(release,AgentWire.Json);
        return new SignedStationRelease(Convert.ToBase64String(payload),Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256)));
    },true);
    public string AuthorityCertificate()=>State(s=>{using var ca=Ca(s);return Convert.ToBase64String(ca.RawData);},true);
    public X509Certificate2 IssueServerCertificate(string ip,DateTime now)=>State(s=>{using var ca=Ca(s);using var sign=ca.GetECDsaPrivateKey()!;using var key=RSA.Create(3072);var req=new CertificateRequest("CN=SysAdmin Assistant Server",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(System.Net.IPAddress.Parse(ip));req.CertificateExtensions.Add(san.Build());req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));using var publicCert=req.Create(ca.SubjectName,X509SignatureGenerator.CreateForECDsa(sign),new DateTimeOffset(now.AddMinutes(-5),TimeSpan.Zero),new DateTimeOffset(now.AddYears(1),TimeSpan.Zero),RandomNumberGenerator.GetBytes(16));return publicCert.CopyWithPrivateKey(key);},true);
    public void PublishServerPins(string current,string? next=null)=>State(s=>{serverPin=current;var pins=s.TrustPins.Where(p=>p!=current&&p!=next).TakeLast(2).Append(current).Concat(next==null?[]:new[]{next}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(!s.TrustPins.SequenceEqual(pins,StringComparer.OrdinalIgnoreCase)){s.TrustPins=pins;s.TrustVersion=checked(s.TrustVersion+1);}return true;},true);
    private static string Issue(X509Certificate2 ca,ECDsa key,string id)
    {
        var request=new CertificateRequest("CN=SAA-Agent-"+id,key,HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
        using var cert=request.Create(ca,DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(90),RandomNumberGenerator.GetBytes(16));return Convert.ToBase64String(cert.RawData);
    }
    private static string Fingerprint(string certificate){if(certificate.Length==0)return "";using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificate));using var key=cert.GetECDsaPublicKey()!;return Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));}
    private AgentView View(AgentRecord a,bool includeReport=true){var report=includeReport?Report(a.Id):null;using var cert=a.Certificate.Length>0?X509CertificateLoader.LoadCertificate(Convert.FromBase64String(a.Certificate)):null;return new(a.Id,a.DeviceId,a.Approved,a.Revoked,a.MachineName,Fingerprint(a.Certificate),a.LastSeenUtc,a.Policy,report,report==null?[]:AgentMetricHistory.EffectiveFindings(directory,a.DeviceId,report,a.Policy,AgentChecks.Evaluate(report,a.Policy)),cert?.NotAfter.ToUniversalTime().ToString("O")??"",a.Reboots,report?.TrustVersion??0,a.RecoveryUntil,a.Platform);}
    public int DeviceId(string id)=>State(s=>s.Agents.SingleOrDefault(a=>a.Id==id&&!a.Revoked)?.DeviceId??throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестный агент")));
    public AgentView? Get(int deviceId)=>State(s=>s.Agents.FirstOrDefault(a=>a.DeviceId==deviceId) is AgentRecord a?View(a):null);
    private Dictionary<string,string> TrustedTemplate(string platform)
    {
        if(platform is not ("Windows" or "Linux"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неизвестная платформа агента"));
        if(trustedBinaries!=null)return trustedBinaries.ToDictionary(p=>p.Key,p=>p.Value);
        if(platform=="Linux"){
            var linuxRoot=Path.Combine(AppContext.BaseDirectory,"AgentTemplate","Linux");
            var executable=Path.Combine(linuxRoot,"SysAdminAssistant.AgentLinux");
            if(!File.Exists(executable))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Шаблон Linux-агента не опубликован на сервере"));
            if((File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0||new FileInfo(executable).Length>150_000_000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Неверный шаблон Linux-агента"));
            return new Dictionary<string,string>{{Path.GetFileName(executable),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)))}};
        }
        var root=Path.Combine(AppContext.BaseDirectory,"AgentTemplate","Agent");
        if(!File.Exists(Path.Combine(root,"SysAdminAssistant.Agent.exe")))throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Нет эталонного AgentTemplate на сервере. Обновите сервер из полного опубликованного комплекта."));
        return Directory.GetFiles(root).ToDictionary(p=>Path.GetFileName(p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    }
    public Dictionary<string,string> LinuxTemplateHashes()=>TrustedTemplate("Linux");
    public AgentSignedBootstrap Package(AgentPackageRequest request,string name)=>State(state=>{
        var existing=state.Agents.FirstOrDefault(a=>a.DeviceId==request.DeviceId&&!a.Revoked&&a.Certificate.Length>0);
        if(request.Upgrade&&existing!=null&&(existing.Platform=="Linux")!=(request.Platform=="Linux"))
            throw new ArgumentException("Пакет обновления не соответствует платформе агента. Linux-агент получает подписанный выпуск автоматически.");
        var trusted=TrustedTemplate(request.Platform);if(request.Binaries==null||request.Binaries.Count!=trusted.Count||trusted.Any(p=>!request.Binaries.TryGetValue(p.Key,out var hash)||hash!=p.Value))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Шаблон клиента не совпадает с эталоном сервера. Обновите обе стороны; произвольные файлы не подписываются."));
        if(existing!=null&&!request.Upgrade)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Агент уже установлен. Используйте «Пакет обновления агента» без удаления и повторной регистрации."));
        if(request.Upgrade&&existing==null)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нет зарегистрированного агента для обновления"));
        if(state.Agents.Count>=256&&!state.Agents.Any(a=>a.DeviceId==request.DeviceId))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Достигнут лимит 256 агентов"));
        using var ca=Ca(state);var id=existing?.Id??Guid.NewGuid().ToString("N");var token=existing==null?Convert.ToHexString(RandomNumberGenerator.GetBytes(32)):"";var expires=DateTime.UtcNow.AddHours(24);
        var bootstrap=new AgentBootstrap{EnrollmentId=id,Token=token,DeviceId=request.DeviceId,DeviceName=name,ServerUrl=request.ServerUrl,ServerSha256=serverPin,AllowedNetworks=request.AllowedNetworks,ExpiresUtc=expires,IssuerCertificate=Convert.ToBase64String(ca.RawData),Binaries=trusted};
        var payload=JsonSerializer.Serialize(bootstrap,AgentWire.Json);using var key=ca.GetECDsaPrivateKey()!;var signed=new AgentSignedBootstrap(payload,Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(payload),HashAlgorithmName.SHA256)));_=AgentWire.Verify(signed);
        if(existing!=null)return signed;
        var previous=state.Agents.FirstOrDefault(a=>a.DeviceId==request.DeviceId);var policy=previous?.Policy??new();if(request.Platform=="Linux"&&previous==null)policy.StandardServices=false;state.Agents.RemoveAll(a=>a.DeviceId==request.DeviceId);
        state.Agents.Add(new(){Id=id,DeviceId=request.DeviceId,Platform=request.Platform=="Linux"?"Linux":null,TokenHash=AgentWire.Hash(token),EnrollmentExpires=expires,Policy=policy});return signed;
    },true);
    public AgentEnrollmentResult Enroll(AgentEnrollment request)=>State(state=>{
        if(!AgentWire.Text(request.MachineName,128)||request.MachineName.Length==0||request.PublicKey.Length>500||request.Proof.Length>256||request.Token.Length!=64)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректная регистрация"));
        var a=state.Agents.SingleOrDefault(a=>a.Id==request.EnrollmentId&&!a.Revoked)??throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Регистрация запрещена"));
        using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.PublicKey),out var used);
        if(key.KeySize!=256||key.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7"||used!=Convert.FromBase64String(request.PublicKey).Length||!key.VerifyData(AgentWire.EnrollmentBytes(request),Convert.FromBase64String(request.Proof),HashAlgorithmName.SHA256))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Неверное доказательство ключа"));
        // Same key may retry a lost enrollment response; a different key can never consume it twice.
        if(a.Certificate.Length>0){if(Fingerprint(a.Certificate)!=Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Разрешение уже использовано"));return new AgentEnrollmentResult(a.Id,a.Certificate,a.Policy);}
        if(a.EnrollmentExpires<DateTime.UtcNow||a.TokenHash.Length==0||!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a.TokenHash),SHA256.HashData(Encoding.UTF8.GetBytes(request.Token))))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Разрешение недействительно или просрочено"));
        using var ca=Ca(state);a.Certificate=Issue(ca,key,a.Id);a.MachineName=request.MachineName;a.TokenHash="";return new AgentEnrollmentResult(a.Id,a.Certificate,a.Policy);
    },true);
    public void Approve(int deviceId)=>State(s=>{var a=s.Agents.Single(a=>a.DeviceId==deviceId&&!a.Revoked);if(a.Certificate.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Агент ещё не зарегистрирован"));a.Approved=true;return true;},true);
    public void Revoke(int deviceId)=>State(s=>{var a=s.Agents.Single(a=>a.DeviceId==deviceId);a.Revoked=true;a.TokenHash="";a.Approved=false;return true;},true);
    public AgentPolicy Save(AgentPolicySave request)=>State(s=>{AgentWire.Validate(request.Policy);var a=s.Agents.Single(a=>a.DeviceId==request.DeviceId);if(a.Policy.Version!=request.ExpectedVersion)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Политика изменена другим пользователем"));var prior=Report(a.Id);bool linux=a.Platform=="Linux";if(request.Policy.ServiceRoles.Any(id=>linux?!LinuxServiceProfiles.IsRole(id):LinuxServiceProfiles.IsRole(id)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Роль не соответствует системе агента"));if(linux)LinuxServiceProfiles.Sync(request.Policy,prior?.Services??[]);else AgentServiceProfiles.Sync(request.Policy,prior?.Services??[]);AgentWire.Validate(request.Policy);request.Policy.Version=a.Policy.Version+1;a.Policy=request.Policy;return a.Policy;},true);
    public (int DeviceId,AgentReport Report,AgentPolicy Policy,AgentReply Reply,bool Reboot) Accept(AgentEnvelope e)=>State(s=>{
        if(e.AgentId.Length!=32||e.Payload.Length>500000||e.Signature.Length>256||e.Timestamp<DateTimeOffset.UtcNow.ToUnixTimeSeconds()-300||e.Timestamp>DateTimeOffset.UtcNow.ToUnixTimeSeconds()+300)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Неверный запрос агента"));
        var a=s.Agents.SingleOrDefault(a=>a.Id==e.AgentId&&!a.Revoked&&a.Approved)??throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Агент не подтверждён или отозван"));
        using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(a.Certificate));using var key=cert.GetECDsaPublicKey()!;
        if(!key.VerifyData(AgentWire.Bytes(e),Convert.FromBase64String(e.Signature),HashAlgorithmName.SHA256))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Подпись агента недействительна"));
        if(e.Sequence is <1 or >9000000000000)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Неверная последовательность"));
        if(e.Sequence<=a.LastSequence)throw new AgentSequenceException(a.LastSequence+1);
        // The approved enrolled key, fresh signature and increasing sequence remain the identity even after certificate expiry; revocation above still blocks access.
        var report=JsonSerializer.Deserialize<AgentReport>(e.Payload,AgentWire.Json)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустой отчёт"));AgentWire.Validate(report);
        bool linux=a.Platform=="Linux";if(linux!=report.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Платформа агента не соответствует регистрации"));
        if(linux){if(!report.Errors.Contains("Не удалось получить службы systemd",StringComparer.Ordinal)&&LinuxServiceProfiles.Sync(a.Policy,report.Services))a.Policy.Version++;}
        else if(AgentServiceProfiles.Sync(a.Policy,report.Services))a.Policy.Version++;
        var reboot=a.LastUptimeSeconds is long uptime && report.UptimeSeconds+5<uptime;a.LastUptimeSeconds=report.UptimeSeconds;
        if(reboot)a.Reboots=a.Reboots.Append(DateTime.UtcNow).TakeLast(30).ToArray();
        a.LastSequence=e.Sequence;a.LastSeenUtc=DateTime.UtcNow;a.MachineName=report.MachineName;a.AgentVersion=report.AgentVersion;
        if(cert.NotAfter.ToUniversalTime()<DateTime.UtcNow.AddDays(30)){using var ca=Ca(s);a.Certificate=Issue(ca,key,a.Id);a.RecoveryUntil=null;}
        return(a.DeviceId,report,a.Policy,new AgentReply(LinuxPolicy.ForAgent(a.Policy,report.AgentVersion),a.Certificate),reboot);
    },true,result=>Atomic(ReportPath(e.AgentId),result.Item2));
    public AgentTrustConfirmation[] TrustConfirmations()=>State(s=>s.Agents.Where(a=>a.Approved&&!a.Revoked).Select(a=>{
        var report=Report(a.Id);return new AgentTrustConfirmation(a.DeviceId,a.MachineName,report?.TrustVersion??0,a.LastSeenUtc,report?.AgentVersion??"");
    }).ToArray());
    public Dictionary<int,string> ReportedVersions()=>State(s=>s.Agents.Where(a=>a.Approved&&!a.Revoked&&!string.IsNullOrWhiteSpace(a.AgentVersion)).ToDictionary(a=>a.DeviceId,a=>a.AgentVersion));
    public AgentView[] Active()=>State(s=>s.Agents.Where(a=>a.Approved&&!a.Revoked).Select(a=>View(a,false)).ToArray());
}
