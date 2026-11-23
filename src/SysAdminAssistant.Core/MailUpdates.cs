using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
namespace SysAdminAssistant.Core;
public sealed record MailUpdateMatch(string Provider,string Product,string Version,string Key,string Issue="");
public sealed class MailUpdateItem
{
 public string Key {get;set;}="";public string Product {get;set;}="";public string Version {get;set;}="";public int ReportId {get;set;}[System.Text.Json.Serialization.JsonConverter(typeof(WallTimeJsonConverter))] public DateTime FirstReceived {get;set;}[System.Text.Json.Serialization.JsonConverter(typeof(WallTimeJsonConverter))] public DateTime LastReceived {get;set;}public DateTime? InstalledUtc {get;set;}public string ConfirmedBy {get;set;}="";
 public bool Withdrawn {get;set;}
 public string Status=>Withdrawn?"Привязка отменена":InstalledUtc!=null?"Отмечено установленным":"Доступно по письму";
}
public sealed record MailUpdatesView(int DeviceId,string Hash,MailUpdateItem[] Items,OsUpdateStatus? Os=null);
public sealed record MailUpdateConfirm(int DeviceId,string Key,string ExpectedHash);
public sealed record DeviceUpdateStatus(OsUpdateStatus? Os,MailUpdateItem[] Mail);
public static class MailUpdates
{
 public static string[] Terms(string s)=>s.Replace('\r','\n').Split([';','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
 public static bool Matches(KeywordProfile p,MailReport mail){var checks=new List<bool>();var a=Terms(p.UpdateSubjectTerms);var b=Terms(p.UpdateBodyTerms);if(a.Length>0)checks.Add(a.Any(t=>mail.Subject.Contains(t,StringComparison.OrdinalIgnoreCase)));if(b.Length>0)checks.Add(b.Any(t=>mail.Body.Contains(t,StringComparison.OrdinalIgnoreCase)));if(checks.Count==0)return false;var text=mail.Subject+"\n"+mail.Body;if(p.Provider=="DSM"&&!text.Contains("DSM",StringComparison.OrdinalIgnoreCase)&&!text.Contains("Synology",StringComparison.OrdinalIgnoreCase))return false;if(p.Provider=="QNAP"&&!new[]{"QNAP","QTS","QuTS"}.Any(t=>text.Contains(t,StringComparison.OrdinalIgnoreCase)))return false;return p.UpdateMatchAll?checks.All(x=>x):checks.Any(x=>x);}
 public static void Defaults(KeywordProfile p){if(p.UpdateDictionaryVersion>0)return;if(p.Provider=="DSM"){p.UpdateProduct="DSM";p.UpdateSubjectTerms="обнаружено новое обновление DSM;new DSM update;DSM update available";p.UpdateBodyTerms="доступна новая версия DSM;new version of DSM is available;new DSM version is available";}if(p.Provider=="QNAP"){p.UpdateProduct="QTS";p.UpdateSubjectTerms="доступна новая версия прошивки;новое обновление прошивки;new firmware update;new firmware version;firmware update available";p.UpdateBodyTerms="firmware update is available;new firmware version is available;доступна новая версия прошивки";}p.UpdateDictionaryVersion=1;}
 public static MailUpdateMatch Extract(MailReport mail,string provider,string product){product=product.Trim();if(provider=="QNAP"&&(mail.Body+mail.Subject).Contains("QuTS hero",StringComparison.OrdinalIgnoreCase))product="QuTS hero";if(product=="")product=provider;
 var text=mail.Subject+"\n"+mail.Body;var pattern=@"(?<![\p{L}\d])"+Regex.Escape(product)+@"\s*(?:(?:version|верси[яию])\s*)?[:v]?\s*(?<v>h?\d+(?:\.\d+){1,4}(?:-\d+)?(?:\s*(?:Update|обновление|build)\s*\d+)?)(?!(?:\d|\.\d))";
 var versions=Regex.Matches(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(m=>Regex.Replace(Regex.Replace(m.Groups["v"].Value,@"(?i)обновление","Update"),@"\s+"," ")).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();string version=versions.Length==1?versions[0]:"";if(version.Length>128)version="";
 var identity=product.ToUpperInvariant()+"\0"+(version!=""?version.ToUpperInvariant():mail.Subject.Trim()+"\0"+mail.Body.Trim());var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));return new(provider,product,version,key,versions.Length>1?"В письме несколько версий; требуется ручная проверка":version==""?"Номер версии не извлечён; подтверждение относится только к этому тексту письма":"");}
 public static MailUpdateItem[] Parse(string json)=>string.IsNullOrEmpty(json)?[]:JsonSerializer.Deserialize<MailUpdateItem[]>(json)??throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Повреждена история обновлений устройства"));
 public static string Serialize(IEnumerable<MailUpdateItem> rows)=>JsonSerializer.Serialize(rows,new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
 public static OsUpdateStatus? Overview(string os,string mails){var rows=Parse(mails).Where(x=>x.InstalledUtc==null&&!x.Withdrawn).ToArray();var value=os==""?null:OsUpdateStatus.Parse(os);if(rows.Length==0)return value;return new(){State="Available",CheckedUtc=value?.CheckedUtc,Installed=value?.Installed??"",Available=value?.Available??"",RebootRequired=value?.RebootRequired??false,PendingCount=(value?.PendingCount??0)+rows.Length,Items=(value?.Items??[]).Concat(rows.Select(x=>x.Product+" "+(x.Version==""?"версия не указана":x.Version)+" · по письму")).ToArray(),Detail="Есть уведомления об обновлениях по почте; установка не подтверждена"};}
}
