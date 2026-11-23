using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
public static class AgentAdviceSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var p=new AgentPolicy{StandardServices=false,DiskFreeGb=10,DiskFreePercent=10};
        var r=new AgentReport{MachineName="fixture",Os="Linux Ubuntu",CapturedUtc=DateTime.UtcNow,CpuCount=4,CpuPercent=96,MemoryTotalBytes=10L*1073741824,MemoryAvailableBytes=1073741824,UptimeSeconds=1000,Disks=[new("/boot",1073741824,734003200)],Services=[new("nginx.service","nginx","Stopped")]};
        check("Healthy boot no longer inherits global absolute floor",!AgentChecks.Evaluate(r,p).Any(f=>f.Key.StartsWith("disk:")));p.Linux=new(){Disks=[new("/boot",10,10240)]};var disk=AgentChecks.Evaluate(r,p).Single(f=>f.Key=="disk-policy:/boot");var details=AgentIssueAdvice.Metric(disk,r,p);
        check("Boot advice includes real free size and impossible absolute threshold",details.Contains("700,0")||details.Contains("700.0"));
        check("Boot advice explains exact threshold and safe next steps",details.Contains("10240 МиБ")&&details.Contains("не меньше размера ФС")&&details.Contains("uname -r")&&details.Contains("Не удаляйте файлы вручную")&&details.Contains("Проверено:"));
        r.Disks=[new("/boot",0,0,"Нет доступа")];details=AgentIssueAdvice.Metric(new("disk:/boot","Read error",Severity.Warning,2),r,p);
        check("Unreadable disk advice does not invent free bytes",details.Contains("Нет доступа")&&details.Contains("не подтверждены")&&!details.Contains("свободно 0"));
        details=AgentIssueAdvice.Metric(new("ram","RAM",Severity.Warning,2),r,p);
        check("Memory advice contains used available total and threshold",details.Contains("9")&&details.Contains("10")&&details.Contains("90")&&details.Contains("порог")&&details.Contains("free -h"));
        details=AgentIssueAdvice.Metric(new("service:nginx.service","Service",Severity.Critical,2),r,p);
        check("Service advice contains actual expected state and read-only commands",details.Contains("Stopped")&&details.Contains("Running")&&details.Contains("systemctl status")&&details.Contains("journalctl -u"));
        var known=AgentEvents.Standard().Concat(AgentEvents.StandardLinux()).Concat(AgentEvents.Role("SQL")).Concat(AgentEvents.Role("AD")).Concat(AgentEvents.Role("DNS"));
        foreach(var rule in known){var value=AgentIssueAdvice.Journal(rule.Id,"fixture",5,DateTime.UtcNow,rule,[],rule.QuietMinutes);check("Specific guidance for built-in agent rule "+rule.Id,value.Contains("Что проверить")&&!value.Contains("Для пользовательского правила"));}
        var raw=new AgentJournalEvent("key","Linux","journald",1003,0,DateTime.UtcNow,"PRIVATE-EVIDENCE","192.0.2.1",3);
        check("Journal raw evidence stays in UI and is omitted from notification advice",AgentIssueAdvice.Journal("linux-login",raw.Source,5,raw.TimeUtc,null,[raw],60).Contains("PRIVATE-EVIDENCE")&&!AgentIssueAdvice.Journal("linux-login",raw.Source,5,raw.TimeUtc,null,[raw],60,false).Contains("PRIVATE-EVIDENCE"));
        foreach(var rule in NetworkLogRules.Standard()){var problem=new NetworkProblem{Kind="Log",RuleId=rule.Id,Title=rule.Name,LastSeen=DateTime.UtcNow,Repeats=5};var value=NetworkIssueAdvice.Describe(problem,rule);check("Specific guidance for built-in router rule "+rule.Id,value.Contains("Что проверить")&&!value.Contains("Для пользовательского правила"));}
        var serviceAdvice=AvailabilityIssueAdvice.Service(new ServiceEntry{Type="HTTP",Address="https://login:PRIVATE-PASSWORD@example.com/api?token=PRIVATE-TOKEN",Status="HTTP 500"},2);
        check("Service advice includes result but not URL secrets",serviceAdvice.Contains("HTTP 500")&&serviceAdvice.Contains("example.com")&&!serviceAdvice.Contains("PRIVATE-"));
        var record=new ConnectivityRecord{CheckedAt=DateTime.Now,ProbeResultsJson=System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0,16).Select(i=>new ConnectivityProbe("WAN",new string('h',200)+i,"ERROR",DateTime.Now)),RemoteData.Json)};
        var wanAdvice=AvailabilityIssueAdvice.Group("WAN",record,2);
        check("Connectivity advice preserves guidance within notification budget",wanAdvice.Length<3000&&wanAdvice.Contains("Что проверить")&&wanAdvice.Contains("Ещё 10"));
        var notification=new NetworkNotification{Source=new string('S',1000),Title=new string('T',6000)};
        check("Detailed Telegram notifications fit message limit",NetworkNotifications.Format(notification).Length<4096);
        var view=new AgentView("id",1,true,false,"host","",DateTime.UtcNow,p,r,[new("service:nginx.service","Service",Severity.Critical,2)],"",[]);
        var unified=UnifiedDeviceProblems.Merge([],view,null,[],DateTime.UtcNow);
        check("Unified device problem has facts and remediation",unified.Single().Details.Contains("Stopped")&&unified.Single().Details.Contains("systemctl status"));
    }
}
