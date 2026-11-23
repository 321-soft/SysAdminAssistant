using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision128Specs
{
 static RouterAuditResult Many(int count)=>new(){Findings=Enumerable.Range(1,count).Select(i=>new RouterAuditFinding{Key="forward:32:uplink:"+i,Fingerprint="fp"+i,Title="Port "+i,Status="Разрешено firewall: любые источники",Severity=i==1?Severity.Critical:Severity.Warning}).ToList()};
 public static async Task Run(string folder,Action<string,bool> check){
  var s=new NetworkState();var h=new NetworkHistory{DeviceId=1};s.Histories.Add(h);RouterAuditStore.Apply(s,h,Many(86),"fixture");
  check("86 port findings queue one MikroTik summary",s.Notifications.Count==1&&s.Notifications[0].Title.Contains("85")&&s.Notifications[0].Severity==Severity.Critical);
  RouterAuditStore.Apply(s,h,Many(86),"fixture");check("Identical audit does not duplicate summary",s.Notifications.Count==1);
  s.Notifications[0].HandledAt=DateTime.UtcNow;s.Notifications[0].Result="Отправлено";
  RouterAuditStore.Apply(s,h,Many(86),"fixture");check("Delivered summary is not repeated for same findings",s.Notifications.Count==1);
  RouterAuditStore.Apply(s,h,Many(87),"fixture");RouterAuditStore.Apply(s,h,Many(88),"fixture");check("New findings coalesce into a single pending summary",s.Notifications.Count(n=>n.HandledAt==null)==1&&s.Notifications.Count==2);
  s.Notifications.Last().HandledAt=DateTime.UtcNow;s.Notifications.Last().Result="Отправлено";
  RouterAuditStore.Apply(s,h,new(),"fixture");RouterAuditStore.Apply(s,h,new(),"fixture");check("All resolved audit sends one green summary",s.Notifications.Count(n=>n.HandledAt==null)==1&&s.Notifications.Last().Transition.StartsWith("ЗАКРЫТО"));
  var legacy=new NetworkState();legacy.Histories.Add(new(){DeviceId=2,Audit=new(){Result=Many(86)}});
  foreach(var f in Many(86).Findings)legacy.Notifications.Add(new(){EntityKey="router-audit:2:"+f.Fingerprint,Source="Конфигурация MikroTik · fixture",Title=f.Title,Severity=f.Severity});
  RouterAuditStore.CoalescePending(legacy);RouterAuditStore.CoalescePending(legacy);check("86 legacy queued port messages collapse before delivery",legacy.Notifications.Count(n=>n.HandledAt==null)==1&&legacy.Notifications.Single(n=>n.HandledAt==null).EntityKey.Contains(":summary:"));
  var store=new NetworkStore(Path.Combine(folder,"audit-summary-delivery"));store.State(state=>{state.Histories=legacy.Histories;state.Notifications=legacy.Notifications;state.ApplicationStates=legacy.ApplicationStates;return true;},true);
  int sends=0;string message="";await NetworkNotifications.FlushAsync(store,new(){Enabled=true,Critical=true,Warning=true,Info=true},default,(text,_)=>{sends++;message=text;return Task.CompletedTask;});
  check("Telegram delivery sends one message for 86 port findings",sends==1&&message.Contains("Сводка")&&message.Length<2000);
  var maintenance=new NetworkState();var mh=new NetworkHistory{DeviceId=3};RouterAuditStore.Apply(maintenance,mh,Many(86),"fixture",false);check("Maintenance suppresses grouped audit too",maintenance.Notifications.Count==0);RouterAuditStore.Apply(maintenance,mh,Many(86),"fixture");check("Leaving maintenance creates one summary",maintenance.Notifications.Count==1);
 }
}
