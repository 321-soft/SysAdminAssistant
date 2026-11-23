using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision130Specs {
 public static async Task Run(string folder,Action<string,bool> check){
  var policy=new DailySummarySettings{Enabled=true};var now=new DateTime(2026,10,19,18,0,0,DateTimeKind.Utc);
  check("Summary Monday schedule becomes due at configured minute",DailySummarySettings.Due(policy,now,"")&&!DailySummarySettings.Due(policy,now.AddMinutes(-1),""));
  policy.Days[1].Time="20:30";check("Summary weekday has independent time",!DailySummarySettings.Due(policy,now.AddDays(1),"")&&DailySummarySettings.Due(policy,now.AddDays(1).AddHours(3),""));
  policy.Days[6].Enabled=false;check("Summary disabled Sunday never sends",!DailySummarySettings.Due(policy,now.AddDays(6),""));
  check("Summary already delivered day is not repeated",!DailySummarySettings.Due(policy,now,"2026-10-19"));
  bool invalid=false;try{DailySummarySettings.Validate(new(){Days=[]});}catch(ArgumentException){invalid=true;}check("Summary rejects malformed seven day schedule",invalid);
  var store=new DailySummaryStore(Path.Combine(folder,"daily-summary"));SummaryProblem P(string key,string category="agents")=>new(key,category,Severity.Critical,"fixture",key);
  store.Observe([P("existing")],now.AddHours(-1));check("Summary first snapshot is baseline rather than all new problems",store.Preview(policy,"UTC",now).Contains("Новых проблем: 0"));
  store.Observe([P("existing"),P("new")],now.AddMinutes(-30));store.Observe([P("new")],now.AddMinutes(-15));var preview=store.Preview(policy,"UTC",now);check("Summary counts observed new and resolved transitions",preview.Contains("Новых проблем: 1")&&preview.Contains("Решено / снято с контроля: 1")&&preview.Contains("Активных: 1"));
  var selected=new DailySummarySettings{Categories=["payments"]};check("Summary filters counters and active list by selected sections",store.Preview(selected,"UTC",now).Contains("Активных: 0")&&store.Preview(selected,"UTC",now).Contains("Новых проблем: 0"));
  int sends=0;string message="";var telegram=new TelegramSettings{Enabled=true};Task Send(string text,CancellationToken token){sends++;message=text;return Task.CompletedTask;}
  await store.SendDueAsync(policy,telegram,"UTC",now,default,Send);await new DailySummaryStore(Path.Combine(folder,"daily-summary")).SendDueAsync(policy,telegram,"UTC",now.AddMinutes(1),default,Send);check("Summary successful send cursor survives store restart",sends==1&&message.Contains("new"));
  var reset=store.Preview(policy,"UTC",now.AddMinutes(2));check("Summary successful send advances reporting interval",reset.Contains("Новых проблем: 0")&&reset.Contains("Решено / снято с контроля: 0"));
  var delayed=new DailySummaryStore(Path.Combine(folder,"daily-summary-retry"));delayed.Observe([P("baseline")],now.AddHours(-1));string failed="";try{await delayed.SendDueAsync(policy,telegram,"UTC",now,default,(text,_)=>{failed=text;throw new IOException("fixture");});}catch(IOException){}
  delayed.Observe([P("baseline"),P("after-failure")],now.AddMinutes(1));string retried="";await delayed.SendDueAsync(policy,telegram,"UTC",now.AddMinutes(2),default,(text,_)=>{retried=text;return Task.CompletedTask;});check("Summary retry retains original snapshot",failed==retried&&delayed.Status().StartsWith("Отправлена"));check("Summary retry preserves transitions after pending cutoff",delayed.Preview(policy,"UTC",now.AddMinutes(3)).Contains("Новых проблем: 1"));
  var many=new DailySummaryStore(Path.Combine(folder,"daily-summary-many"));many.Observe(Enumerable.Range(0,100).Select(i=>P("Problem-"+i+new string('x',120))),now.AddHours(-1));await many.SendDueAsync(policy,telegram,"UTC",now,default,(text,_)=>{message=text;return Task.CompletedTask;});check("Large summary remains one bounded Telegram message",message.Length<3500&&message.Contains("Ещё ")&&many.Preview(policy,"UTC",now).Contains("Problem-99"));
  var concurrent=new DailySummaryStore(Path.Combine(folder,"daily-summary-concurrent"));concurrent.Observe([],now.AddHours(-1));int parallelSends=0;Task Slow(string _,CancellationToken token){Interlocked.Increment(ref parallelSends);return Task.Delay(60,token);}await Task.WhenAll(concurrent.SendDueAsync(policy,telegram,"UTC",now,default,Slow),concurrent.SendDueAsync(policy,telegram,"UTC",now,default,Slow));check("Concurrent daily summary workers do not duplicate day",parallelSends==1);
  var zoned=new DailySummarySettings{Enabled=true};zoned.Days[0].Time="21:00";check("Summary schedule uses selected server timezone",DailySummarySettings.Due(zoned,AppTime.InZone(now,"Russian Standard Time"),""));
 }
}
