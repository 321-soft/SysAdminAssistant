using SysAdminAssistant.Core;
using System.Collections.Frozen;
namespace SysAdminAssistant.Infrastructure;
public static class RouterAuditReader
{
 public static readonly FrozenDictionary<string,string> Commands=new Dictionary<string,string>(){["ipsec-policies"]="/ip/ipsec/policy/print",["filter4"]="/ip/firewall/filter/print",["raw4"]="/ip/firewall/raw/print",["nat4"]="/ip/firewall/nat/print",["filter6"]="/ipv6/firewall/filter/print",["raw6"]="/ipv6/firewall/raw/print",["address-lists4"]="/ip/firewall/address-list/print",["address-lists6"]="/ipv6/firewall/address-list/print",["addresses4"]="/ip/address/print",["addresses6"]="/ipv6/address/print",["routes4"]="/ip/route/print",["routes6"]="/ipv6/route/print",["interfaces"]="/interface/print",["lists"]="/interface/list/print",["members"]="/interface/list/member/print",["services"]="/ip/service/print",["dns"]="/ip/dns/print",["ipv6-settings"]="/ipv6/settings/print",["mac"]="/tool/mac-server/print",["mac-winbox"]="/tool/mac-server/mac-winbox/print",["bridge-ports"]="/interface/bridge/port/print",["switches"]="/interface/ethernet/switch/print",["mangle4"]="/ip/firewall/mangle/print",["mangle6"]="/ipv6/firewall/mangle/print",["neighbors"]="/ip/neighbor/discovery-settings/print"}.ToFrozenDictionary();
 public static async Task<RouterAuditConfig> ReadAsync(RouterOsClient router,CancellationToken token){var result=new RouterAuditConfig();long total=0;foreach(var item in Commands.Where(p=>!p.Key.StartsWith("address-lists"))){token.ThrowIfCancellationRequested();try{var words=item.Key=="ipsec-policies"?new[]{item.Value,"=.proplist=.id,src-address,dst-address,protocol,src-port,dst-port,action,template,disabled,invalid,tunnel"}:item.Key=="dns"?new[]{item.Value,"=.proplist=allow-remote-requests"}:item.Key=="services"?new[]{item.Value,"=.proplist=.id,name,port,address,disabled"}:new[]{item.Value};var rows=await router.CommandAsync(words,token);total+=rows.Sum(r=>r.Sum(p=>p.Key.Length+p.Value.Length));if(rows.Count>2000||total>1000000){result.Errors[item.Key]="Превышен предел правил / настроек; address lists проверяются отдельно";continue;}result.Tables[item.Key]=rows.Select(r=>new Dictionary<string,string>(r)).ToList();}catch(InvalidOperationException e)when(e is not RouterConnectionException){result.Errors[item.Key]=e.Message;}}
 foreach(var bits in new[]{32,128}){var table=bits==32?"address-lists4":"address-lists6";result.Tables[table]=[];var references=RouterAuditAddresses.Referenced(result,bits);if(references.Length>128){result.Errors[table]="Слишком много используемых списков (более 128)";continue;}foreach(var name in references){var accumulator=new RouterAuditAddresses(name,bits);try{await router.ReadAddressListAsync(Commands[table],name,accumulator.Add,token);var normalized=accumulator.Finish();if(result.Tables[table].Count+normalized.Count>500000){result.Errors[table+":"+name]="Превышен предел нормализованных диапазонов семейства (500000)";continue;}result.Tables[table].AddRange(normalized);}catch(InvalidOperationException e)when(e is not RouterConnectionException){result.Errors[table+":"+name]=e.Message;}}}return result;}

}
public sealed record RouterAuditIgnore(int DeviceId,string Key,string Fingerprint,bool Ignore,string ExpectedHash);
public sealed record RouterAuditPolicy(int DeviceId,string[] WanInterfaces,string ExpectedHash);
public sealed record RouterAuditView(int DeviceId,string Hash,string[] ManualWan,RouterAuditResult? Result,string[] Interfaces);
public sealed class RouterAuditIgnored {public string Key {get;set;}="";public string Fingerprint {get;set;}="";public string Actor {get;set;}="";public DateTime TimeUtc {get;set;}}
public sealed class RouterAuditState {public string Host {get;set;}="";public string[] ManualWan {get;set;}=[];public RouterAuditResult? Result {get;set;}public List<RouterAuditIgnored> Ignored {get;set;}=[];}
public static class RouterAuditStore
{
 public static RouterAuditView View(NetworkStore store,int id)=>store.State(s=>{var h=s.Histories.SingleOrDefault(h=>h.DeviceId==id)??new(){DeviceId=id};return new RouterAuditView(id,RemoteData.Hash(h.Audit),h.Audit.ManualWan,h.Audit.Result,h.Snapshot?.Interfaces.Select(i=>i.GetValueOrDefault("name","")).Where(n=>n!="").ToArray()??[]);});
 private static string IssueKey(string key)=>key.StartsWith("dns:",StringComparison.Ordinal)&&(key.EndsWith(":tcp",StringComparison.Ordinal)||key.EndsWith(":udp",StringComparison.Ordinal))?key[..key.LastIndexOf(':')]:key;
 public static void Apply(NetworkState s,NetworkHistory h,RouterAuditResult result,string name,bool notify=true){
  var prefix="router-audit:"+h.DeviceId+":";var statePrefix=prefix+"issue:";
  var old=new Dictionary<string,Severity>(StringComparer.Ordinal);
  foreach(var item in s.ApplicationStates.Where(p=>p.Key.StartsWith(statePrefix)))if(Enum.TryParse<Severity>(item.Value,out var level))old[item.Key[statePrefix.Length..]]=level;
  // Upgrade the per-port notification state without replaying already delivered findings.
  if(old.Count==0&&s.ApplicationStates.Keys.Any(k=>k.StartsWith(prefix))&&h.Audit.Result!=null)
   foreach(var f in h.Audit.Result.Findings.Where(f=>!f.Ignored&&f.Severity is Severity.Warning or Severity.Critical))old[IssueKey(f.Key)]=f.Severity;
  if(h.Audit.Result!=null)foreach(var saved in h.Audit.Ignored.ToArray()){
   var prior=h.Audit.Result.Findings.FirstOrDefault(f=>f.Key==saved.Key);if(prior==null||!saved.Key.StartsWith("nat:")||saved.Key.StartsWith("nat:tcp:")||saved.Key.StartsWith("nat:udp:"))continue;
   foreach(var current in result.Findings.Where(f=>f.Key.StartsWith("nat:")&&f.Title==prior.Title&&f.Evidence.Split('\n').Any(line=>line.StartsWith("IPv")&&prior.Evidence.Split('\n').Contains(line))))
    if(!h.Audit.Ignored.Any(i=>i.Key==current.Key))h.Audit.Ignored.Add(new(){Key=current.Key,Fingerprint=current.Fingerprint,Actor=saved.Actor,TimeUtc=saved.TimeUtc});
  }
  // Apply saved publication exceptions as well as legacy individual-port exceptions.
  foreach(var group in result.DisplayFindings){
   var groupIgnore=h.Audit.Ignored.SingleOrDefault(i=>i.Key==group.Key);
   foreach(var f in group.Members.Count>0?group.Members:[group]){
    var ignored=groupIgnore??h.Audit.Ignored.SingleOrDefault(i=>i.Key==f.Key);
    f.Ignored=ignored!=null;f.IgnoredBy=ignored?.Actor??"";f.IgnoredUtc=ignored?.TimeUtc;
   }
  }
  var active=result.DisplayFindings.Where(f=>!f.Ignored&&f.Severity is Severity.Warning or Severity.Critical).GroupBy(f=>IssueKey(f.Key)).Select(g=>g.OrderByDescending(f=>f.Severity).First()).ToArray();
  foreach(var group in result.DisplayFindings.Where(f=>f.Members.Count>0)){
   var levels=group.Members.Where(f=>old.ContainsKey(IssueKey(f.Key))).Select(f=>old[IssueKey(f.Key)]).ToArray();
   if(levels.Length>0&&!old.ContainsKey(group.Key))old[group.Key]=levels.Max();
  }
  var pending=s.Notifications.Where(n=>n.EntityKey.StartsWith(prefix)&&n.HandledAt==null).ToArray();
  var legacy=pending.Any(n=>!n.EntityKey.StartsWith(prefix+"summary:"));
  var summary=pending.FirstOrDefault(n=>n.EntityKey.StartsWith(prefix+"summary:"));
  var delivered=s.Notifications.Any(n=>n.EntityKey.StartsWith(prefix)&&n.Result=="Отправлено");
  var changed=active.Any(f=>!old.TryGetValue(IssueKey(f.Key),out var level)||f.Severity>level);
  foreach(var key in s.ApplicationStates.Keys.Where(k=>k.StartsWith(prefix)).ToArray())s.ApplicationStates.Remove(key);
  if(notify)foreach(var f in active)s.ApplicationStates[statePrefix+IssueKey(f.Key)]=f.Severity.ToString();
  foreach(var n in pending.Where(n=>n!=summary)){n.HandledAt=DateTime.UtcNow;n.Result=notify?"Объединено в сводку проверки":"Обслуживание устройства";}
  if(!notify){if(summary!=null){summary.HandledAt=DateTime.UtcNow;summary.Result="Обслуживание устройства";}h.Audit.Result=result;return;}
  var recovered=active.Length==0&&(old.Count>0&&delivered||summary?.Transition.StartsWith("ЗАКРЫТО")==true);
  if(active.Length==0&&!recovered){if(summary!=null){summary.HandledAt=DateTime.UtcNow;summary.Result="Проблема закрыта до отправки уведомления";}h.Audit.Result=result;return;}
  if(changed||legacy||recovered||summary!=null){
   summary??=new(){EntityKey=prefix+"summary:"+Guid.NewGuid().ToString("N"),Topic="router.audit",Source="Конфигурация MikroTik · "+name};
   if(!s.Notifications.Contains(summary))s.Notifications.Add(summary);
   summary.Topic="router.audit";summary.Source="Конфигурация MikroTik · "+name;
   summary.Severity=recovered?(old.Count>0?old.Values.Max():summary.Severity):active.Max(f=>f.Severity);
   summary.Transition=recovered?"ЗАКРЫТО / ВОССТАНОВЛЕНО":"ПРОБЛЕМА";
   summary.Title=recovered?"Проверка конфигурации: активных проблем нет.":
    "Сводка проверки: критичных — "+active.Count(f=>f.Severity==Severity.Critical)+", предупреждений — "+active.Count(f=>f.Severity==Severity.Warning)+".\n"+
    string.Join("\n",active.GroupBy(f=>f.Key.Split(':')[0]).OrderBy(g=>g.Key).Select(g=>Category(g.Key)+": "+g.Count()))+
    "\nПодробности и команды: Коммутаторы → Анализ конфигурации. Проверки отдельных портов не отправляются по одному.";
  }
  h.Audit.Result=result;
 }
 private static string Category(string key)=>key switch{"forward"=>"Транзитные пути","nat" or "publication"=>"Публикации NAT","dns"=>"DNS","service"=>"Службы управления","read"=>"Недоступные разделы",_=>"Прочие проверки ("+key+")"};
 public static void CoalescePending(NetworkState state){
  var groups=state.Notifications.Where(n=>n.HandledAt==null&&n.EntityKey.StartsWith("router-audit:")&&!n.EntityKey.Contains(":summary:")).GroupBy(n=>n.EntityKey.Split(':')[1]).ToArray();
  foreach(var group in groups){if(!int.TryParse(group.Key,out var id))continue;var h=state.Histories.SingleOrDefault(h=>h.DeviceId==id);var name=group.First().Source.Replace("Конфигурация MikroTik · ","",StringComparison.Ordinal);
   if(h?.Audit.Result!=null){Apply(state,h,h.Audit.Result,name);continue;}
   var items=group.ToArray();foreach(var n in items){n.HandledAt=DateTime.UtcNow;n.Result="Объединено в сводку проверки";}
   state.Notifications.Add(new(){EntityKey="router-audit:"+id+":summary:"+Guid.NewGuid().ToString("N"),Topic="router.audit",Source=group.First().Source,Severity=items.Max(n=>n.Severity),Title="Сводка ранее выполненной проверки: "+items.Length+" результатов. Подробности: Коммутаторы → Анализ конфигурации. Выполните свежую проверку."});
  }
 }
 public static void Ignore(NetworkStore store,RouterAuditIgnore request,string actor){store.State(s=>{var h=s.Histories.Single(h=>h.DeviceId==request.DeviceId);if(RemoteData.Hash(h.Audit)!=request.ExpectedHash)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Анализ изменился. Обновите окно."));var finding=h.Audit.Result?.DisplayFindings.Concat(h.Audit.Result.Findings).FirstOrDefault(f=>f.Key==request.Key&&f.Fingerprint==request.Fingerprint)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Находка изменилась или исчезла"));var memberKeys=finding.Members.Select(m=>m.Key).Append(request.Key).ToHashSet();h.Audit.Ignored.RemoveAll(i=>memberKeys.Contains(i.Key));if(request.Ignore){if(h.Audit.Ignored.Count>=2000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Лимит исключений 2000. Удалите ненужные исключения."));h.Audit.Ignored.Add(new(){Key=request.Key,Fingerprint=request.Fingerprint,Actor=actor,TimeUtc=DateTime.UtcNow});}finding.Ignored=request.Ignore;finding.IgnoredBy=request.Ignore?actor:"";finding.IgnoredUtc=request.Ignore?DateTime.UtcNow:null;Apply(s,h,h.Audit.Result!,"устройство #"+h.DeviceId);return true;},true);}
 public static void Policy(NetworkStore store,RouterAuditPolicy request){if(request.WanInterfaces==null||request.WanInterfaces.Length>16||request.WanInterfaces.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>100||n.Any(char.IsControl)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Выберите до 16 внешних интерфейсов"));store.State(s=>{var h=s.Histories.SingleOrDefault(h=>h.DeviceId==request.DeviceId);if(h==null){h=new(){DeviceId=request.DeviceId};s.Histories.Add(h);}if(RemoteData.Hash(h.Audit)!=request.ExpectedHash)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Анализ изменился. Обновите окно."));h.Audit.ManualWan=request.WanInterfaces.Distinct().ToArray();h.Audit.Result=null;return true;},true);}
 public static LampState Color(LampState original,RouterAuditState state,bool enabled){if(!enabled)return original;if(state.Result==null)return original;if(state.Result.Critical>0)return LampState.Red;if(state.Result.Warnings>0||DateTime.UtcNow-state.Result.CheckedUtc>TimeSpan.FromMinutes(30))return original==LampState.Red?original:LampState.Amber;return original;}
}
