using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed record AgentTrustDocument(long Version,string[] Pins);
public sealed record AgentSignedTrust(string Payload,string Signature);
public sealed record AgentReplyV2(AgentPolicy Policy,string Certificate,AgentSignedTrust Trust);
public static class AgentTrust
{
    public static AgentTrustDocument Verify(AgentSignedTrust signed,string issuer,long minimumVersion=0)
    {
        if(signed==null||signed.Payload==null||signed.Signature==null||signed.Payload.Length>4096||signed.Signature.Length>256)throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Неверный файл доверия"));
        using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(issuer));using var key=cert.GetECDsaPublicKey()??throw new CryptographicException();
        if(!key.VerifyData(Encoding.UTF8.GetBytes("SAA-AGENT-TRUST-1\n"+signed.Payload),Convert.FromBase64String(signed.Signature),HashAlgorithmName.SHA256))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Файл доверия подписан другим сервером или изменён"));
        var result=JsonSerializer.Deserialize<AgentTrustDocument>(signed.Payload,AgentWire.Json)??throw new CryptographicException();
        if(result.Version<minimumVersion||result.Version is <1 or >9000000000000||result.Pins==null||result.Pins.Length is <1 or >8||result.Pins.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=result.Pins.Length||result.Pins.Any(p=>p==null||p.Length!=64||!p.All(Uri.IsHexDigit)))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Неверные отпечатки или устаревший файл доверия"));
        return result;
    }
}
