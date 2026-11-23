using System.Text.Json;
using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;
public sealed record KscRule(string Id,string Title,string OpenEvent,string RecoveryEvent,Severity Level,bool Notify)
{
 public bool Enabled {get;init;}=true;
}
public sealed record KscRuleView(string Revision,KscRule[] Rules);
public sealed record KscRuleFile(int Format,string Product,KscRule[] Rules);
public sealed record KscResolve(int DeviceId,string Key);
public sealed record KscBinding(Guid Source,string Host,int DeviceId);
public static class KscRules{
 // KSC Open API, List of event attributes (event_type).
 public static KscRule[] Standard()=>[
  new("suspicious-object-found","Обнаружен подозрительный объект","GNRL_EV_SUSPICIOUS_OBJECT_FOUND","",Severity.Warning,true),
  new("virus-found","Обнаружен вирус","GNRL_EV_VIRUS_FOUND","",Severity.Critical,true),
  new("object-not-cured","Не удалось вылечить объект","GNRL_EV_OBJECT_NOTCURED","",Severity.Critical,true)
 ];

 public static KscRule[] Merge(IEnumerable<KscRule> existing,KscRule[] incoming){
  var next=existing.Where(r=>!incoming.Any(n=>n.Id==r.Id)).Concat(incoming).ToArray();
  return Parse(Export(next));
 }
 public static string Export(IEnumerable<KscRule> rules)=>JsonSerializer.Serialize(new KscRuleFile(1,"KSC",rules.ToArray()),new JsonSerializerOptions{WriteIndented=true});
 public static KscRule[] Parse(string json){
  if(json.Length>262144)throw new ArgumentException("KSC: файл превышает 256 КБ");
  var f=JsonSerializer.Deserialize<KscRuleFile>(json,new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new InvalidDataException();
  if(f.Format!=1||f.Product!="KSC"||f.Rules==null||f.Rules.Length is <1 or >128)throw new ArgumentException("KSC: неверный формат или количество правил");
  HashSet<string> ids=new(StringComparer.Ordinal);HashSet<string> events=new(StringComparer.Ordinal);
  foreach(var r in f.Rules){if(r==null||string.IsNullOrWhiteSpace(r.Id)||r.Id.Length>64||!r.Id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_')||!ids.Add(r.Id)||string.IsNullOrWhiteSpace(r.Title)||r.Title.Length>160||string.IsNullOrWhiteSpace(r.OpenEvent)||r.OpenEvent.Length>256||!events.Add(r.OpenEvent)||r.RecoveryEvent==null||r.RecoveryEvent.Length>256||r.RecoveryEvent==r.OpenEvent||r.Level is not (Severity.Warning or Severity.Critical))throw new ArgumentException("KSC: неверное или повторяющееся правило");}
  return f.Rules;
 }
}
