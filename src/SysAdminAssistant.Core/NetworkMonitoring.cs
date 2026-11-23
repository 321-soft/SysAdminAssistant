using System.Text.Json;
using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;
public static class NetworkDevices
{
    public static bool IsNetwork(string type)=>type is "MikroTik" or "Коммутатор";
    public static string Module(string type)=>IsNetwork(type)?"routers":"devices";
}
public sealed class NetworkDictionary
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public List<NetworkRule> Rules {get;set;}=[];
}
public sealed class NetworkRule
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Kind {get;set;}="Interface";
    public bool Enabled {get;set;}=true;
    public bool Notify {get;set;}=true;
    public bool NotifyRecovery {get;set;}=true;
    public string[] PrecedingTerms {get;set;}=[];
    public int PrecedingCount {get;set;}
    public string[] MessageTerms {get;set;}=[];
    public string[] TopicTerms {get;set;}=[];
    public string[] ExcludeTerms {get;set;}=[];
    public bool AccumulateMatches {get;set;}
    public bool MatchAll {get;set;}
    public int Threshold {get;set;}=1;
    public int WindowSeconds {get;set;}=300;
    public string EscalationGroup {get;set;}="";
    public string GroupBy {get;set;}="SourceIp";
    public int AutoCloseSeconds {get;set;}
    public string Role {get;set;}="Tunnel";
    public Severity Severity {get;set;}=Severity.Critical;
    public string[] Interfaces {get;set;}=[];
    public string[] Comments {get;set;}=[];
    public string[] Peers {get;set;}=[];
    public string[] Down {get;set;}=["link down","disconnected","terminating","connection terminated","negotiation failed"];
    public string[] Up {get;set;}=["link up","connected","established"];
    public int DelaySeconds {get;set;}=30;
    public int RecoveryChecks {get;set;}=2;
    public int FlapThreshold {get;set;}=4;public int FlapWindowSeconds {get;set;}=600;public int FlapQuietSeconds {get;set;}=600;
    public bool IgnoreDisabled {get;set;}=true;
}
public sealed class NetworkDictionaryPackage { public int Format {get;set;}=1; public List<NetworkDictionary> Dictionaries {get;set;}=[]; }
public static class NetworkDictionaries
{
    public static NetworkDictionary Generic()=>new(){Id="generic-network",Name="Типовой мониторинг сети",Rules=[
        new(){Id="wan",Name="WAN: доступ в интернет",Role="WAN"},new(){Id="server-tunnel",Name="Туннель до серверов",Role="Tunnel"},
        new(){Id="office-tunnel",Name="Межофисный туннель",Role="Tunnel"},new(){Id="uplink",Name="Межкоммутаторный uplink",Role="Uplink"},
        new(){Id="user-vpn",Name="Подключение VPN-пользователя",Role="UserVpn",Severity=Severity.Info} ]};
    public static NetworkDictionaryPackage Parse(string json)
    {
        if(json.Length>2_000_000)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Словарь слишком большой"));
        var package=JsonSerializer.Deserialize<NetworkDictionaryPackage>(json,new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустой словарь"));
        if(package.Format!=1||package.Dictionaries==null||package.Dictionaries.Count is <1 or >100)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный формат словаря"));
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var dictionary in package.Dictionaries) {
            if(dictionary==null||!ValidId(dictionary.Id)||!ids.Add(dictionary.Id)||string.IsNullOrWhiteSpace(dictionary.Name)||dictionary.Name.Length>200||dictionary.Rules==null||dictionary.Rules.Count is <1 or >100)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверное имя или ID словаря"));
            var rules=new HashSet<string>();
            foreach(var rule in dictionary.Rules) {
                if(rule!=null)foreach(var list in new[]{rule.Interfaces,rule.Comments,rule.Peers,rule.Down,rule.Up,rule.MessageTerms,rule.TopicTerms,rule.ExcludeTerms,rule.PrecedingTerms})if(list==null||list.Length>100||list.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>500))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные условия словаря"));
                if(rule==null||!ValidId(rule.Id)||!rules.Add(rule.Id)||string.IsNullOrWhiteSpace(rule.Name)||rule.Name.Length>200||rule.Role is not("WAN" or "Tunnel" or "Uplink" or "UserVpn" or "Security")||rule.Severity is not(Severity.Info or Severity.Warning or Severity.Critical)||rule.FlapThreshold is <0 or >1000||rule.FlapWindowSeconds is <1 or >86400||rule.FlapQuietSeconds is <1 or >604800||rule.DelaySeconds is <0 or >3600||rule.RecoveryChecks is <1 or >10||rule.EscalationGroup==null||rule.EscalationGroup.Length>0&&!ValidId(rule.EscalationGroup)||rule.Kind is not("Interface" or "Log")||rule.PrecedingCount is <0 or >1000||rule.PrecedingCount>0&&rule.PrecedingTerms.Length==0||rule.Threshold is <1 or >1000||rule.WindowSeconds is <1 or >86400||rule.AutoCloseSeconds is <0 or >604800||rule.GroupBy is not("All" or "SourceIp")||rule.Kind=="Log"&&rule.MessageTerms.Length==0&&rule.TopicTerms.Length==0)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверное правило сетевого мониторинга"));
                foreach(var list in new[]{rule.Interfaces,rule.Comments,rule.Peers,rule.Down,rule.Up,rule.MessageTerms,rule.TopicTerms,rule.ExcludeTerms,rule.PrecedingTerms})if(list==null||list.Length>100||list.Any(s=>string.IsNullOrWhiteSpace(s)||s.Length>500))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные условия словаря"));
            }
        }
        return package;
    }
    public static void ValidateRules(List<NetworkRule> rules){
        if(rules==null||rules.Count>100)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("До 100 правил на устройство"));
        if(rules.Count>0)Parse(JsonSerializer.Serialize(new NetworkDictionaryPackage{Dictionaries=[new(){Id="validation",Name="validation",Rules=rules}]}));
    }
    private static bool ValidId(string id)=>id!=null&&id.Length is >0 and <=100&&id.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_');
}
public sealed record NetworkPortState(string Name,bool Running,bool Disabled,string Comment);
public sealed class NetworkLogEvidence
{
    public NetworkLog Log {get;set;}=new();
    public NetworkRule Rule {get;set;}=new();
    public DateTime EvaluatedAtUtc {get;set;}
    public int MatchedCount {get;set;}
}
public sealed class NetworkProblem
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");public string Kind {get;set;}="Interface";public bool OpenQueued {get;set;}public bool CloseQueued {get;set;}public string RuleId {get;set;}="";public string Key {get;set;}="";public string Title {get;set;}="";public Severity Severity {get;set;}
    public DateTime? CountSince {get;set;}public DateTime? DownSince {get;set;}public DateTime? OpenedAt {get;set;}public DateTime LastSeen {get;set;}public DateTime? RestoredAt {get;set;}
    public List<NetworkLogEvidence> Evidence {get;set;}=[];
    public string Resolution {get;set;}="";public bool Open {get;set;}public int HealthyChecks {get;set;}public int Repeats {get;set;}
}
public sealed class NetworkLog
{
    public DateTime? EventUtc {get;set;}
    [System.Text.Json.Serialization.JsonIgnore] public DateTime OccurredUtc=>EventUtc??ReceivedAt;
    public bool MonitoringLogin {get;set;}public bool Baseline {get;set;}public string Key {get;set;}="";public DateTime ReceivedAt {get;set;}public string RouterTime {get;set;}="";public string Topics {get;set;}="";public string Message {get;set;}="";
    public Severity Severity {get;set;}=Severity.Info;public string Meaning {get;set;}="Событие роутера; правило оценки не назначено";
}
public static class NetworkSignals
{
    public static void Observe(List<NetworkProblem> problems,NetworkRule rule,string objectName,bool healthy,DateTime now)
    {
        var key=rule.Id+":"+objectName;
        var matching=problems.Where(p=>p.Key==key||(rule.Id=="connection"&&p.RuleId=="connection")).ToArray();
        if(healthy){
            // Old stores can contain several open records, or a legacy connection key.
            // Confirm recovery for every active record, not only the last (possibly closed) one.
            foreach(var p in matching.Where(p=>p.Open||p.DownSince!=null)){
                p.LastSeen=now;p.HealthyChecks++;
                if(!p.Open||p.HealthyChecks>=rule.RecoveryChecks){
                    if(p.Open){p.RestoredAt=now;p.Resolution="Связь восстановлена, состояние подтверждено";}
                    p.Open=false;p.DownSince=null;p.Repeats=0;
                }
            }
            return;
        }
        var problem=matching.LastOrDefault(p=>p.Open)??matching.LastOrDefault(p=>p.RestoredAt==null);
        if(problem==null){problem=new(){RuleId=rule.Id,Key=key,Title=rule.Name+" · "+objectName,Severity=rule.Severity};problems.Add(problem);}
        foreach(var duplicate in matching.Where(p=>p.Open&&p!=problem)){duplicate.Open=false;duplicate.RestoredAt=now;duplicate.Resolution="Объединено с текущей проблемой того же состояния";}
        problem.LastSeen=now;problem.Severity=rule.Severity;problem.Title=rule.Name+" · "+objectName;
        problem.HealthyChecks=0;problem.DownSince??=now;problem.Repeats++;
        if(rule.Severity!=Severity.Info&&(now-problem.DownSince.Value).TotalSeconds>=rule.DelaySeconds){if(!problem.Open){problem.OpenedAt=problem.DownSince;problem.RestoredAt=null;}problem.Open=true;}
    }
    public static void Interfaces(List<NetworkProblem> problems,IEnumerable<NetworkPortState> interfaces,NetworkDictionary dictionary,IReadOnlyDictionary<string,string[]> overrides,DateTime now,bool maintenance=false)
    {
        if(maintenance)return;var ports=interfaces.ToArray();
        foreach(var previous in problems.Where(p=>p.Open&&p.Kind!="Log"&&!p.Key.StartsWith("connection:",StringComparison.Ordinal)&&!p.Key.StartsWith("log-reader:",StringComparison.Ordinal))) {
            var keep=dictionary.Rules.Any(r=>{
                if(!r.Enabled||r.Kind!="Interface")return false;
                var configured=overrides.TryGetValue(r.Id,out var selected)?selected:r.Interfaces;
                return configured.Any(name=>previous.Key==r.Id+":"+name)||ports.Any(port=>previous.Key==r.Id+":"+port.Name&&r.Comments.Any(c=>port.Comment.Contains(c,StringComparison.OrdinalIgnoreCase)));
            });
            if(!keep){previous.Open=false;previous.RestoredAt=now;previous.Resolution="Объект исключён из выбранных правил мониторинга";}
        }
        foreach(var rule in dictionary.Rules.Where(r=>r.Enabled&&r.Kind=="Interface")) {
            var names=overrides.TryGetValue(rule.Id,out var value)?value:rule.Interfaces;
            if(names.Length==0&&rule.Comments.Length==0)continue;
            var targets=ports.Where(p=>names.Contains(p.Name,StringComparer.OrdinalIgnoreCase)||rule.Comments.Any(c=>p.Comment.Contains(c,StringComparison.OrdinalIgnoreCase))).ToArray();
            foreach(var port in targets) {if(port.Disabled&&rule.IgnoreDisabled)continue;Observe(problems,rule,port.Name,port.Running,now);}
            foreach(var name in names.Where(n=>ports.All(p=>!p.Name.Equals(n,StringComparison.OrdinalIgnoreCase))))Observe(problems,rule,name,false,now);
        }
    }
    public static void Explain(NetworkLog log,NetworkDictionary dictionary,IReadOnlyDictionary<string,string[]> overrides)
    {
        var matches=dictionary.Rules.Where(rule=>rule.Enabled&&rule.Kind=="Interface"&& (overrides.TryGetValue(rule.Id,out var value)?value:rule.Interfaces).Concat(rule.Comments).Concat(rule.Peers).Any(name=>ContainsToken(log.Message,name))).ToArray();
        var down=matches.Where(r=>r.Down.Any(s=>log.Message.Contains(s,StringComparison.OrdinalIgnoreCase))).ToArray();
        var up=matches.Where(r=>r.Up.Any(s=>log.Message.Contains(s,StringComparison.OrdinalIgnoreCase))).ToArray();
        if(down.Length>0){log.Severity=down.Max(r=>r.Severity);log.Meaning=string.Join("; ",down.Select(r=>r.Name+": событие разрыва; состояние проверяется отдельно"));}
        else if(up.Length>0){log.Severity=Severity.Info;log.Meaning=string.Join("; ",up.Select(r=>r.Name+": сообщение о восстановлении; требуется подтверждение состояния"));}
    }
    private static bool ContainsToken(string text,string term)
    {
        int start=0;while((start=text.IndexOf(term,start,StringComparison.OrdinalIgnoreCase))>=0){var end=start+term.Length;bool Part(char c)=>char.IsLetterOrDigit(c)||c is '_' or '-' or '.';if((start==0||!Part(text[start-1]))&&(end==text.Length||!Part(text[end])))return true;start++;}return false;
    }
    public static LampState Color(IEnumerable<NetworkProblem> problems,bool configured,bool reachable,bool stale=false)
    {
        var open=problems.Where(p=>p.Open).ToArray();if(open.Any(p=>p.Severity==Severity.Critical))return LampState.Red;
        if(open.Any(p=>p.Severity==Severity.Warning))return LampState.Amber;
        return !configured||stale?LampState.Gray:reachable?LampState.Green:LampState.Red;
    }
}
