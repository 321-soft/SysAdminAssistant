using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
public static class LinuxMonitoringSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var now=DateTime.UtcNow;var p=new AgentPolicy{StandardServices=false,ExcludedServices=["sysadminassistant-agent-update.service","sysadminassistant-agent-update.timer"]};
        var r=new AgentReport{AgentVersion="0.32.0.0",MachineName="fixture",Os="Linux Ubuntu",CpuCount=2,CpuPercent=2,MemoryTotalBytes=100,MemoryAvailableBytes=90,CapturedUtc=now,UptimeSeconds=3600,Linux=new()};
        LinuxVolume Volume(string mount,long size,long free,long? inodes=100000,long? availableInodes=80000)=>new(mount,"/dev/mapper/vg-data","ext4","LVM","253:0","/",size,size-free,free,0,inodes,availableInodes);
        r.Linux.Volumes=[Volume("/boot",2L*1073741824,(long)(2L*1073741824*0.89)),Volume("/boot/efi",256L*1048576,200L*1048576)];
        check("2 GiB boot with 89 percent free is healthy under default 10 GiB global floor",!AgentChecks.Evaluate(r,p).Any(f=>f.Key.StartsWith("disk:")||f.Key.StartsWith("disk-policy:")));
        r.Linux.Volumes=[Volume("/boot",2L*1073741824,30L*1048576)];check("Really low boot bytes open disk incident",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="disk:/boot"));
        r.Linux.Volumes=[Volume("/",100L*1073741824,80L*1073741824,100000,1)];var findings=AgentChecks.Evaluate(r,p);check("Inode exhaustion is separate from byte space",findings.Any(f=>f.Key=="inode:/")&&!findings.Any(f=>f.Key=="disk:/"));
        p.Linux=new(){Disks=[new("/",5,0,0,0)]};check("Custom inode threshold can be disabled without disabling byte monitoring",!AgentChecks.Evaluate(r,p).Any(f=>f.Key=="inode:/"));
        p.Linux.Disks=[new("/",95,0,0,0)];check("Individual mount override is applied",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="disk:/"));
        p.ExcludedVolumes=["/"];check("Existing excluded volumes remain excluded",!AgentChecks.Evaluate(r,p).Any(f=>f.Key=="disk:/"));p.ExcludedVolumes=[];r.Linux.Volumes=[];
        p.Services=[new("fixture.service",MaxIntervalMinutes:5)];LinuxUnit Job(string state,string result,int status,DateTime? last)=>new("fixture.service","oneshot",state,state=="failed"?"failed":"dead",result,1,status,last,null);
        r.Linux.Units=[Job("inactive","success",0,now.AddMinutes(-1))];check("Successful inactive oneshot is normal",!AgentChecks.Evaluate(r,p).Any(f=>f.Key.Contains("fixture")));
        r.Linux.Units=[Job("failed","exit-code",1,now.AddSeconds(-5))];check("Nonzero oneshot exit opens distinct job incident",AgentChecks.Evaluate(r,p).Any(f=>f.Key.StartsWith("systemd-job:")));
        r.Linux.Units=[Job("inactive","success",0,now.AddMinutes(-10))];check("Missing expected run is distinct schedule incident",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="systemd-schedule:fixture.service"));
        r.Linux.Units=[Job("inactive","success",0,now)];r.Linux.UpdateDelivery=new("Rejected","SignatureMismatch","Подпись отклонена",now,now,1,new string('A',64));check("Successful oneshot without package does not hide failed delivery",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="update-delivery:SignatureMismatch")&&!AgentChecks.Evaluate(r,p).Any(f=>f.Key.StartsWith("systemd-job:")));
        r.Linux.UpdateDelivery=r.Linux.UpdateDelivery! with{State="RolledBack",Code="InterruptedUpdate"};check("Rolled back interrupted package remains separate unresolved delivery incident",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="update-delivery:InterruptedUpdate"));
        p.Services=[new("fixture.timer",MaxIntervalMinutes:5)];var timer=new LinuxUnit("fixture.timer","timer","active","waiting","",0,0,now.AddMinutes(-1),now.AddMinutes(4),"fixture.service");r.Linux.Units=[timer,Job("inactive","success",0,now.AddMinutes(-1))];check("Active timer and successful linked oneshot are healthy",!AgentChecks.Evaluate(r,p).Any(f=>f.Key.Contains("fixture")));
        r.Linux.Units=[timer with{Active="inactive",Sub="dead"},Job("inactive","success",0,now)];check("Stopped timer is not called a failed job",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="service:fixture.timer")&&!AgentChecks.Evaluate(r,p).Any(f=>f.Key.StartsWith("systemd-job:")));
        r.Linux.Units=[timer,Job("failed","exit-code",2,now)];check("Timer observes linked job failure separately",AgentChecks.Evaluate(r,p).Any(f=>f.Key=="systemd-job:fixture.service"));
        p.Services.Add(new("fixture.service"));check("Job selected directly and through timer creates one incident",AgentChecks.Evaluate(r,p).Count(f=>f.Key=="systemd-job:fixture.service")==1);p.Services.RemoveAt(p.Services.Count-1);
        var legacy=LinuxPolicy.ForAgent(p,"0.31.2.0");var wire=JsonSerializer.Serialize(legacy,AgentWire.Json);check("Old Linux agents receive no unsupported policy members during auto upgrade",legacy.Linux==null&&legacy.Services.All(s=>s.MaxIntervalMinutes==null)&&!wire.Contains("MaxIntervalMinutes")&&!wire.Contains("\"Linux\""));check("New Linux agents receive complete policy",LinuxPolicy.ForAgent(p,"0.32.0.0").Services[0].MaxIntervalMinutes==5);
        var folder=Path.Combine(Path.GetTempPath(),"saa-history-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);try{
            var f=new AgentFinding("update-delivery:SignatureMismatch","Signature failed",Severity.Warning,0);r.Linux.UpdateDelivery=new("Rejected","SignatureMismatch","Rejected",now,now,1,new string('A',64));
            for(int i=0;i<3;i++){r.CapturedUtc=now.AddSeconds(i*30);AgentMetricHistory.Capture(folder,1,r,p,[f]);}
            var history=AgentMetricHistory.Read(folder,1).Rows;check("Repeated error has first last count and full occurrence history",history.Length==3&&history[0].Count==3&&history[0].FirstUtc==now);
            AgentMetricHistory.Capture(folder,1,r,p,[f]);check("Same report cannot duplicate metric history",AgentMetricHistory.Read(folder,1).Rows.Length==3);
            r.CapturedUtc=now.AddMinutes(2);AgentMetricHistory.Capture(folder,1,r,p,[]);check("Fresh confirmed recovery closes metric history",AgentMetricHistory.Read(folder,1).Rows[0].Transition=="Recovered");
            var state=new NetworkState();for(int i=0;i<3;i++)AvailabilityAlerts.Observe(state,"agent:1:update-delivery:SignatureMismatch",true,AppTime.FromUtc(now.AddMinutes(i)),0,30,Severity.Warning,"fixture","error","recovery");check("Repeated failure queues exactly one notification",state.Notifications.Count==1);AvailabilityAlerts.Observe(state,"agent:1:update-delivery:SignatureMismatch",true,AppTime.FromUtc(now.AddMinutes(3)),0,30,Severity.Critical,"fixture","escalation","recovery");check("Severity escalation queues one new notification",state.Notifications.Count==2);
            var signatures=new NetworkState();for(int i=0;i<3;i++)AvailabilityAlerts.Observe(signatures,"fixture",true,AppTime.FromUtc(now.AddMinutes(i)),0,30,Severity.Warning,"fixture","same failure","recovery","A");AvailabilityAlerts.Observe(signatures,"fixture",true,AppTime.FromUtc(now.AddMinutes(3)),0,30,Severity.Warning,"fixture","different cause","recovery","B");check("Changed signature notifies once without waiting for severity change",signatures.Notifications.Count==2);AvailabilityAlerts.Observe(signatures,"fixture",true,AppTime.FromUtc(now.AddMinutes(4)),0,30,Severity.Warning,"fixture","same changed cause","recovery","B");check("Repeated changed signature does not spam",signatures.Notifications.Count==2);
            var events=new AgentEventStore(folder,true);
            AgentEventBatch Batch(params AgentJournalEvent[] rows)=>new(Guid.NewGuid().ToString("N"),rows,[new("Linux","OK","ok")]);
            AgentJournalEvent Failure(string key,string result)=>new(key,"Linux","journald",1004,0,now,"svc.service: Failed with result '"+result+"'.","svc.service",3);
            events.Ingest(2,Batch(Failure("one","exit-code")),null,false,now);events.Ingest(2,Batch(Failure("two","exit-code")),null,false,now.AddSeconds(1));check("Same signature increments one incident",events.View(2).Problems.Count==1&&events.View(2).Problems[0].Count==2);
            events.Ingest(2,Batch(Failure("three","timeout")),null,false,now.AddSeconds(2));check("Changed failure nature creates separate signature",events.View(2).Problems.Count==2&&events.View(2).Problems.Select(p=>p.Signature).Distinct().Count()==2);
            r.CapturedUtc=now.AddSeconds(3);r.Linux!.Units=[new("svc.service","oneshot","inactive","dead","success",1,0,now.AddSeconds(3),null)];events.RecoverLinux(2,r,p,now.AddSeconds(4));check("Fresh successful inactive oneshot closes prior journal incidents",events.View(2).Problems.All(p=>!p.Open));
            var burst=Enumerable.Range(0,2100).Select(i=>new AgentJournalEvent("burst-"+i.ToString("D4"),"Linux","journald",1001,0,now.AddMilliseconds(i),"Out of memory","",2)).ToArray();foreach(var chunk in burst.Chunk(32))events.Ingest(3,Batch(chunk),null,false,now.AddSeconds(3));
            var historyRows=new List<AgentJournalEvent>();DateTime? beforeTime=null;string? beforeKey=null;do{var page=events.History(3,beforeTime,beforeKey,CancellationToken.None);historyRows.AddRange(page.Events);beforeTime=page.BeforeUtc;beforeKey=page.BeforeKey;}while(beforeTime!=null);
            check("Full archive retains repeats beyond 2000-event hot cache",events.View(3).Events.Count==2000&&historyRows.Count==2100&&historyRows.Select(e=>e.Key).Distinct().Count()==2100);
            check("Burst produces one notification and exact first last counter",events.View(3).Pending.Count==1&&events.View(3).Problems is [{Count:2100,FirstUtc:not null,LastEventUtc:not null}]);
        }finally{Directory.Delete(folder,true);}
    }
}
