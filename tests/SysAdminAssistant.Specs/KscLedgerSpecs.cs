using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
public static class KscLedgerSpecs{
 public static async Task Run(Action<string,bool> check){
  var attackGeneration=Guid.NewGuid();
  var attacks=new KscLedger{Generation=attackGeneration,Rules=KscRules.Standard().ToList()};
  var notifications=new List<NetworkNotification>();
  var attackAt=DateTime.UtcNow;
  KscEvent Event(long id,string host,string text)=>KscNetworkAttack.Enrich(new(id,host,id<=2?"GNRL_EV_VIRUS_FOUND":"network.block",4),"",text,"");
  var attack=Event(1,"server-a","RDP brute force detected from 192.0.2.10");
  var file=Event(2,"server-a","Trojan detected in C:/temp/test.exe") with{EventTitle="Файл помещён на проверку",Threat="Test.Trojan"};
  var wrongHost=Event(3,"server-b","IP address 192.0.2.10 blocked");
  var wrongIp=Event(4,"server-a","IP address 192.0.2.11 blocked");
  var denied=Event(5,"server-a","IP address 192.0.2.10 not blocked");
  attacks.ApplyStandalone(attackGeneration,new(5,false,5,[attack,file,wrongHost,wrongIp,denied]),notifications,attackAt);
  check("KSC distinguishes RDP attempts and refuses unrelated or failed IP blocks",
      attacks.Open.Any(x=>x.Title=="Подбор пароля RDP"&&x.AttackerIp=="192.0.2.10")&&attacks.Resolved.Count==0);
  check("KSC retains the original label and threat in the incident summary",attacks.Open.Any(x=>x.LatestEventSummary=="Файл помещён на проверку · Test.Trojan"));
  var blocked=KscNetworkAttack.Enrich(new(6,"server-a","network.block",4),"","IP address 192.0.2.10 blocked","");
  attacks.ApplyStandalone(attackGeneration,new(6,false,1,[blocked]),notifications,attackAt);
  check("KSC closes the matching RDP attempt without closing the file incident",
      attacks.Resolved is [{AttackerIp:"192.0.2.10"}]&&attacks.Open.Any(x=>x.AttackerIp==null&&x.EventCode=="GNRL_EV_VIRUS_FOUND"));
  var resolvedFile=attacks.Open.Single(x=>x.EventCode=="GNRL_EV_VIRUS_FOUND");
  attacks.Close(resolvedFile,attackAt,"Отмечено решённым вручную");
  attacks.ApplyStandalone(attackGeneration,new(7,false,1,[file with{Id=7}]),notifications,attackAt.AddMinutes(1));
  check("Manual KSC resolution closes only the current occurrence; a new detection reopens it",attacks.Open.Any(x=>x.LastEventId==7&&x.Key!=resolvedFile.Key)&&attacks.Resolved.Any(x=>x.Key==resolvedFile.Key));
  attacks.ApplyStandalone(attackGeneration,new(7,false,1,[file with{Id=7}]),notifications,attackAt.AddMinutes(2));
  check("Replayed detection after manual resolution does not duplicate the new incident",attacks.Open.Single(x=>x.LastEventId==7).Count==1);
  attacks.ApplyStandalone(attackGeneration,new(8,false,1,[attack with{Id=8}]),notifications,attackAt.AddMinutes(3));
  check("A later RDP attempt is visible again after an earlier IP block",attacks.Open.Any(x=>x.LastEventId==8&&x.Title=="Подбор пароля RDP"));
  var portable=KscRules.Parse(KscRules.Export(KscRules.Standard()));
  check("Built-in Kaspersky rules use common codes and do not auto-close unrelated objects",
      portable.Select(r=>r.OpenEvent).SequenceEqual(new[]{"GNRL_EV_SUSPICIOUS_OBJECT_FOUND","GNRL_EV_VIRUS_FOUND","GNRL_EV_OBJECT_NOTCURED"})&&portable.All(r=>r.RecoveryEvent==""));
  check("Monitoring import routes legacy KSC and network formats to their own catalog",
      MonitoringDictionaryFormat.IsKaspersky(KscRules.Export(portable))&&!MonitoringDictionaryFormat.IsKaspersky("{\"Format\":1,\"Dictionaries\":[]}"));
  bool unknown=false;
  try{_=MonitoringDictionaryFormat.IsKaspersky("{\"Product\":\"unexpected\"}");}catch(ArgumentException){unknown=true;}
  check("Monitoring import refuses unknown products instead of treating them as network dictionaries",unknown);
  var gen=Guid.NewGuid();var s=new KscLedger{Generation=gen,Hosts={["stable-id"]=7},Rules=[new("test","Protection stopped","down","up",Severity.Critical,true)]};
  var exported=KscRules.Export(s.Rules);
  check("KSC dictionary export preserves all rule fields",KscRules.Parse(exported).SequenceEqual(s.Rules));
  var conflicting=new KscRule("other","Conflict","down","",Severity.Warning,false);
  bool rejected=false;try{KscRules.Merge(s.Rules,[conflicting]);}catch(ArgumentException){rejected=true;}
  check("KSC preview merge rejects existing event conflict without mutation",rejected&&s.Rules.Count==1&&s.Rules[0].Id=="test");
  var replacement=s.Rules[0] with{Title="Changed"};
  check("KSC merge replaces matching ID and preserves unrelated rules",KscRules.Merge(s.Rules,[replacement,new("new","New","new-down","",Severity.Warning,false)]).Length==2);
  foreach(var bad in new[]{
   new KscBatch(2,false,2,[new(1,"stable-id","down",4),new(1,"stable-id","down",4)]),
   new KscBatch(2,false,2,[new(2,"stable-id","down",4),new(1,"stable-id","down",4)]),
   new KscBatch(3,false,2,[new(1,"stable-id","down",4),new(2,"stable-id","down",4)])}){
   var pending=new List<NetworkNotification>();bool invalid=false;
   try{s.Apply(gen,bad,new Dictionary<int,string>{{7,"server"}},pending,DateTime.UtcNow);}catch(InvalidDataException){invalid=true;}
   check("KSC malformed page leaves cursor incidents and queue unchanged",invalid&&s.Position==0&&s.Open.Count==0&&pending.Count==0);
  }
  var q=new List<NetworkNotification>();var devices=new Dictionary<int,string>{{7,"server"}};var now=DateTime.UtcNow;
  for(int first=1;first<=100000;first+=100){var page=new KscBatch(first+99,false,100,Enumerable.Range(first,100).Select(i=>new KscEvent(i,"stable-id","down",4)).ToArray());s.Apply(gen,page,devices,q,now);s.Apply(gen,page,devices,q,now);}
  check("100000 KSC repeats and page replays create one linked incident and one notification",s.Open is [{Count:100000,DeviceId:7}]&&q.Count==1);
  s.Apply(gen,new(100001,false,1,[new(100001,"another-host","up",1)]),devices,q,now);
  check("Unlinked KSC recovery cannot clear linked device",s.Open.Count==1&&q.Count==1);
  s.Apply(gen,new(100002,false,1,[new(100002,"stable-id","up",1)]),devices,q,now);
  check("Explicit same-host recovery queues once with original key and device name",s.Open.Count==0&&q.Count==2&&q[0].EntityKey==q[1].EntityKey&&q.All(n=>n.Source.Contains("server")));

  var policy=new KscLedger{Generation=gen,Hosts={["stable-id"]=7},Rules=[s.Rules[0]]};
  var policyQueue=new List<NetworkNotification>();
  policy.Apply(gen,new(1,false,1,[new(1,"stable-id","down",4)]),devices,policyQueue,now);
  var incident=policy.Open.Single();
  policy.Rules[0]=policy.Rules[0] with{Enabled=false,Notify=false};
  policy.Apply(gen,new(2,false,1,[new(2,"stable-id","down",4)]),devices,policyQueue,now);
  check("Disabled KSC rule retains existing incident without counting new triggers",policy.Open.Single()==incident&&policyQueue.Count==1&&policy.Position==2);
  policy.Apply(gen,new(3,false,1,[new(3,"stable-id","up",1)]),devices,policyQueue,now);
  check("Disabled KSC rule still recovers existing incident and original notification",policy.Open.Count==0&&policyQueue.Count==2&&policyQueue[0].EntityKey==policyQueue[1].EntityKey);
  policy.Apply(gen,new(4,false,1,[new(4,"stable-id","down",4)]),devices,policyQueue,now);
  check("Disabled KSC rule opens no new incident",policy.Open.Count==0&&policyQueue.Count==2);
  var disabled=KscRules.Parse(KscRules.Export(policy.Rules)).Single();
  check("KSC export preserves disabled and notification options",!disabled.Enabled&&!disabled.Notify);
  // Construct a legacy document without the new member, independent of indentation.
  using(var doc=System.Text.Json.JsonDocument.Parse(KscRules.Export(s.Rules))){
   var legacy=System.Text.Json.JsonSerializer.Serialize(new{Format=1,Product="KSC",Rules=s.Rules.Select(r=>new{r.Id,r.Title,r.OpenEvent,r.RecoveryEvent,r.Level,r.Notify})});
   check("Legacy KSC dictionaries remain enabled by default",KscRules.Parse(legacy).All(r=>r.Enabled));
  }
  policy.Rules[0]=disabled with{Enabled=true};
  policy.Apply(gen,new(5,false,1,[new(5,"stable-id","down",4)]),devices,policyQueue,now);
  check("Reenabled KSC rule creates a new silent incident without replay",policy.Open is [{Count:1,Notify:false}]&&policyQueue.Count==2);
  var standalone=new KscLedger{Generation=gen,Rules=[s.Rules[0]]};
  var standaloneQueue=new List<NetworkNotification>();
  standalone.ApplyStandalone(gen,new(2,false,2,[new(1,"workstation-a","down",4),new(2,"workstation-b","down",4)]),standaloneQueue,now);
  check("KSC processes unregistered hosts without creating Assistant devices",
   standalone.Open.Count==2&&standalone.Open.All(p=>p.DeviceId==0)&&standaloneQueue.All(n=>n.DeviceId==null));
  standalone.ApplyStandalone(gen,new(3,false,1,[new(3,"workstation-a","up",1)]),standaloneQueue,now);
  check("Standalone KSC closes the matching host only",standalone.Open is [{Host:"workstation-b"}]&&standaloneQueue[2].EntityKey==standaloneQueue[0].EntityKey);
  check("KSC Telegram is opt-in and independently selects severity and recovery",
   !new KscTelegramOptions().Allows(Severity.Critical,false)&&new KscTelegramOptions(true).Allows(Severity.Warning,true)&&
   !new KscTelegramOptions(true,Warning:false).Allows(Severity.Warning,false)&&!new KscTelegramOptions(true,Recovery:false).Allows(Severity.Critical,true));
  var dir=Path.Combine(Path.GetTempPath(),"SaaKscLedger-"+Guid.NewGuid().ToString("N"));
  try{
   var store=new NetworkStore(dir);
   store.State(net=>{net.Ksc=new(){Generation=gen,Hosts={["stable-id"]=7},Rules=s.Rules.ToList()};return true;},true);
   var opening=new KscBatch(1,false,1,[new(1,"stable-id","down",4)]);
   store.State(net=>{net.Ksc.Apply(gen,opening,devices,net.Notifications,now);return true;},true);
   var restarted=new NetworkStore(dir);
   restarted.State(net=>{net.Ksc.Apply(gen,opening,devices,net.Notifications,now);return true;},true);
   check("KSC restart replay preserves one incident and queued opening",restarted.State(net=>net.Ksc.Open is [{Count:1}]&&net.Notifications.Count==1&&net.Ksc.Position==1));
   var recovery=new KscBatch(2,false,1,[new(2,"stable-id","up",1)]);
   restarted.State(net=>{net.Ksc.Apply(gen,recovery,devices,net.Notifications,now);return true;},true);
   new NetworkStore(dir).State(net=>{net.Ksc.Apply(gen,recovery,devices,net.Notifications,now);return true;},true);
   check("KSC recovery replay after restart queues exactly one matching closure",restarted.State(net=>net.Ksc.Open.Count==0&&net.Notifications.Count==2&&net.Notifications[0].EntityKey==net.Notifications[1].EntityKey));
   try{restarted.State<bool>(net=>{net.Ksc.Position=999;net.Notifications.Clear();throw new IOException("simulated failure before commit");},true);}catch(IOException){}
   check("KSC failed store transaction retains committed cursor and outbox",new NetworkStore(dir).State(net=>net.Ksc.Position==2&&net.Notifications.Count==2));
   var standaloneStore=new NetworkStore(Path.Combine(dir,"standalone-telegram"));
   standaloneStore.State(net=>{net.Notifications.AddRange(standaloneQueue);return true;},true);
   var sends=0;
   var telegram=new TelegramSettings{Enabled=true,Critical=false,Topics=new(){{"ksc.security",false},{"recovery",false}}};
   var options=new KscTelegramOptions(true,Recovery:false);
   await NetworkNotifications.FlushAsync(standaloneStore,telegram,default,(_,_)=>{sends++;return Task.CompletedTask;},
    kscFilter:n=>options.Allows(n.Severity,n.Transition.StartsWith("ЗАКРЫТО",StringComparison.Ordinal)));
   check("KSC Telegram settings override general severity and topic filters, not the master switch",
    sends==2&&standaloneStore.State(net=>net.Notifications[2].Result!="Отправлено"));
  }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}

 }
}
