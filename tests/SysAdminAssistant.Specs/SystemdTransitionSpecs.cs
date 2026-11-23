using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
static class SystemdTransitionSpecs
{
 public static void Run(Action<string,bool> check){
 var at=DateTime.UtcNow;var policy=new AgentPolicy{StandardServices=false,ExcludedServices=["sysadminassistant-agent-update.timer","sysadminassistant-agent-update.service"],Services=[new("update.timer",MaxIntervalMinutes:15)]};
 var job=new LinuxUnit("update.service","oneshot","failed","failed","exit-code",1,1,at,null);var timer=new LinuxUnit("update.timer","timer","active","waiting","",0,0,at,at.AddMinutes(5),"update.service");
 var report=new AgentReport{Os="Linux Ubuntu",MachineName="fixture",CpuPercent=1,MemoryTotalBytes=100,MemoryAvailableBytes=90,CapturedUtc=at,UptimeSeconds=3600,Linux=new(){Units=[timer,job]}};
 var path=Path.Combine(Path.GetTempPath(),"saa-systemd-transition-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
 try{
 var network=new NetworkState();const string key="systemd-job:update.service";int sequence=0;
 AgentFinding[] Poll(LinuxUnit u){report.CapturedUtc=at.AddSeconds(sequence++*30);report.Linux!.Units=[timer,u];var observed=LinuxPolicy.Systemd(report,policy);var effective=AgentMetricHistory.EffectiveFindings(path,1,report,policy,observed);AgentMetricHistory.Capture(path,1,report,policy,observed);var active=effective.Select(f=>f.Key).ToHashSet();foreach(var old in network.AvailabilityAlerts.Keys.Where(k=>!active.Contains(k)&&LinuxPolicy.CanRecover(k,report,policy,network.AvailabilityAlerts[k].FailedSinceUtc)).ToArray())AvailabilityAlerts.Observe(network,old,false,report.CapturedUtc,0,30,Severity.Warning,"fixture","","recovered");foreach(var f in observed)AvailabilityAlerts.Observe(network,f.Key,true,report.CapturedUtc,0,30,f.Severity,"fixture",f.Title,"recovered",AgentIssueSignature.For(f,report));return effective;}
 Poll(job);network.Notifications[0].Result="Отправлено";var transient=job with{Active="activating",Sub="start",Result="success",ExitCode=0,ExitStatus=0};
 check("Oneshot entering activating cannot recover an incident",!LinuxPolicy.CanRecover(key,withUnit(transient),policy,at));
 check("Transition retains visible original problem",Poll(transient).Any(f=>f.Key==key));
 Poll(job);Poll(transient);Poll(job);check("Repeated failure start failure cycles send one alert and no false recovery",network.Notifications.Count==1&&network.AvailabilityAlerts.ContainsKey(key));
 check("History has no recovery during in-progress attempts",!AgentMetricHistory.Read(path,1).Rows.Any(r=>r.Transition=="Recovered"));
 var between=job with{Active="inactive",Sub="dead",Result="success",ExitStatus=0,LastRunUtc=at.AddSeconds(60)};check("Success older than latest confirmed failure cannot close repeated incident",Poll(between).Any(f=>f.Key==key));
 var oldSuccess=job with{Active="inactive",Sub="dead",Result="success",ExitStatus=0,LastRunUtc=at.AddMinutes(-10)};check("Old successful exit cannot close a later incident",Poll(oldSuccess).Any(f=>f.Key==key));
 report.Linux!.Units=[timer];check("Missing linked unit is not proof of recovery",!LinuxPolicy.CanRecover(key,report,policy,at));report.Errors=["Не удалось получить службы systemd"];check("Collector failure is not proof of recovery",!LinuxPolicy.CanRecover(key,report,policy,at));report.Errors=[];var missing=new AgentReport{Os="Linux Ubuntu",CapturedUtc=report.CapturedUtc};check("Missing entire Linux telemetry cannot close timer dependency failure",!LinuxPolicy.CanRecover(key,missing,policy,at));
 var success=job with{Active="inactive",Sub="dead",Result="success",ExitStatus=0,LastRunUtc=at.AddSeconds(sequence*30)};check("Fresh successful completion closes retained incident",!Poll(success).Any(f=>f.Key==key)&&network.Notifications.Count==2&&!network.AvailabilityAlerts.ContainsKey(key));Poll(success);check("Successful follow-up does not send repeated recovery",network.Notifications.Count==2);
 check("History contains exactly one confirmed recovery",AgentMetricHistory.Read(path,1).Rows.Count(r=>r.Transition=="Recovered")==1);
 var activeReport=withUnit(transient);check("Schedule problem cannot close while linked job starts",!LinuxPolicy.CanRecover("systemd-schedule:update.timer",activeReport,policy,at));
 var eventStore=new AgentEventStore(path,true);var ev=new AgentJournalEvent("failure","Linux","journald",1004,0,at,"update.service: Failed with result 'exit-code'.","update.service",3);eventStore.Ingest(2,new("batch",[ev],[new("Linux","OK","ok")]),null,false,at);activeReport.CapturedUtc=DateTime.UtcNow;eventStore.RecoverLinux(2,activeReport,policy,DateTime.UtcNow);check("Journal incident does not close on activating with old successful result",eventStore.View(2).Problems.Any(p=>p.Open));
 AgentReport withUnit(LinuxUnit u)=>new(){Os="Linux Ubuntu",CapturedUtc=report.CapturedUtc,Linux=new(){Units=[timer,u]}};
 }finally{Directory.Delete(path,true);}
 }
}
