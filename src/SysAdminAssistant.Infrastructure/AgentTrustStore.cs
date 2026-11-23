using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record AgentTrustStage(string Certificate);
public sealed partial class AgentStore
{
    // Existing key, approval and revocation remain authoritative. One bounded recovery window.
    public void AllowRecovery(int deviceId)=>State(s=>{
        var a=s.Agents.SingleOrDefault(a=>a.DeviceId==deviceId&&a.Approved&&!a.Revoked)??throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Агент не подтверждён или отозван"));
        using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(a.Certificate));
        if(cert.NotAfter.ToUniversalTime()>DateTime.UtcNow)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Сертификат ещё действителен; обычное продление автоматическое"));
        a.RecoveryUntil=DateTime.UtcNow.AddHours(24);return true;
    },true);
    private AgentSignedTrust SignTrust(AgentIndex s){
        if(s.TrustPins.Length==0)s.TrustPins=[serverPin];
        else if(!s.TrustPins.Contains(serverPin,StringComparer.OrdinalIgnoreCase)){
            if(s.TrustPins.Length>=8)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Достигнут лимит доверия. Главному администратору нужно оставить только текущий HTTPS-сертификат и экспортировать новый файл."));
            s.TrustPins=s.TrustPins.Append(serverPin).ToArray();s.TrustVersion=checked(s.TrustVersion+1);
        }
        var pins=s.TrustPins;
        var payload=JsonSerializer.Serialize(new AgentTrustDocument(s.TrustVersion,pins),AgentWire.Json);
        using var ca=Ca(s);using var key=ca.GetECDsaPrivateKey()!;
        return new(payload,Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes("SAA-AGENT-TRUST-1\n"+payload),HashAlgorithmName.SHA256)));
    }
    public AgentSignedTrust Trust()=>State(SignTrust,true);
    public AgentSignedTrust StageTrust(AgentTrustStage request)=>State(s=>{
        if(request.Certificate==null||request.Certificate.Length>30000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Нужен публичный DER-сертификат .cer без закрытого ключа"));
        using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(request.Certificate));
        if(cert.NotAfter.ToUniversalTime()<DateTime.UtcNow.AddDays(1)||cert.NotBefore.ToUniversalTime()>DateTime.UtcNow.AddDays(30))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Срок нового сертификата непригоден для перехода"));
        var pins=s.TrustPins.Append(serverPin).Append(cert.GetCertHashString(HashAlgorithmName.SHA256)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(pins.Length>8)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Лимит 8 доверенных отпечатков; удалите старые через управление доверием"));
        s.TrustPins=pins;s.TrustVersion=checked(s.TrustVersion+1);return SignTrust(s);
    },true);
    public AgentSignedTrust ResetTrust()=>State(s=>{s.TrustPins=[serverPin];s.TrustVersion=checked(s.TrustVersion+1);return SignTrust(s);},true);
}
