using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed record LinuxAgentReleaseDocument(string AgentId,string Nonce,string Version,DateTime ExpiresUtc,long Length,string Sha256);
public sealed record LinuxAgentSignedRelease(string Payload,string Signature);
public sealed class LinuxReleaseException(string code,string message):CryptographicException(message){public string Code {get;}=code;}
public static class LinuxAgentRelease
{
    public static byte[] Content(string payload)=>Encoding.UTF8.GetBytes("SAA-LINUX-RELEASE-1\n"+payload);
    public static void Validate(LinuxAgentReleaseDocument d,DateTime? clock=null)
    {
        var now=clock??DateTime.UtcNow;
        if(!Guid.TryParseExact(d.AgentId,"N",out _)||!Guid.TryParseExact(d.Nonce,"N",out _)||!Version.TryParse(d.Version,out _)||d.ExpiresUtc.Kind!=DateTimeKind.Utc||d.Length is <1 or >150_000_000||d.Sha256==null||d.Sha256.Length!=64||!d.Sha256.All(Uri.IsHexDigit))throw new LinuxReleaseException("ManifestFields",global::SysAdminAssistant.Core.ProductText.Get("Неверные поля манифеста Linux-агента"));
        if(d.ExpiresUtc<=now)throw new LinuxReleaseException("ManifestExpired",$"Истёк срок подписанного манифеста: {d.ExpiresUtc:O}; время проверки {now:O}. Требуется свежий пакет сервера.");
        if(d.ExpiresUtc>now.AddMinutes(15))throw new LinuxReleaseException("ManifestFuture",global::SysAdminAssistant.Core.ProductText.Get("Срок манифеста вне допустимого окна 15 минут; проверьте часы агента и сервера"));
    }
    // Authentication is separate from installation authorization. An expired *authentic*
    // package may be discarded as already installed, but may never be installed.
    public static LinuxAgentReleaseDocument Authenticate(LinuxAgentSignedRelease signed,string issuer,string agentId)
    {
        try{
            if(signed.Payload==null||signed.Signature==null||signed.Payload.Length>10000||signed.Signature.Length>256)throw new LinuxReleaseException("EnvelopeFields",global::SysAdminAssistant.Core.ProductText.Get("Payload/Signature отсутствуют или превышают допустимый размер"));
            using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(issuer));using var key=cert.GetECDsaPublicKey()??throw new LinuxReleaseException("IssuerKey",global::SysAdminAssistant.Core.ProductText.Get("У издателя нет ключа ECDSA"));
            if(!key.VerifyData(Content(signed.Payload),Convert.FromBase64String(signed.Signature),HashAlgorithmName.SHA256))throw new LinuxReleaseException("SignatureMismatch",global::SysAdminAssistant.Core.ProductText.Get("Цифровая подпись манифеста не соответствует Payload и доверенному издателю"));
            var d=JsonSerializer.Deserialize<LinuxAgentReleaseDocument>(signed.Payload,AgentWire.Json)??throw new LinuxReleaseException("ManifestJson",global::SysAdminAssistant.Core.ProductText.Get("Payload не содержит манифест"));
            if(d.AgentId!=agentId)throw new LinuxReleaseException("AgentIdentity",global::SysAdminAssistant.Core.ProductText.Get("Подписанный пакет предназначен другому агенту"));
            if(!Guid.TryParseExact(d.Nonce,"N",out _)||!Version.TryParse(d.Version,out _)||d.ExpiresUtc.Kind!=DateTimeKind.Utc||d.Length is <1 or >150_000_000||d.Sha256==null||d.Sha256.Length!=64||!d.Sha256.All(Uri.IsHexDigit))throw new LinuxReleaseException("ManifestFields",global::SysAdminAssistant.Core.ProductText.Get("Неверные поля подписанного манифеста"));
            return d;
        }catch(LinuxReleaseException){throw;}catch(FormatException){throw new LinuxReleaseException("EnvelopeEncoding",global::SysAdminAssistant.Core.ProductText.Get("Payload/Signature или сертификат имеют неверное кодирование Base64"));}catch(JsonException){throw new LinuxReleaseException("ManifestJson",global::SysAdminAssistant.Core.ProductText.Get("Подписанный Payload имеет неверный формат JSON"));}catch(CryptographicException){throw new LinuxReleaseException("IssuerOrSignature",global::SysAdminAssistant.Core.ProductText.Get("Не удалось проверить ключ издателя или формат цифровой подписи"));}
    }
    public static LinuxAgentReleaseDocument Verify(LinuxAgentSignedRelease signed,string issuer,string agentId,string nonce,string current,DateTime? clock=null)
    {
        var d=Authenticate(signed,issuer,agentId);Validate(d,clock);
        if(d.Nonce!=nonce)throw new LinuxReleaseException("RequestNonce",global::SysAdminAssistant.Core.ProductText.Get("Манифест не соответствует запросу обновления"));
        if(Version.Parse(d.Version)<Version.Parse(current))throw new LinuxReleaseException("VersionDowngrade",global::SysAdminAssistant.Core.ProductText.Get("Понижение версии Linux-агента запрещено"));
        return d;
    }
    public static void VerifyFile(Stream stream,LinuxAgentReleaseDocument release)
    {
        if(!stream.CanSeek||stream.Length!=release.Length)throw new LinuxReleaseException("BinaryLength",global::SysAdminAssistant.Core.ProductText.Get("Размер release.bin не соответствует подписанному манифесту"));
        stream.Position=0;var actual=Convert.ToHexString(SHA256.HashData(stream));if(!actual.Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new LinuxReleaseException("BinaryHash",global::SysAdminAssistant.Core.ProductText.Get("SHA256 release.bin не соответствует подписанному манифесту"));stream.Position=0;
    }
}
