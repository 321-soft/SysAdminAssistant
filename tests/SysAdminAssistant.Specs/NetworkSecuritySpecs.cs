using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class NetworkSecuritySpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        var rules=NetworkLogRules.Standard();NetworkDictionaries.ValidateRules(rules);
        check("Security standard rules have stable unique IDs",rules.Select(r=>r.Id).Distinct().Count()==rules.Count);
        var legacy=NetworkLogRules.Effective(NetworkDictionaries.Generic(),new Dictionary<string,string[]>{{"wan",["ether1"]}},null);
        check("Security defaults extend existing network templates",legacy.Any(r=>r.Id=="auth-bruteforce")&&legacy.Single(r=>r.Id=="wan").Interfaces.SequenceEqual(["ether1"]));
        check("Security custom rules do not silently reenable deleted templates",NetworkLogRules.Effective(NetworkDictionaries.Generic(),new Dictionary<string,string[]>(),[]).Count==0);
        var now=DateTime.UtcNow;var problems=new List<NetworkProblem>();var logs=new List<NetworkLog>();
        NetworkLog Failure(string ip,int index)=>new(){Key="f"+index,ReceivedAt=now.AddSeconds(index),Topics="system,error,critical",Message="login failure for user admin from "+ip+" via winbox"};
        for(int i=0;i<5;i++)logs.Add(Failure("192.0.2.10",i));
        NetworkLogRules.Evaluate(problems,logs,[],rules,now.AddSeconds(5));check("Security first historical buffer creates no alerts",problems.Count==0);
        NetworkLogRules.Evaluate(problems,logs,logs,rules,now.AddSeconds(5));
        check("Security five failures open warning below critical threshold",problems.Any(p=>p.RuleId=="auth-failure-warning"&&p.Open)&&!problems.Any(p=>p.Severity==Severity.Critical));
        check("Security below-threshold critical rule does not color logs red",logs.All(l=>l.Severity!=Severity.Critical));
        var oldCount=problems.Count;NetworkLogRules.Evaluate(problems,logs,[],rules,now.AddSeconds(10));check("Security unchanged log buffer creates no duplicate alarms",problems.Count==oldCount);
        var more=Enumerable.Range(5,15).Select(i=>Failure("192.0.2.10",i)).ToArray();logs.AddRange(more);NetworkLogRules.Evaluate(problems,logs,more,rules,now.AddSeconds(21));
        check("Security twenty failures open critical per-source alarm",problems.Count(p=>p.RuleId=="auth-bruteforce"&&p.Open)==1);
        var success=new NetworkLog{Key="success",ReceivedAt=now.AddSeconds(25),Topics="system,info,account",Message="user admin logged in from 192.0.2.10 via winbox"};logs.Add(success);NetworkLogRules.Evaluate(problems,logs,[success],rules,now.AddSeconds(25));
        check("Security success after failed attempts is critical",problems.Any(p=>p.RuleId=="login-after-failures"&&p.Open));
        var cleanSuccess=new NetworkLog{Key="clean",ReceivedAt=now.AddSeconds(26),Topics="system,info,account",Message="user admin logged in from 192.0.2.99 via winbox"};logs.Add(cleanSuccess);NetworkLogRules.Evaluate(problems,logs,[cleanSuccess],rules,now.AddSeconds(26));
        check("Security source IP groups isolate clean successful login",!problems.Any(p=>p.RuleId=="login-after-failures"&&p.Key.EndsWith("192.0.2.99")));
        var reboot=new NetworkLog{Key="reboot",ReceivedAt=now.AddSeconds(30),Topics="system,info",Message="router rebooted"};logs.Add(reboot);NetworkLogRules.Evaluate(problems,logs,[reboot],rules,now.AddSeconds(30));
        var rebootProblem=problems.Single(p=>p.RuleId=="unexpected-reboot");check("Security ordinary router reboot is critical",rebootProblem.Open&&rebootProblem.Severity==Severity.Critical);
        NetworkLogRules.Evaluate(problems,logs,[],rules,now.AddDays(1));check("Security reboot needs manual review while quiet auth expires",rebootProblem.Open&&!problems.Any(p=>p.RuleId=="auth-bruteforce"&&p.Open));
        NetworkLogRules.Close(rebootProblem,now.AddDays(1),"Reviewed");check("Security manual review closes selected event only",!rebootProblem.Open&&problems.Any(p=>p.RuleId=="login-after-failures"&&p.Open));
        check("Security IPv6 grouping normalizes address",NetworkLogRules.Group(new(){Message="login failure from 2001:db8::1 via api"},rules[0])=="2001:db8::1");
        check("Security uptime formats parsed",NetworkLogRules.UptimeSeconds("1w2d3h4m5s")==788645&&NetworkLogRules.UptimeSeconds("bad")==null);
        var store=new NetworkStore(Path.Combine(folder,"network-security-outbox"));
        store.State(s=>{var h=new NetworkHistory{DeviceId=1,Problems=[new(){RuleId="unexpected-reboot",Key="reboot",Title="Router rebooted",Severity=Severity.Critical,Open=true,OpenedAt=now}]};s.Histories.Add(h);NetworkNotifications.Capture(s,h,"Fixture",rules,now);NetworkNotifications.Capture(s,h,"Fixture",rules,now);return true;},true);
        check("Security outbox captures one transition despite repeated polls",store.State(s=>s.Notifications.Count)==1);
        var settings=new TelegramSettings{Enabled=true,Critical=true,ChatId="123",ProtectedToken="fake"};bool failed=false;
        try{await NetworkNotifications.FlushAsync(store,settings,default,(_,_)=>throw new IOException("offline"));}catch(IOException){failed=true;}
        check("Security Telegram failed send remains pending",failed&&store.State(s=>s.Notifications.Single().HandledAt)==null);
        var sent=new List<string>();await NetworkNotifications.FlushAsync(new NetworkStore(store.DirectoryPath),settings,default,(text,_)=>{sent.Add(text);return Task.CompletedTask;});
        await NetworkNotifications.FlushAsync(store,settings,default,(_,_)=>throw new Exception("duplicate"));
        check("Security Telegram retry survives restart and sends once",sent.Count==1&&sent[0].Contains("Fixture"));
        store.State(s=>{var h=s.Histories.Single();NetworkLogRules.Close(h.Problems.Single(),now.AddSeconds(40),"Reviewed");NetworkNotifications.Capture(s,h,"Fixture",rules,now.AddSeconds(40));return true;},true);
        await NetworkNotifications.FlushAsync(store,settings,default,(text,_)=>{sent.Add(text);return Task.CompletedTask;});check("Security critical selection includes recovery of critical event",sent.Count==2&&sent[1].Contains("ВОССТАНОВЛЕНО"));
        store.State(s=>{s.Notifications.Add(new(){Title="Info",Severity=Severity.Info});return true;},true);await NetworkNotifications.FlushAsync(store,settings,default,(_,_)=>throw new Exception("Info filter bypass"));check("Security Telegram respects level filter",store.State(s=>s.Notifications.Last().Result)=="Отключено настройками Telegram");
        var dbSettings=new AppSettings{AvailabilityAlertMinutes=0,SqlitePath=Path.Combine(folder,"notification-payments.db")};var factory=new DatabaseFactory(dbSettings);await factory.InitializeAsync();
        await using(var db=factory.Create()){db.Payments.Add(new(){Title="Invoice",DueDate=new DateTime(2026,10,17)});db.Services.Add(new(){Name="API",Address="example.test",Status="OFFLINE",CheckedAt=new DateTime(2026,10,19,12,0,0)});await db.SaveChangesAsync();}
        var appStore=new NetworkStore(Path.Combine(folder,"application-outbox"));
        await ApplicationNotifications.CaptureAsync(factory,appStore,dbSettings,new DateTime(2026,10,18,12,0,0),default);check("Telegram invoice reminder suppressed on weekend",appStore.State(s=>s.Notifications.Count)==0);
        await ApplicationNotifications.CaptureAsync(factory,appStore,dbSettings,new DateTime(2026,10,19,12,0,0),default);
        check("Telegram due invoice and failed service enqueue",appStore.State(s=>s.Notifications.Count)==2);
        await ApplicationNotifications.CaptureAsync(factory,appStore,dbSettings,new DateTime(2026,10,19,12,0,30),default);check("Telegram unchanged application state is deduplicated",appStore.State(s=>s.Notifications.Count)==2);
        var split=Enumerable.Range(0,20).Select(i=>Failure(i%2==0?"192.0.2.1":"192.0.2.2",i)).ToList();var splitProblems=new List<NetworkProblem>();NetworkLogRules.Evaluate(splitProblems,split,split,rules,now.AddSeconds(30));
        check("Security failures from different IPs do not combine into brute-force critical",splitProblems.All(p=>p.Severity!=Severity.Critical));
        var resetProblems=new List<NetworkProblem>();var initial=Enumerable.Range(0,5).Select(i=>Failure("192.0.2.3",i)).ToList();NetworkLogRules.Evaluate(resetProblems,initial,initial,rules,now.AddSeconds(5));NetworkLogRules.Close(resetProblems.Single(),now.AddSeconds(6),"Reviewed");
        var next=Failure("192.0.2.3",7);initial.Add(next);NetworkLogRules.Evaluate(resetProblems,initial,[next],rules,now.AddSeconds(7));check("Security manual resolution restarts escalation counters",resetProblems.Last().Severity==Severity.Info&&resetProblems.Count(p=>p.Open)==1);
        var weekendStore=new NetworkStore(Path.Combine(folder,"weekend-delivery"));weekendStore.State(s=>{s.Notifications.Add(new(){Title="Invoice",Severity=Severity.Critical,WorkingDaysOnly=true});s.Notifications.Add(new(){Title="Network incident",Severity=Severity.Critical});return true;},true);
        var weekendSent=new List<string>();await NetworkNotifications.FlushAsync(weekendStore,settings,default,(text,_)=>{weekendSent.Add(text);return Task.CompletedTask;},new DateTime(2026,10,18));
        check("Telegram deferred weekend invoice does not block critical network event",weekendSent.Count==1&&weekendSent[0].Contains("Network incident")&&weekendStore.State(s=>s.Notifications.First().HandledAt)==null);

        var unknownLogs=Enumerable.Range(0,20).Select(i=>new NetworkLog{ReceivedAt=now.AddSeconds(i),Message="authentication failed",Topics="system,critical"}).ToList();var unknownProblems=new List<NetworkProblem>();NetworkLogRules.Evaluate(unknownProblems,unknownLogs,unknownLogs,rules,now.AddSeconds(30));
        check("Security unknown sources are not falsely combined as one brute-force IP",unknownProblems.All(p=>p.Severity==Severity.Info));
        check("Security endpoint IP grouping strips source port",NetworkLogRules.Group(new(){Message="login failure from 192.0.2.4:1234 via api"},rules[0])=="192.0.2.4");
        var baselineFailures=Enumerable.Range(0,20).Select(i=>Failure("192.0.2.5",i)).ToList();foreach(var log in baselineFailures)log.Baseline=true;
        var firstFresh=Failure("192.0.2.5",21);baselineFailures.Add(firstFresh);var baselineProblems=new List<NetworkProblem>();NetworkLogRules.Evaluate(baselineProblems,baselineFailures,[firstFresh],rules,now.AddSeconds(30));check("Security baseline failures never inflate future thresholds",baselineProblems.Count==1&&baselineProblems.Single().Severity==Severity.Info);

    }
}
