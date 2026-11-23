using SysAdminAssistant.Core;using SysAdminAssistant.Infrastructure;
void Check(string s,bool v){if(!v)throw new Exception(s);Console.WriteLine("PASS "+s);}
var dir=Path.Combine(Path.GetTempPath(),"saa-store-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
try{var store=new AgentEventStore(dir,true);var now=DateTime.UtcNow;
AgentJournalEvent Event(string key,int id,string source,DateTime time)=>new(key,"Linux","journald",id,0,time,"event",source,3);
AgentEventBatch Batch(params AgentJournalEvent[] events)=>new(Guid.NewGuid().ToString(),events,[new("Linux","OK","ok")]);
store.Ingest(1,Batch(Event("old",1002,"",now.AddHours(-2))),null,false,now);Check("old event retained without alert",store.View(1).Events.Count==1&&store.View(1).Problems.Count==0);
var entries=Enumerable.Range(0,5).Select(i=>Event("ssh"+i,1003,"1.2.3.4",now.AddSeconds(-i))).ToArray();store.Ingest(1,Batch(entries),null,false,now);Check("SSH threshold grouped",store.View(1).Problems.Count(p=>p.Open&&p.RuleId=="linux-login")==1);
store.Ingest(1,Batch(entries),null,false,now);Check("duplicate ignored",store.View(1).Problems.Single(p=>p.RuleId=="linux-login").Count==5);
store.Ingest(1,Batch(Event("svc",1004,"nginx.service",now)),null,false,now);var report=new AgentReport{CapturedUtc=now,Services=[new("nginx.service","nginx","Running")]};store.RecoverLinux(1,report,new AgentPolicy(),now);Check("running service closes problem",store.View(1).Problems.Single(p=>p.RuleId=="linux-service").Open==false);
Check("Windows data remains isolated",new AgentEventStore(dir).View(1).Events.Count==0);
}finally{Directory.Delete(dir,true);}
