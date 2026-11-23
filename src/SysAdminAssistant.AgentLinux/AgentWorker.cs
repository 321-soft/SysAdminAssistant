using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;
public sealed class AgentIdentity
{
    public AgentEventPolicy EventPolicy {get;set;}=new(){Enabled=false,Updates=false};
    public AgentSignedTrust? Trust {get;set;}
    public string PrivateKey {get;set;}="";public string AgentId {get;set;}="";public string Certificate {get;set;}="";public long Sequence {get;set;}public AgentPolicy Policy {get;set;}=new();
}
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class AgentWorker(string directory)
{
    private string IdentityPath=>Path.Combine(directory,"identity.json");
    private AgentIdentity Load()=>File.Exists(IdentityPath)?JsonSerializer.Deserialize<AgentIdentity>(File.ReadAllBytes(IdentityPath),AgentWire.Json)??throw new InvalidDataException():new();
    private void Save(AgentIdentity id){var bytes=JsonSerializer.SerializeToUtf8Bytes(id,AgentWire.Json);try{Atomic(IdentityPath,bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}}
    private static void Atomic(string path,byte[] bytes){var temp=path+".tmp";try{using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);file.Write(bytes);file.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    public async Task RunAsync(CancellationToken token)
    {
        // Installation-only bootstrap is read from a fixed, ACL-protected path. No remote code or file paths are accepted.
        
        var updates=new LinuxUpdates();var updater=new LinuxAgentUpdater(directory);
        var signed=JsonSerializer.Deserialize<AgentSignedBootstrap>(File.ReadAllText(Path.Combine(directory,"bootstrap.json")),AgentWire.Json)??throw new InvalidDataException();var bootstrap=AgentWire.Verify(signed);var endpoint=AgentWire.Endpoint(bootstrap);
        var id=Load();var trustedPins=new[]{bootstrap.ServerSha256};long trustVersion=0;
        void ApplyTrust(AgentSignedTrust signed){var value=AgentTrust.Verify(signed,bootstrap.IssuerCertificate,trustVersion);if(value.Version==trustVersion&&id.Trust!=null&&signed.Payload!=id.Trust.Payload)throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Конфликт версии доверия"));trustedPins=value.Pins;trustVersion=value.Version;id.Trust=signed;Save(id);}
        if(id.Trust!=null)ApplyTrust(id.Trust);
        using var handler=new SocketsHttpHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.None,ConnectTimeout=TimeSpan.FromSeconds(10),MaxResponseHeadersLength=16,PooledConnectionLifetime=TimeSpan.FromMinutes(2)};
        handler.SslOptions=new SslClientAuthenticationOptions{CertificateRevocationCheckMode=X509RevocationMode.NoCheck,CertificateChainPolicy=new X509ChainPolicy{RevocationMode=X509RevocationMode.NoCheck,DisableCertificateDownloads=true},RemoteCertificateValidationCallback=(_,cert,_,_)=>ServerTrust.Accept(cert,endpoint.IdnHost,trustedPins,bootstrap.IssuerCertificate)};
        using var http=new HttpClient(handler){BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(20)};
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if(id.PrivateKey.Length==0){id.PrivateKey=Convert.ToBase64String(key.ExportPkcs8PrivateKey());Save(id);}else key.ImportPkcs8PrivateKey(Convert.FromBase64String(id.PrivateKey),out _);
        var fingerprint=Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));var collector=new LinuxCollector();var journal=new LinuxJournalCollector(directory);
        while(!token.IsCancellationRequested){
            var delay=30;
            try{
                var trustFile=Path.Combine(directory,"trust.json");
                if(File.Exists(trustFile)){
                    if(new FileInfo(trustFile).Length>10000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой файл доверия"));
                    var local=JsonSerializer.Deserialize<AgentSignedTrust>(File.ReadAllText(trustFile),AgentWire.Json)??throw new InvalidDataException();
                    if(AgentTrust.Verify(local,bootstrap.IssuerCertificate).Version>trustVersion)ApplyTrust(local);
                }
                if(id.AgentId.Length==0){
                    var enrollment=new AgentEnrollment(bootstrap.EnrollmentId,bootstrap.Token,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),"",Environment.MachineName);
                    enrollment=enrollment with{Proof=Convert.ToBase64String(key.SignData(AgentWire.EnrollmentBytes(enrollment),HashAlgorithmName.SHA256))};
                    var result=await AgentHttp.SendAsync<AgentEnrollmentResult>(http,"agent/enroll",enrollment,token);if(result.AgentId!=bootstrap.EnrollmentId)throw new InvalidDataException();AgentWire.Validate(result.Policy);ValidateCertificate(result.Certificate,key,bootstrap);id.AgentId=result.AgentId;id.Certificate=result.Certificate;id.Policy=result.Policy;Save(id);
                }
                var report=collector.Read(id.Policy);report.AgentVersion=typeof(AgentWorker).Assembly.GetName().Version!.ToString();report.AgentUpdaterStatus=updater.Status;var applied=LinuxUpdateStatus.Read();if(applied.Length>0)report.AgentUpdaterStatus+=" · "+applied;report.Updates=id.EventPolicy.Updates?updates.Read():null;var rootOutcome=LinuxUpdateStatus.ReadOutcome();var localOutcome=updater.Delivery;var protectsNewerFailure=rootOutcome?.State is "Rejected" or "RollbackFailed" or "RolledBack"&&localOutcome?.State=="Installed"&&Version.TryParse(rootOutcome.Version,out var failedVersion)&&Version.TryParse(localOutcome.Version,out var verifiedVersion)&&failedVersion>verifiedVersion;report.Linux!.UpdateDelivery=!protectsNewerFailure&&localOutcome!=null&&(rootOutcome==null||localOutcome.LastUtc>rootOutcome.LastUtc)?localOutcome:rootOutcome;if(report.AgentUpdaterStatus.Length>500)report.AgentUpdaterStatus=report.AgentUpdaterStatus[..500];report.TrustVersion=trustVersion;report.EventBatch=id.EventPolicy.Enabled?await journal.ReadAsync(token):null;AgentWire.Validate(report);
                var payload=JsonSerializer.Serialize(report,AgentWire.Json);
                if(payload.Length>250000){
                    var required=AgentServiceProfiles.Effective(id.Policy,report.Services).Select(s=>s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    report.Services=report.Services.OrderByDescending(s=>required.Contains(s.Name)).Take(96).Select(s=>s with{DisplayName=s.DisplayName[..Math.Min(96,s.DisplayName.Length)]}).ToArray();
                    report.Errors=report.Errors.Take(15).Append("Список служб усечён по лимиту пакета; выбранные переданы первыми").ToArray();payload=JsonSerializer.Serialize(report,AgentWire.Json);
                    if(payload.Length>250000)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Отчёт не укладывается в лимит"));
                }
                id.Sequence=checked(id.Sequence+1);Save(id); // Persist before sending: a crash cannot reuse a sequence.
                var envelope=new AgentEnvelope(id.AgentId,id.Sequence,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),payload,"");envelope=envelope with{Signature=Convert.ToBase64String(key.SignData(AgentWire.Bytes(envelope),HashAlgorithmName.SHA256))};
                var reply=await AgentHttp.SendAsync<AgentReplyV3>(http,"agent/report-v3",envelope,token);AgentEvents.Validate(reply.Events);ApplyTrust(reply.Trust);AgentWire.Validate(reply.Policy);ValidateCertificate(reply.Certificate,key,bootstrap);id.Policy=reply.Policy;id.Certificate=reply.Certificate;id.EventPolicy=reply.Events;Save(id);journal.Ack(reply.Ack);delay=id.Policy.IntervalSeconds;
                Status("Связь с сервером установлена",fingerprint,id.Policy.Version,trustVersion);
                updater.Schedule(http,bootstrap,id.AgentId,key,report.AgentVersion,token);
            }
            catch(AgentSequenceException e){id.Sequence=e.NextSequence-1;Save(id);Status("Счётчик синхронизирован после восстановления состояния; повтор через 30 секунд",fingerprint,id.Policy.Version,trustVersion);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception e)when(e is HttpRequestException or IOException or CryptographicException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException){var status="Ошибка связи или проверки данных; повтор через 30 секунд";
                if(e is HttpRequestException h&&h.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized){
                    status="Ожидается подтверждение в Assistant либо доступ отозван";
                    if(id.Certificate.Length>0){using var local=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(id.Certificate));if(local.NotAfter.ToUniversalTime()<DateTime.UtcNow)status="Не удалось автоматически продлить сертификат; проверьте доступ и регистрацию агента на сервере";}
                }
                Status(status,fingerprint,id.Policy.Version,trustVersion);}
            try{await Task.Delay(TimeSpan.FromSeconds(delay),token);}catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
        }
    }
    private void Status(string status,string fingerprint,int policy,long trustVersion){Atomic(Path.Combine(directory,"status.json"),JsonSerializer.SerializeToUtf8Bytes(new{TimeUtc=DateTime.UtcNow,AgentVersion=typeof(AgentWorker).Assembly.GetName().Version!.ToString(),Status=status,PublicKeySha256=fingerprint,PolicyVersion=policy,TrustVersion=trustVersion}));}
    private static void ValidateCertificate(string raw,ECDsa key,AgentBootstrap bootstrap)
    {
        using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(raw));using var issuer=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(bootstrap.IssuerCertificate));using var publicKey=cert.GetECDsaPublicKey()??throw new CryptographicException();
        using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;chain.ChainPolicy.CustomTrustStore.Add(issuer);chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;chain.ChainPolicy.DisableCertificateDownloads=true;
        if(!ServerTrust.IssuedBy(cert,bootstrap.IssuerCertificate)||!publicKey.ExportSubjectPublicKeyInfo().SequenceEqual(key.ExportSubjectPublicKeyInfo())||cert.GetNameInfo(X509NameType.SimpleName,false)!="SAA-Agent-"+bootstrap.EnrollmentId)throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Сертификат агента не соответствует ключу"));
    }
}
