using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
static class SocketFloppySpecs
{
 public static void Run(Action<string,bool> check){
 var now=DateTime.UtcNow;var policy=new AgentPolicy{StandardServices=false,BootGraceMinutes=0,ExcludedServices=["sysadminassistant-agent-update.service","sysadminassistant-agent-update.timer"],Services=[new("ssh.service")]};
 var ssh=new LinuxUnit("ssh.service","notify","inactive","dead","success",0,0,null,null,"ssh.socket");var socket=new LinuxUnit("ssh.socket","socket","active","listening","success",0,0,null,null,Dependency:true);
 var r=new AgentReport{Os="Linux Ubuntu",CapturedUtc=now,UptimeSeconds=3600,Linux=new(){Units=[ssh,socket]}};
 check("SSH socket activation is healthy",LinuxPolicy.Systemd(r,policy).Length==0);
 check("Fresh listening socket recovers inactive SSH incident",LinuxPolicy.CanRecover("service:ssh.service",r,policy,now.AddMinutes(-5)));
 foreach(var bad in new[]{socket with{Active="inactive",Sub="dead"},socket with{Error="no data"},socket with{Sub="running"},socket with{Name="other.socket"}}){r.Linux.Units=[ssh,bad];check("SSH requires declared listening healthy socket "+bad.Name+bad.Sub+bad.Error,LinuxPolicy.Systemd(r,policy).Any(f=>f.Key=="service:ssh.service")&&!LinuxPolicy.CanRecover("service:ssh.service",r,policy,now.AddMinutes(-1)));}
 r.Linux.Units=[ssh];check("Old telemetry without socket is not silently healthy",LinuxPolicy.Systemd(r,policy).Length==1);
 r.Linux.Units=[ssh with{Active="failed",Sub="failed",Result="exit-code",ExitStatus=1},socket];check("Listening socket cannot hide service failure",LinuxPolicy.Systemd(r,policy).Length==1&&!LinuxPolicy.CanRecover("service:ssh.service",r,policy,now.AddMinutes(-1)));
 r.Linux.Units=[ssh with{Trigger="custom.socket ssh.socket"},socket];check("Multiple declared sockets permit healthy matching listener",LinuxPolicy.Systemd(r,policy).Length==0);
 r.Linux.Units=[ssh with{Trigger=""},socket];check("Unrelated listener never substitutes missing relation",LinuxPolicy.Systemd(r,policy).Length==1);
 r.Linux.Units=[ssh,socket];check("Stale snapshot cannot recover newer incident",!LinuxPolicy.CanRecover("service:ssh.service",r,policy,now.AddSeconds(1)));
 var dir=Path.Combine(Path.GetTempPath(),"saa-socket-floppy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
 AgentJournalEvent Event(string key,string dev)=>new(key,"Linux","journald",1002,0,now,"I/O error, dev "+dev+", sector 0 op 0x0:(READ) flags 0x0 phys_seg 1 prio class 2");
 AgentEventBatch Batch(params AgentJournalEvent[] rows)=>new(Guid.NewGuid().ToString("N"),rows,[new("Linux","OK","")]);
 try{
 var fd=Event("fd","fd0");check("Floppy fd0 I/O classified as ignored",AgentEvents.IgnoredFloppy(fd));check("Floppy fd1 ignored",AgentEvents.IgnoredFloppy(Event("fd1","fd1")));
 foreach(var dev in new[]{"sda","vda","nvme0n1","dm-0","fd0extra"})check("Real disk or partial name remains monitored "+dev,!AgentEvents.IgnoredFloppy(Event(dev,dev)));
 check("Windows messages are unaffected",!AgentEvents.IgnoredFloppy(fd with{Channel="System"}));
 var store=new AgentEventStore(dir,true);store.Ingest(1,Batch(fd),null,false,now);var state=store.View(1);check("Floppy raw record preserved without problem or Telegram",state.Events.Count==1&&state.Problems.Count==0&&state.Pending.Count==0);

 store.Ingest(1,Batch(Event("sda","sda")),null,false,now);check("Real disk still opens critical incident",store.View(1).Problems.Any(p=>p.Open&&p.Severity==Severity.Critical));
 // Simulate pre-upgrade persisted floppy-only and mixed incidents.
 var old=new AgentEventState{Policy=new(){Rules=AgentEvents.StandardLinux()},ArchiveSeeded=true,Events=[fd],Problems=[new(){Id="old",RuleId="linux-storage",Source="",FirstUtc=now,LastEventUtc=now,LastUtc=now,Count=1,Title="old floppy"}]};
 var statePath=Path.Combine(dir,"agent-linux-events-2.json");File.WriteAllText(statePath,JsonSerializer.Serialize(old,AgentWire.Json));store.Ingest(2,Batch(),null,false,now);check("Old floppy-only incident closes and preserves history",!store.View(2).Problems.Single().Open&&store.View(2).Events.Count==1);
 old.Events.Add(Event("real","sda"));old.Problems[0].Count=2;File.WriteAllText(Path.Combine(dir,"agent-linux-events-3.json"),JsonSerializer.Serialize(old,AgentWire.Json));store.Ingest(3,Batch(),null,false,now);check("Mixed old floppy and real disk problem never auto-closes",store.View(3).Problems.Single().Open);
 }finally{Directory.Delete(dir,true);}
 }
}
