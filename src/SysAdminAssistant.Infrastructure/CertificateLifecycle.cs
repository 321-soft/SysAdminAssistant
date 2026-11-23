using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class CertificateNotice {public string Fingerprint {get;set;}="";public DateTime ExpiresUtc {get;set;}public DateTime? SentUtc {get;set;}}
public sealed class CertificateLifecycleState {public string Current {get;set;}="";public string Next {get;set;}="";public DateTime? ActivateUtc {get;set;}public List<CertificateNotice> Notices {get;set;}=[];}
public sealed record CertificateStatus(string Fingerprint,DateTime ExpiresUtc,string NextFingerprint,DateTime? ActivateUtc,long TrustVersion,int Confirmed,int Agents,int PendingTelegram,string Error,AgentTrustConfirmation[]? AgentDetails=null);
public sealed class CertificateLifecycle:IDisposable
{
 private readonly string directory,password,ip;private readonly AgentStore agents;private readonly object gate=new();private readonly List<X509Certificate2> retained=[];private CertificateLifecycleState state;private X509Certificate2 current;
 public string Error {get;private set;}="";public X509Certificate2 Current=>Volatile.Read(ref current);
 private string StatePath=>Path.Combine(directory,"certificate-lifecycle.json");private string NextPath=>Path.Combine(directory,"server-next.pfx");private static string Pin(X509Certificate2 c)=>c.GetCertHashString(HashAlgorithmName.SHA256);
 public CertificateLifecycle(string directory,string password,string ip,AgentStore agents){this.directory=directory;this.password=password;this.ip=ip;this.agents=agents;current=Load(Path.Combine(directory,"server.pfx"));retained.Add(current);state=File.Exists(StatePath)?JsonSerializer.Deserialize<CertificateLifecycleState>(File.ReadAllText(StatePath))??throw new InvalidDataException():new(){Current=Pin(current)};if(state.Next==Pin(current))Complete();if(state.Current!=Pin(current))throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("HTTPS-сертификат изменён вне процедуры продления"));agents.PublishServerPins(Pin(current),state.Next.Length==0?null:state.Next);}
 private X509Certificate2 Load(string p){var c=X509CertificateLoader.LoadPkcs12FromFile(p,password,OperatingSystem.IsWindows()?X509KeyStorageFlags.MachineKeySet|X509KeyStorageFlags.PersistKeySet|X509KeyStorageFlags.Exportable:X509KeyStorageFlags.EphemeralKeySet|X509KeyStorageFlags.Exportable);if(!c.HasPrivateKey){c.Dispose();throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Нет закрытого ключа HTTPS"));}return c;}
 private static void Write(string path,byte[] bytes){var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){f.Write(bytes);f.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 private void Save()=>Write(StatePath,JsonSerializer.SerializeToUtf8Bytes(state));
 private void Complete(){var pin=Pin(current);if(!state.Notices.Any(n=>n.Fingerprint==pin))state.Notices.Add(new(){Fingerprint=pin,ExpiresUtc=current.NotAfter.ToUniversalTime()});state.Current=pin;state.Next="";state.ActivateUtc=null;Save();agents.PublishServerPins(pin);if(File.Exists(NextPath))File.Delete(NextPath);}
 public bool Maintain(DateTime now){lock(gate){_=agents.AuthorityCertificate();bool changed=false;
  if(state.Next.Length==0&&current.NotAfter.ToUniversalTime()<=now.AddDays(60)){using var next=agents.IssueServerCertificate(ip,now);Write(NextPath,next.Export(X509ContentType.Pfx,password));state.Next=Pin(next);state.ActivateUtc=new[]{now.AddDays(7),current.NotAfter.ToUniversalTime().AddDays(-1)}.Min();Save();changed=true;}
  agents.PublishServerPins(Pin(current),state.Next.Length==0?null:state.Next);
  if(state.Next.Length>0&&state.ActivateUtc<=now){var next=Load(NextPath);if(Pin(next)!=state.Next||!ServerTrust.IssuedBy(next,agents.AuthorityCertificate(),ip,now)){next.Dispose();throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Новый сертификат не прошёл проверку"));}Write(Path.Combine(directory,"server.pfx"),next.Export(X509ContentType.Pfx,password));retained.Add(next);Volatile.Write(ref current,next);Complete();changed=true;}
  Error="";return changed;
 }}
 public void Failed()=>Error="Автоматическое продление сертификата не выполнено; проверьте доступ службы к каталогу и ключу. Повтор через минуту.";
 public ServerTrustInfo Trust(string url){lock(gate)return new(agents.AuthorityCertificate(),agents.Trust(),Pin(current),current.NotAfter.ToUniversalTime(),url);}
 public CertificateStatus Status(bool includeAgentDetails=false){lock(gate){var list=agents.TrustConfirmations();var version=AgentTrust.Verify(agents.Trust(),agents.AuthorityCertificate()).Version;return new(Pin(current),current.NotAfter.ToUniversalTime(),state.Next,state.ActivateUtc,version,list.Count(a=>a.TrustVersion>=version),list.Length,state.Notices.Count(n=>n.SentUtc==null),Error,includeAgentDetails?list:null);}}
 public async Task SendAsync(TelegramSettings settings,string url,string zone,CancellationToken token,Func<string,CancellationToken,Task>? send=null){if(!settings.Enabled)return;CertificateNotice[] notices;lock(gate)notices=state.Notices.Where(n=>n.SentUtc==null).Take(10).ToArray();foreach(var n in notices){var message=$"🔵 SysAdmin Assistant · сертификат сервера перевыпущен\nСервер: {url}\nSHA-256:\n{n.Fingerprint}\nДействует до: {AppTime.InZone(n.ExpiresUtc,zone):dd.MM.yyyy HH:mm}\nПояс: {zone}\nДоверие клиентов и агентов обновляется автоматически.";await(send?.Invoke(message,token)??TelegramSender.SendAsync(settings,message,token));lock(gate){n.SentUtc=DateTime.UtcNow;state.Notices=state.Notices.Where(x=>x.SentUtc==null).Concat(state.Notices.Where(x=>x.SentUtc!=null).TakeLast(30)).ToList();Save();}}}
 public void Dispose(){foreach(var c in retained)c.Dispose();}
}
