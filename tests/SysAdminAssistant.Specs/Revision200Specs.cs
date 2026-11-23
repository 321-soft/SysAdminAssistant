using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class Revision200Specs
{
 static Dictionary<string,string> Row(string id,string time,string message="router rebooted")=>new(){{".id",id},{"time",time},{"topics","system,info"},{"message",message}};
 static RouterSnapshot Sample(params Dictionary<string,string>[] logs)=>new(new(),new(),new(),[],logs,Clock:new(){{"date","2026-10-23"},{"time","09:08:19"}});
 public static async Task Run(string folder,Action<string,bool> check){
  var now=new DateTime(2026,10,23,6,8,19,DateTimeKind.Utc);var dictionary=new NetworkDictionary{Rules=NetworkLogRules.Standard()};
  var h=new NetworkHistory{LogsInitialized=true};var old=Row("*1","2026-10-22 20:31:11");var fresh=RouterLogIntake.Collect(h,Sample(old),"reader",dictionary,now);
  check("Upgrade establishes old RouterOS buffer as baseline",fresh.Count==0&&h.Logs.Single().Baseline);
  old[".id"]="*ABC";fresh=RouterLogIntake.Collect(h,Sample(old),"reader",dictionary,now.AddSeconds(1));
  check("Router log reindex does not replay old reboot",fresh.Count==0&&h.Logs.Count==1);
  var current=Row("*2","2026-10-23 09:08:10");fresh=RouterLogIntake.Collect(h,Sample(old,current),"reader",dictionary,now);
  check("New timely router event is detected once",fresh.Count==1&&fresh[0].EventUtc==now.AddSeconds(-9));
  NetworkLogRules.Evaluate(h.Problems,h.Logs,fresh,dictionary.Rules,now);
  check("Problem starts at event time rather than download time",h.Problems.Single(p=>p.Open).OpenedAt==now.AddSeconds(-9));
  fresh=RouterLogIntake.Collect(h,Sample(old,current),"reader",dictionary,now.AddSeconds(1));check("Repeated successful poll never repeats reboot",fresh.Count==0);
  var late=Row("*3","2026-10-22 23:00:00","disk failure");fresh=RouterLogIntake.Collect(h,Sample(old,current,late),"reader",dictionary,now);check("Late old critical stays archived instead of new alert",fresh.Count==0&&h.Logs.Last().Baseline);
  var future=Row("*4","2026-10-23 12:00:00","disk failure");fresh=RouterLogIntake.Collect(h,Sample(old,current,late,future),"reader",dictionary,now);check("Future RouterOS timestamp cannot trigger present incident",fresh.Count==0&&h.Logs.Last().Baseline);
  var shortTime=Row("*5","09:08:11","fan failure");fresh=RouterLogIntake.Collect(h,Sample(old,current,shortTime),"reader",dictionary,now);check("Time-only record uses router date",fresh.Count==1&&fresh[0].EventUtc==now.AddSeconds(-8));
  shortTime["time"]="2026-10-23 09:08:11";fresh=RouterLogIntake.Collect(h,Sample(old,current,shortTime),"reader",dictionary,now);check("Timestamp format expansion does not replay existing record",fresh.Count==0);
  check("Router midnight infers previous day",RouterLogIntake.RouterLocal("23:59:59",new DateTime(2026,10,23,0,0,2))==new DateTime(2026,10,22,23,59,59));
  check("Router year boundary uses previous year",RouterLogIntake.RouterLocal("dec/31 23:59:59",new DateTime(2026,1,1,0,0,2))==new DateTime(2025,12,31,23,59,59));
  var unknown=new NetworkHistory{LogIntakeVersion=1,LogsInitialized=true};var noClock=new RouterSnapshot(new(),new(),new(),[],[Row("*1","unparsed")]);
  check("Unanchored untimed buffer does not produce false new incidents",RouterLogIntake.Collect(unknown,noClock,"r",dictionary,now).Count==0);
  noClock=noClock with{Logs=[Row("*AA","unparsed"),Row("*2","other-unknown","disk failure")]};check("Untimed suffix after retained cursor is admitted",RouterLogIntake.Collect(unknown,noClock,"r",dictionary,now).Count==1);
  var duplicates=new NetworkHistory{LogIntakeVersion=1,LogsInitialized=true};fresh=RouterLogIntake.Collect(duplicates,Sample(current,Row("*3",current["time"])),"r",dictionary,now);check("Two identical same-second events retain separate occurrences",fresh.Count==2&&fresh.Select(l=>l.Key).Distinct().Count()==2);
  var migrated=new NetworkHistory{LogsInitialized=true,Problems=[new(){Kind="Log",Open=true,RuleId="unexpected-reboot",OpenedAt=now,Evidence=[new(){Log=new(){RouterTime=old["time"],ReceivedAt=now}}]}]};RouterLogIntake.Collect(migrated,Sample(old),"r",dictionary,now);check("Proven late old-log alarm corrected with evidence retained",!migrated.Problems[0].Open&&migrated.Problems[0].Evidence.Count==1);
  var real=new NetworkHistory{LogsInitialized=true,Problems=[new(){Kind="Log",Open=true,Evidence=[new(){Log=new(){RouterTime=old["time"],ReceivedAt=now.AddHours(-13)}}]}]};RouterLogIntake.Collect(real,Sample(old),"r",dictionary,now);check("Real old unresolved incident is never silently closed",real.Problems[0].Open);
  var failures=new List<NetworkProblem>{new(){Open=true,RuleId="connection",Key="legacy-api",DownSince=now.AddDays(-1)},new(){Open=false,RuleId="connection",Key="connection:API-SSL",RestoredAt=now.AddHours(-1)},new(){Open=true,RuleId="connection",Key="connection:API-SSL",DownSince=now.AddDays(-1)}};
  var rule=new NetworkRule{Id="connection",Name="API",RecoveryChecks=2};NetworkSignals.Observe(failures,rule,"API-SSL",true,now);check("Legacy and duplicate API alarms wait for recovery confirmation",failures.Count(p=>p.Open)==2&&failures.Where(p=>p.Open).All(p=>p.HealthyChecks==1));
  NetworkSignals.Observe(failures,rule,"API-SSL",true,now.AddMinutes(1));check("Second successful poll closes every old API alarm",failures.All(p=>!p.Open));
  NetworkSignals.Observe(failures,rule,"API-SSL",false,now.AddMinutes(2));NetworkSignals.Observe(failures,rule,"API-SSL",false,now.AddMinutes(3));check("New real outage still opens an API alarm",failures.Count(p=>p.Open)==1);
  NetworkSignals.Observe(failures,rule,"API-SSL",true,now.AddMinutes(4));NetworkSignals.Observe(failures,rule,"API-SSL",false,now.AddMinutes(5));NetworkSignals.Observe(failures,rule,"API-SSL",true,now.AddMinutes(6));check("Intervening API failure resets recovery counter",failures.Single(p=>p.Open).HealthyChecks==1);
  using var wake=new SemaphoreSlim(0,1);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));var cycles=0;var errors=0;var loop=IndependentPollLoop.RunAsync(()=>{cycles++;if(cycles==1)throw new IOException("fixture");if(cycles==3)stop.Cancel();return Task.CompletedTask;},()=>TimeSpan.FromMilliseconds(5),wake,_=>errors++,stop.Token);await loop;
  check("Autonomous poll loop runs without client or external wake",cycles==3);check("Failed poll does not stop subsequent autonomous cycles",errors==1);
  var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"poll200.db")});await f.InitializeAsync();int id;await using(var db=f.Create()){var d=new Device{Name="Fixture",Type="MikroTik",Host="fixture"};db.Devices.Add(d);await db.SaveChangesAsync();id=d.Id;}
  var store=new NetworkStore(Path.Combine(folder,"poll200-state"));store.State(s=>{s.Connections.Add(new(){DeviceId=id,User="reader"});s.Histories.Add(new(){DeviceId=id,Host="fixture",Problems=[new(){Open=true,RuleId="connection",Key="old-api",DownSince=now.AddDays(-1)}]});return true;},true);
  await using var monitor=new NetworkMonitor(store);await monitor.CollectAsync(f,new(),read:(_,_)=>Task.FromResult(Sample()));await monitor.CollectAsync(f,new(),read:(_,_)=>Task.FromResult(Sample()));
  check("Actual monitor clears stale connection record and persists successful polls",store.State(s=>s.Histories.Single().Problems.All(p=>!p.Open)&&s.Histories.Single().Polls.Count==2&&s.Histories.Single().LastSuccessfulPollUtc!=null));
  await monitor.CollectAsync(f,new(),read:(_,_)=>throw new IOException("fixture failure"));check("Actual monitor records failed poll separately from successful time",store.State(s=>s.Histories.Single().Polls.Count==3&&!s.Histories.Single().Polls.Last().Success&&s.Histories.Single().Polls.Last().Error.Length>0));
 }
}
