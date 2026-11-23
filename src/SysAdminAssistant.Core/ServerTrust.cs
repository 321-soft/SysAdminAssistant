using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace SysAdminAssistant.Core;
public sealed record ServerTrustInfo(string AuthorityCertificate,AgentSignedTrust Trust,string Fingerprint,DateTime NotAfterUtc,string ServerUrl);
public static class ServerTrust
{
 public static bool SameAuthority(string a,string b){using var first=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(a));using var second=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(b));return first.PublicKey.ExportSubjectPublicKeyInfo().SequenceEqual(second.PublicKey.ExportSubjectPublicKeyInfo());}
 public static bool ValidLeaf(X509Certificate2 c,DateTime now)=>c.NotBefore.ToUniversalTime()<=now&&c.NotAfter.ToUniversalTime()>now;
 public static bool IssuedBy(X509Certificate2 leaf,string authority,string? host=null,DateTime? now=null){try{
  if(!ValidLeaf(leaf,now??DateTime.UtcNow)||authority.Length is <1 or >30000)return false;using var root=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(authority));using var chain=new X509Chain();chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;chain.ChainPolicy.CustomTrustStore.Add(root);chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;chain.ChainPolicy.DisableCertificateDownloads=true;
  // The pinned public key is the application anchor. Root-wrapper expiry cannot revoke that key.
  chain.ChainPolicy.VerificationFlags=X509VerificationFlags.IgnoreNotTimeValid;if(now.HasValue)chain.ChainPolicy.VerificationTime=now.Value;
  if(host!=null){var eku=leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();if(eku==null||!eku.EnhancedKeyUsages.Cast<Oid>().Any(x=>x.Value=="1.3.6.1.5.5.7.3.1")||!leaf.MatchesHostname(host,false,false))return false;chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));}
  return chain.Build(leaf)&&chain.ChainElements.Count==2&&chain.ChainElements[1].Certificate.PublicKey.ExportSubjectPublicKeyInfo().SequenceEqual(root.PublicKey.ExportSubjectPublicKeyInfo());
 }catch(Exception e)when(e is CryptographicException or ArgumentException or FormatException){return false;}}
 public static bool Accept(X509Certificate? certificate,string host,IEnumerable<string> pins,string authority){if(certificate==null)return false;try{using var c=X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());return ValidLeaf(c,DateTime.UtcNow)&&(pins.Contains(c.GetCertHashString(HashAlgorithmName.SHA256),StringComparer.OrdinalIgnoreCase)||IssuedBy(c,authority,host));}catch(CryptographicException){return false;}}
}
