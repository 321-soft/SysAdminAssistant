using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class AvailabilitySpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var now=new DateTime(2026,10,20,9,0,0,DateTimeKind.Utc);var state=new NetworkState();
        void Bad(int seconds)=>AvailabilityAlerts.Observe(state,"device",true,now.AddSeconds(seconds),2,60,Severity.Warning,"Device","Down","Up");
        void Good(int seconds)=>AvailabilityAlerts.Observe(state,"device",false,now.AddSeconds(seconds),2,60,Severity.Warning,"Device","Down","Up");
        check("Availability delay defaults to two minutes",new AppSettings().AvailabilityAlertMinutes==2);
        Bad(0);Bad(119);check("Availability 119 second outage is silent",state.Notifications.Count==0);
        Good(120);check("Availability brief recovery sends neither problem nor recovery",state.Notifications.Count==0&&state.AvailabilityAlerts.Count==0);
        Bad(150);Bad(269);check("Availability new outage restarts timeout",state.Notifications.Count==0);
        Bad(270);check("Availability sustained 120 seconds queues once",state.Notifications.Count==1);
        Bad(280);check("Availability repeated polls do not duplicate",state.Notifications.Count==1);
        Good(290);check("Availability recovered unsent outage cancels pending message",state.Notifications.Single().HandledAt!=null&&state.Notifications.Count==1);
        Bad(300);Bad(420);state.Notifications.Last().Result="Отправлено";state.Notifications.Last().HandledAt=now.AddSeconds(420);
        Good(430);check("Availability notified outage has one recovery",state.Notifications.Count==3&&state.Notifications.Last().Transition.Contains("ВОССТАНОВЛЕНО"));
        Good(440);check("Availability recovery does not repeat",state.Notifications.Count==3);
        state=new();Bad(0);Bad(0);check("Availability same snapshot cannot advance timer",state.Notifications.Count==0);
        Bad(700);check("Availability long blind gap restarts pending timer",state.Notifications.Count==0&&state.AvailabilityAlerts["device"].FailedSinceUtc==now.AddSeconds(700));
        AvailabilityAlerts.Retain(state,[],now.AddSeconds(701));check("Availability disabled check drops pending timer",state.AvailabilityAlerts.Count==0);
        var store=new NetworkStore(Path.Combine(folder,"availability-restart"));
        store.State(s=>{AvailabilityAlerts.Observe(s,"wan",true,now,2,60,Severity.Warning,"WAN","Down","Up");return true;},true);
        var restarted=new NetworkStore(store.DirectoryPath);restarted.State(s=>{AvailabilityAlerts.Observe(s,"wan",true,now.AddMinutes(2),2,60,Severity.Warning,"WAN","Down","Up");return true;},true);
        check("Availability pending timeout survives restart",restarted.State(s=>s.Notifications.Count)==1);
        check("Connectivity parses distinct LAN IPv4 DNS and IPv6",ConnectivityProbes.Hosts("192.0.2.1;example.test\n2001:db8::1;EXAMPLE.TEST").Length==3);
        bool invalid=false;try{ConnectivityProbes.Hosts("https://example.test:443");}catch(ArgumentException){invalid=true;}check("Connectivity rejects URL instead of host",invalid);
        ConnectivityProbe[] probes=[new("WAN","192.0.2.1","OK",now),new("WAN","192.0.2.2","ERROR",now)];
        check("Connectivity partial failure is amber",ConnectivityProbes.Aggregate(probes)=="PARTIAL"&&ConnectivityProbes.Color("PARTIAL")==LampState.Amber);
        check("Connectivity all failed is red",ConnectivityProbes.Aggregate(probes.Select(p=>p with{Result="ERROR"}))=="ERROR");
        check("Connectivity empty LAN is disabled",ConnectivityProbes.Aggregate([])=="DISABLED");
        var measured=await ConnectivityProbes.CheckAsync(new(){LanPingHosts="192.0.2.1;192.0.2.2",PingHost="198.51.100.1"},(host,ct)=>Task.FromResult(host.EndsWith(".2")?"ERROR":"OK"),default);
        check("Connectivity independently checks LAN and WAN endpoints",measured.Count(p=>p.Group=="LAN")==2&&measured.Single(p=>p.Group=="WAN").Result=="OK");
        var net=new NetworkState();var problem=new NetworkProblem{Open=true,DownSince=now,OpenedAt=now,Title="WAN",Severity=Severity.Critical};var history=new NetworkHistory{Problems=[problem]};
        NetworkNotifications.Capture(net,history,"Router",[],now.AddSeconds(119),120);check("Router interface notification waits two minutes",net.Notifications.Count==0&&!problem.OpenQueued);
        problem.Open=false;problem.RestoredAt=now.AddSeconds(119);NetworkNotifications.Capture(net,history,"Router",[],now.AddSeconds(119),120);check("Router transient interface has no recovery notification",net.Notifications.Count==0);
        problem.Open=true;NetworkNotifications.Capture(net,history,"Router",[],now.AddSeconds(120),120);check("Router sustained interface queues at timeout",net.Notifications.Count==1);
        var security=new NetworkProblem{Kind="Log",Open=true,Title="Reboot",OpenedAt=now,Severity=Severity.Critical};history.Problems.Add(security);NetworkNotifications.Capture(net,history,"Router",[],now,120);check("Router security log bypasses availability timeout",net.Notifications.Count==2);
        var settings=new AppSettings{SqlitePath=Path.Combine(folder,"availability-integration.db")};var f=new DatabaseFactory(settings);await f.InitializeAsync();
        await using(var db=f.Create()){
            db.Devices.Add(new(){Name="Offline",Host="192.0.2.1",Status="OFFLINE",CheckedAt=now.ToLocalTime()});
            db.Jobs.Add(new(){Title="Missed backup",Active=true,Days=127,DeadlineMinutes=60,GraceMinutes=0,EarlyMinutes=0,MonitoringSince=now.ToLocalTime().Date.AddDays(-2)});await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_ConnectivityRecord DROP COLUMN Lan;");await db.Database.ExecuteSqlRawAsync("ALTER TABLE sa2_ConnectivityRecord DROP COLUMN ProbeResultsJson;");
            await BindingSchema.EnsureAsync(db);await BindingSchema.EnsureAsync(db);
            db.Connectivity.Add(new(){Ping="OK",Dns="OK",CheckedAt=now.ToLocalTime()});await db.SaveChangesAsync();
            check("Connectivity additive schema upgrade is repeatable",(await db.Connectivity.SingleAsync()).Lan=="DISABLED");
        }
        var appStore=new NetworkStore(Path.Combine(folder,"availability-integration"));await ApplicationNotifications.CaptureAsync(f,appStore,settings,now.ToLocalTime(),default);
        check("Backup missing report bypasses two minute availability timeout",appStore.State(s=>s.Notifications.Any(n=>n.Source.StartsWith("Резервное копирование"))&&!s.Notifications.Any(n=>n.Source.StartsWith("Устройства"))));
        await using(var db=f.Create()){var d=await db.Devices.SingleAsync();d.CheckedAt=now.AddMinutes(2).ToLocalTime();await db.SaveChangesAsync();}
        await ApplicationNotifications.CaptureAsync(f,appStore,settings,now.AddMinutes(2).ToLocalTime(),default);
        check("Device availability enqueues after confirmed timeout",appStore.State(s=>s.Notifications.Count(n=>n.Source.StartsWith("Устройства")))==1);
    }
}
