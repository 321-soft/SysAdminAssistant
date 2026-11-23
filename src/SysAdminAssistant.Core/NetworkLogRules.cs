using System.Net;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Core;
public static class NetworkLogRules
{
    public static List<NetworkRule> Standard()=>[
        Log("auth-failure-info","Неудачная попытка входа",Severity.Info,["login failure","authentication failed"],1,300,3600),
        Log("auth-failure-warning","Повторные неверные пароли с одного IP",Severity.Warning,["login failure","authentication failed"],5,300,3600),
        Log("auth-bruteforce","Возможный перебор пароля с одного IP",Severity.Critical,["login failure","authentication failed"],20,300,3600),
        new(){Id="login-after-failures",Name="Успешный вход после серии неверных паролей",Kind="Log",Role="Security",Severity=Severity.Critical,MessageTerms=["logged in from"],PrecedingTerms=["login failure","authentication failed"],PrecedingCount=5,WindowSeconds=600,AutoCloseSeconds=0},
        Log("management-login","Вход в управление маршрутизатором",Severity.Info,["logged in from"],1,300,60),
        Log("configuration-change","Изменена конфигурация маршрутизатора",Severity.Warning,["changed by","added by","removed by"],1,300,0,"All"),
        Log("unexpected-reboot","Маршрутизатор перезагружен",Severity.Critical,["router rebooted","router was rebooted","system rebooted"],1,300,0,"All"),
        Log("reboot-storm","Несколько перезагрузок за короткое время",Severity.Critical,["router rebooted","router was rebooted","system rebooted"],3,1800,0,"All"),
        Log("resource-failure","Ошибка памяти, ядра или накопителя",Severity.Critical,["out of memory","kernel panic","disk failure","bad blocks"],1,300,0,"All"),
        Log("power-temperature","Проблема питания, вентилятора или температуры",Severity.Critical,["temperature above","overheating","power supply failure","fan failure"],1,300,0,"All"),
        Log("storage-warning","Предупреждение накопителя",Severity.Warning,["disk is full","not enough disk space","low disk space"],1,300,0,"All"),
        Log("certificate-warning","Ошибка или истечение сертификата",Severity.Warning,["certificate has expired","certificate expired","certificate verification failed","ssl fatal"],3,300,900,"All"),
        Log("syn-flood","Возможный SYN flood / сканирование",Severity.Warning,["possible SYN flooding","syn flooding","port scan"],1,300,900,"All"),
        new(){Id="router-critical",Name="Другие критические сообщения RouterOS",Kind="Log",Role="Security",Severity=Severity.Critical,TopicTerms=["critical"],ExcludeTerms=["login failure","authentication failed","router rebooted","router was rebooted","system rebooted"],GroupBy="All"}
    ];
    private static NetworkRule Log(string id,string name,Severity severity,string[] terms,int threshold,int window,int quiet,string group="SourceIp")=>new(){Id=id,Name=name,Kind="Log",Role="Security",Severity=severity,MessageTerms=terms,Threshold=threshold,WindowSeconds=window,AutoCloseSeconds=quiet,GroupBy=group,NotifyRecovery=severity!=Severity.Info,AccumulateMatches=id is "unexpected-reboot" or "reboot-storm",EscalationGroup=id.StartsWith("auth-",StringComparison.Ordinal)?"authentication-failures":id is "unexpected-reboot" or "reboot-storm"?"reboots":""};
    public static double? UptimeSeconds(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        var match=Regex.Match(value,@"^(?:(\d+)w)?(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(match.Success)return Enumerable.Range(1,5).Select((i)=>double.TryParse(match.Groups[i].Value,out var n)?n*new[]{604800,86400,3600,60,1}[i-1]:0).Sum();
        return TimeSpan.TryParse(value.Replace("d","."),System.Globalization.CultureInfo.InvariantCulture,out var time)?time.TotalSeconds:null;
    }
    public static List<NetworkRule> Effective(NetworkDictionary dictionary,IReadOnlyDictionary<string,string[]> overrides,List<NetworkRule>? custom)
    {
        if(custom!=null)return custom;
        var rules=System.Text.Json.JsonSerializer.Deserialize<List<NetworkRule>>(System.Text.Json.JsonSerializer.Serialize(dictionary.Rules))!;
        foreach(var rule in rules)if(overrides.TryGetValue(rule.Id,out var names))rule.Interfaces=names;
        return rules.Concat(Standard().Where(s=>rules.All(r=>r.Id!=s.Id))).ToList();
    }
    public static bool Matches(NetworkLog log,NetworkRule rule)
    {
        if(log.MonitoringLogin&&rule.Id=="management-login")return false;
        if(log.Baseline||!rule.Enabled||rule.Kind!="Log"||rule.ExcludeTerms.Any(t=>log.Message.Contains(t,StringComparison.OrdinalIgnoreCase)))return false;
        var topics=log.Topics.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        return (rule.TopicTerms.Length==0||rule.TopicTerms.Any(t=>topics.Contains(t,StringComparer.OrdinalIgnoreCase)))&&
            (rule.MessageTerms.Length==0||(rule.MatchAll?rule.MessageTerms.All(t=>log.Message.Contains(t,StringComparison.OrdinalIgnoreCase)):rule.MessageTerms.Any(t=>log.Message.Contains(t,StringComparison.OrdinalIgnoreCase))));
    }
    public static string Group(NetworkLog log,NetworkRule rule)
    {
        if(rule.GroupBy=="All")return "Все источники";
        var match=Regex.Match(log.Message,@"\bfrom\s+(\[[^\]]+\](?::\d+)?|[0-9a-fA-F:.]+)(?=\s|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var value=match.Success?match.Groups[1].Value:"";
        if(IPAddress.TryParse(value.Trim('[',']'),out var address))return address.ToString();
        return IPEndPoint.TryParse(value,out var endpoint)?endpoint.Address.ToString():"IP не указан в логе";
    }
    public static bool ContextMatches(NetworkLog log,IReadOnlyList<NetworkLog> history,NetworkRule rule)
    {
        if(!Matches(log,rule))return false;if(rule.PrecedingCount==0)return true;
        var group=Group(log,rule);var earlier=history.TakeWhile(candidate=>!ReferenceEquals(candidate,log));
        return earlier.Count(l=>!l.Baseline&&l.OccurredUtc<=log.OccurredUtc&&l.OccurredUtc>=log.OccurredUtc.AddSeconds(-rule.WindowSeconds)&&rule.PrecedingTerms.Any(t=>l.Message.Contains(t,StringComparison.OrdinalIgnoreCase))&&Group(l,rule)==group)>=rule.PrecedingCount;
    }
    public static void Evaluate(List<NetworkProblem> problems,IReadOnlyList<NetworkLog> history,IReadOnlyList<NetworkLog> fresh,List<NetworkRule> rules,DateTime now)
    {
        foreach(var problem in problems.Where(p=>p.Open&&p.Kind=="Log")) {
            var rule=rules.SingleOrDefault(r=>r.Id==problem.RuleId&&r.Enabled&&r.Kind=="Log");
            if(rule==null)Close(problem,now,"Правило удалено или выключено");
        }
        foreach(var rule in rules.Where(r=>r.Enabled&&r.Kind=="Log")) {
            foreach(var group in fresh.Where(l=>ContextMatches(l,history,rule)).GroupBy(l=>Group(l,rule))) {
                if(rule.GroupBy=="SourceIp"&&group.Key=="IP не указан в логе"&&(rule.Threshold>1||rule.PrecedingCount>0))continue;
                var key="log:"+(rule.EscalationGroup.Length>0?"group-"+rule.EscalationGroup:rule.Id)+":"+group.Key;var last=problems.LastOrDefault(p=>p.Key==key);
                var since=now.AddSeconds(-rule.WindowSeconds);var cutoff=last?.RestoredAt??last?.CountSince;
                var eligible=cutoff==null?history:history.Where(l=>l.OccurredUtc>cutoff).ToArray();
                var count=eligible.Count(l=>l.OccurredUtc<=now&&l.OccurredUtc>=since&&ContextMatches(l,eligible,rule)&&Group(l,rule)==group.Key);
                if(count>=rule.Threshold)foreach(var log in group){log.Severity=(Severity)Math.Max((int)log.Severity,(int)rule.Severity);log.Meaning=rule.Name+" · "+group.Key;}
                if(last?.Open==true){
                    if(count>=rule.Threshold)CaptureEvidence(last,eligible.Where(l=>l.OccurredUtc<=now&&l.OccurredUtc>=since&&ContextMatches(l,eligible,rule)&&Group(l,rule)==group.Key),rule,now,count);
                    if(count>=rule.Threshold&&(rule.Severity>last.Severity||rule.Id=="reboot-storm"&&last.RuleId!="reboot-storm")){last.OpenQueued=false;last.Severity=rule.Severity;last.RuleId=rule.Id;last.Title=rule.Name+" · "+group.Key;}
                    if(rule.AccumulateMatches){if(last.LastSeen!=now)last.Repeats+=group.Count();}else last.Repeats=count;last.LastSeen=group.Max(l=>l.OccurredUtc);continue;
                }
                if(count<rule.Threshold)continue;
                var created=new NetworkProblem{CountSince=cutoff,Kind="Log",RuleId=rule.Id,Key=key,Title=rule.Name+" · "+group.Key,Severity=rule.Severity,DownSince=group.Min(l=>l.OccurredUtc),OpenedAt=group.Min(l=>l.OccurredUtc),LastSeen=group.Max(l=>l.OccurredUtc),Open=true,Repeats=count};
                CaptureEvidence(created,eligible.Where(l=>l.OccurredUtc<=now&&l.OccurredUtc>=since&&ContextMatches(l,eligible,rule)&&Group(l,rule)==group.Key),rule,now,count);
                problems.Add(created);
            }
            if(rule.AutoCloseSeconds>0)foreach(var problem in problems.Where(p=>p.Open&&p.Kind=="Log"&&p.RuleId==rule.Id&&(now-p.LastSeen).TotalSeconds>=rule.AutoCloseSeconds))Close(problem,now,"Повторов нет в течение заданного периода");
        }
    }
    // Keep a bounded independent snapshot: history pruning and rule edits must not rewrite the cause.
    private static void CaptureEvidence(NetworkProblem problem,IEnumerable<NetworkLog> logs,NetworkRule rule,DateTime now,int count)
    {
        var ruleJson=System.Text.Json.JsonSerializer.Serialize(rule);
        var candidates=logs.ToArray();
        foreach(var log in candidates.Take(1).Concat(candidates.TakeLast(19)).Distinct()){
            if(problem.Evidence.Any(e=>e.Log.Key==log.Key&&e.Log.OccurredUtc==log.OccurredUtc&&e.Log.Message==log.Message&&System.Text.Json.JsonSerializer.Serialize(e.Rule)==ruleJson))continue;
            problem.Evidence.Add(new(){Log=System.Text.Json.JsonSerializer.Deserialize<NetworkLog>(System.Text.Json.JsonSerializer.Serialize(log))!,Rule=System.Text.Json.JsonSerializer.Deserialize<NetworkRule>(ruleJson)!,EvaluatedAtUtc=now,MatchedCount=count});
        }
        if(problem.Evidence.Count>20)problem.Evidence=problem.Evidence.Take(1).Concat(problem.Evidence.TakeLast(19)).ToList();
    }
    public static void Close(NetworkProblem problem,DateTime now,string reason){problem.Open=false;problem.RestoredAt=now;problem.Resolution=reason;}
}
