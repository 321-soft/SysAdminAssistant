using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record UnifiedDeviceProblem(string Key,DateTime? Time,Severity Severity,string Source,string Title,string Details,string Kind,ReportEvent? Mail=null,int? IncidentId=null,string JournalId="")
{
 public string Level=>Severity==SysAdminAssistant.Core.Severity.Critical?"Критично":Severity==SysAdminAssistant.Core.Severity.Warning?"Предупреждение":"Информация";public bool IsMail=>Kind=="Mail";public bool CanResolve=>Kind=="KSC"||IsMail||JournalId!="";
}
public static class UnifiedDeviceProblems
{
 public static UnifiedDeviceProblem[] Merge(IEnumerable<DeviceProblem> mail,AgentView? agent,AgentEventState? journal,AgentFinding[] findings,DateTime now){var result=mail.Select(p=>new UnifiedDeviceProblem("mail:"+p.Incident.Id,p.Incident.CreatedAt,p.Incident.Severity,p.Incident.Source,p.Incident.Title,p.Report.Issue+"\n"+p.Report.Description+"\n"+p.Report.Evidence,"Mail",p.Report,p.Incident.Id)).ToList();
 if(agent is {Approved:true,Revoked:false}){
  if(agent.LastSeenUtc==null||now-agent.LastSeenUtc>TimeSpan.FromSeconds(Math.Max(60,agent.Policy.IntervalSeconds*2)))result.Add(new("agent-link",AppTime.FromUtc(agent.LastSeenUtc),Severity.Warning,"Агент → подключение","Нет свежих данных агента",AgentIssueAdvice.Connection(agent.LastSeenUtc,now,agent.Policy.IntervalSeconds,agent.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true),"Metric"));
  else foreach(var f in agent.Findings.Concat(findings).GroupBy(f=>f.Key).Select(g=>g.First())){var entry=f.Key.StartsWith("journal:")?journal?.Problems.SingleOrDefault(p=>"journal:"+p.Id==f.Key&&p.Open):null;var details=AgentIssueAdvice.Metric(f,agent.Report,agent.Policy);
   if(entry!=null&&journal!=null)details=AgentEventExplanation.Describe(journal,entry);
   result.Add(new(f.Key,AppTime.FromUtc(entry?.LastUtc??agent.LastSeenUtc),f.Severity,entry!=null?(agent.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true?"Агент → журнал Linux":"Агент → журнал Windows"):f.Key.StartsWith("journal-reader:")?"Агент → чтение журналов":"Агент → показатели / службы",f.Title,details,entry!=null?"Journal":"Metric",JournalId:entry?.Id??""));
  }
 }
 return result.OrderByDescending(p=>p.Severity).ThenByDescending(p=>p.Time).ToArray();}
}
