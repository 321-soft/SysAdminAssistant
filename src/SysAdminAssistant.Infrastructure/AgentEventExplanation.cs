using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class AgentEventExplanation
{
    public static string Describe(AgentEventState state,AgentEventProblem problem,bool includeEvidence=true)
    {
        var rules=state.Policy.Rules.Where(r=>r.Id==problem.RuleId||problem.RuleId=="reboot"&&(r.Id is "power" or "shutdown" or "bsod")).ToArray();
        var evidence=state.Events.Where(e=>rules.Any(r=>r.Channel==e.Channel&&r.Provider.Equals(e.Provider,StringComparison.OrdinalIgnoreCase)&&r.Ids.Contains(e.Id))&&(problem.Source==""||e.Source==problem.Source)).OrderByDescending(e=>e.TimeUtc).Take(5).ToArray();
        return "Первое событие: "+(problem.FirstUtc?.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")??"не сохранено старой версией")+"; сигнатура: "+problem.Signature+"\n"+AgentIssueAdvice.Journal(problem.RuleId,problem.Source,problem.Count,problem.LastUtc,rules.FirstOrDefault(),evidence,problem.QuietMinutes,includeEvidence)+(problem.Open?"":"\nЗакрыто: "+problem.ClosedUtc?.ToServerTime().ToString("dd.MM.yyyy HH:mm:ss")+" · "+problem.Resolution);
    }
    public static string DescribeEvent(AgentEventState state,AgentJournalEvent e)
    {
        var rule=state.Policy.Rules.Concat(AgentEvents.Standard()).Concat(AgentEvents.StandardLinux()).Concat(AgentEvents.Role("SQL")).Concat(AgentEvents.Role("AD")).Concat(AgentEvents.Role("DNS")).FirstOrDefault(r=>r.Channel==e.Channel&&r.Provider.Equals(e.Provider,StringComparison.OrdinalIgnoreCase)&&r.Ids.Contains(e.Id));
        return AgentIssueAdvice.Journal(rule?.Id??"custom",e.Source,1,e.TimeUtc,rule,[e],rule?.QuietMinutes??0);
    }

}
