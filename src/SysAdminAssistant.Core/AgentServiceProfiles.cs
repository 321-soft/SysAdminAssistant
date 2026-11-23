using System.Text.Json;
using System.Text.RegularExpressions;
namespace SysAdminAssistant.Core;
public sealed record AgentServiceRole(string Id,string Title,string Description);
public static class AgentServiceProfiles
{
    public const string Standard="$standard";
    public static readonly AgentServiceRole[] Catalog=[
        new("ad","Контроллер домена AD DS","NTDS, Kdc, Netlogon, LanmanServer, RpcSs, SamSs, EventSystem, LanmanWorkstation, Dnscache, W32Time. DNS/ADWS — если установлены; DFSR/NtFrs — автоматические или работающие."),
        new("dns","DNS-сервер","DNS; не клиентская Dnscache."),
        new("dhcp","DHCP-сервер","DHCPServer; не клиентская Dhcp."),
        new("sql","Microsoft SQL Server","MSSQLSERVER и MSSQL$INSTANCE. SQLSERVERAGENT / SQLAgent$INSTANCE и SQLBrowser — только автоматические или работающие. Express без Agent допустим."),
        new("1c","Сервер 1С:Предприятия","Обнаруженные агенты сервера 1С, включая несколько версий и экземпляров. SQL на другом сервере не добавляется. Процессы rphost/rmngr не являются службами."),
        new("file","Файловый сервер SMB","LanmanServer, RpcSs, SamSs; SrmSvc (FSRM) — автоматическая или работающая. DFS выбирается отдельно."),
        new("dfsn","Пространства имён DFS","Dfs, LanmanServer, RpcSs. Только на сервере пространства имён."),
        new("dfsr","Репликация DFS","DFSR. Только на участнике репликации."),
        new("iis","Веб-сервер IIS","W3SVC, WAS. Не проверка сайтов или пулов приложений."),
        new("hyperv","Хост Hyper-V","vmms. Только на хосте, не внутри гостевой ВМ."),
        new("rds","Терминальный сервер RDS","TermService; SessionEnv / UmRdpService — автоматические или работающие. Не выбирать только ради административного RDP."),
        new("ca","Центр сертификации AD CS","CertSvc. Не для штатно выключенного офлайн-корневого центра.")
    ];
    public static string Title(string id)=>id==Standard?"Базовые Windows":Catalog.Concat(LinuxServiceProfiles.Catalog).FirstOrDefault(r=>r.Id==id)?.Title??id;
    public static string Origin(AgentServiceRule r)=>r.Profiles is {Length:>0}?string.Join(", ",r.Profiles.Select(Title)):"Ручная / прежняя настройка";
    public static void Validate(AgentPolicy p)
    {
        if(p.ExcludedServices==null||p.ExcludedServices.Length>256||p.ExcludedServices.Any(n=>!AgentWire.Text(n,256)||string.IsNullOrWhiteSpace(n))||p.ExcludedServices.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.ExcludedServices.Length)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные исключения служб"));
        if(p.ServiceRoles==null||p.ServiceRoles.Length>Catalog.Length+LinuxServiceProfiles.Catalog.Length||p.ServiceRoles.Distinct().Count()!=p.ServiceRoles.Length||p.ServiceRoles.Any(id=>!Catalog.Any(r=>r.Id==id)&&!LinuxServiceProfiles.IsRole(id))||p.ServiceProfileOverflow is <0 or >20000||(!p.RoleProfilesConfigured&&p.ServiceRoles.Length>0))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные роли служб агента"));
        if(p.Services.Any(s=>s!=null&&s.Profiles!=null&&(s.Profiles.Length>Catalog.Length+LinuxServiceProfiles.Catalog.Length+1||s.Profiles.Distinct().Count()!=s.Profiles.Length||s.Profiles.Any(id=>id!=Standard&&!Catalog.Any(r=>r.Id==id)&&!LinuxServiceProfiles.IsRole(id)))))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный источник службы агента"));
    }
    private static bool Same(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase);
    private static bool Usable(AgentService s)=>!Same(s.StartMode,"Disabled");
    private static bool Persistent(AgentService s)=>Usable(s)&&(Same(s.StartMode,"Automatic")||Same(s.Status,"Running"));
    private static bool OneC(AgentService s)=>Regex.IsMatch(s.Name+"\n"+s.DisplayName,@"(?im)^(?:1C:Enterprise\s+8(?:\.\d+)*\s+Server Agent\b|1cv8[23]?(?:[\s_.\-]|$)|Агент сервера\s+1[СC](?:[:\s]|$))",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static bool Sql(AgentService s)=>Same(s.Name,"MSSQLSERVER")||s.Name.StartsWith("MSSQL$",StringComparison.OrdinalIgnoreCase)&&s.Name.Length>6;
    private static IEnumerable<AgentServiceRule> Candidates(string id,AgentService[] observed)
    {
        AgentServiceRule Rule(string n,Severity severity=Severity.Critical)=>new(n,true,"Running",severity,2,[id]);
        string[] names=id switch{
            "ad"=>["NTDS","Kdc","Netlogon","LanmanServer","RpcSs","SamSs","EventSystem","LanmanWorkstation","Dnscache","W32Time"],
            "dns"=>["DNS"],"dhcp"=>["DHCPServer"],"file"=>["LanmanServer","RpcSs","SamSs"],"dfsn"=>["Dfs","LanmanServer","RpcSs"],"dfsr"=>["DFSR"],"iis"=>["W3SVC","WAS"],"hyperv"=>["vmms"],"rds"=>["TermService"],"ca"=>["CertSvc"],_=>[]};
        foreach(var n in names)yield return Rule(n,n=="W32Time"?Severity.Warning:Severity.Critical);
        foreach(var s in observed.Where(Usable)){
            bool include=id switch{
                "ad"=>Same(s.Name,"DNS")||Same(s.Name,"ADWS")||Persistent(s)&&(Same(s.Name,"DFSR")||Same(s.Name,"NtFrs")),
                "sql"=>Sql(s)||Persistent(s)&&(Same(s.Name,"SQLSERVERAGENT")&&observed.Any(e=>Usable(e)&&Same(e.Name,"MSSQLSERVER"))||s.Name.StartsWith("SQLAgent$",StringComparison.OrdinalIgnoreCase)&&observed.Any(e=>Usable(e)&&Same(e.Name,"MSSQL$"+s.Name[9..]))||Same(s.Name,"SQLBrowser")&&observed.Any(e=>Usable(e)&&Sql(e))),
                "1c"=>OneC(s),"file"=>Same(s.Name,"SrmSvc")&&Persistent(s),"rds"=>Persistent(s)&&(Same(s.Name,"SessionEnv")||Same(s.Name,"UmRdpService")),_=>false};
            if(include)yield return Rule(s.Name,Same(s.Name,"ADWS")||Same(s.Name,"SQLBrowser")?Severity.Warning:Severity.Critical);
        }
    }
    public static AgentServiceRule[] Effective(AgentPolicy p,IEnumerable<AgentService> observed)
    {
        var rules=(p.StandardServices?AgentChecks.Defaults(observed,!p.RoleProfilesConfigured):[]).ToDictionary(r=>r.Name,StringComparer.OrdinalIgnoreCase);
        foreach(var r in p.Services)rules[r.Name]=r;
        return rules.Values.Where(r=>!p.ExcludedServices.Contains(r.Name,StringComparer.OrdinalIgnoreCase)).OrderBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static bool Sync(AgentPolicy p,IEnumerable<AgentService> inventory)
    {
        Validate(p);var before=JsonSerializer.Serialize(p.Services)+":"+p.ServiceProfileOverflow;
        var observed=inventory.ToArray();var active=p.ServiceRoles.ToHashSet();if(p.StandardServices)active.Add(Standard);
        var manual=p.Services.Where(r=>r.Profiles is not{Length:>0}&&!p.ExcludedServices.Contains(r.Name,StringComparer.OrdinalIgnoreCase)).ToDictionary(r=>r.Name,StringComparer.OrdinalIgnoreCase);
        var generated=new Dictionary<string,AgentServiceRule>(StringComparer.OrdinalIgnoreCase);
        void Add(AgentServiceRule r){if(p.ExcludedServices.Contains(r.Name,StringComparer.OrdinalIgnoreCase)||manual.ContainsKey(r.Name))return;if(generated.TryGetValue(r.Name,out var e))generated[r.Name]=e with{Profiles=e.Profiles!.Concat(r.Profiles!).Distinct().Order().ToArray(),Severity=(Severity)Math.Max((int)e.Severity,(int)r.Severity)};else generated.Add(r.Name,r);}
        foreach(var r in p.Services.Where(r=>r.Profiles is {Length:>0})){var owners=r.Profiles!.Where(id=>active.Contains(id)&&(id!=Standard||!p.RoleProfilesConfigured||new[]{"EventLog","RpcSs","Winmgmt","WinDefend"}.Contains(r.Name,StringComparer.OrdinalIgnoreCase))).ToArray();if(owners.Length>0)Add(r with{Profiles=owners});}
        if(p.StandardServices)foreach(var r in AgentChecks.Defaults(observed,!p.RoleProfilesConfigured))Add(r with{Profiles=[Standard]});
        foreach(var id in p.ServiceRoles)foreach(var r in Candidates(id,observed))Add(r);
        var old=p.Services.Select(r=>r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows=generated.Values.OrderByDescending(r=>old.Contains(r.Name)).ThenBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        p.ServiceProfileOverflow=Math.Max(0,manual.Count+rows.Length-64);
        p.Services=manual.Values.Concat(rows.Take(Math.Max(0,64-manual.Count))).OrderBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).ToList();
        return before!=JsonSerializer.Serialize(p.Services)+":"+p.ServiceProfileOverflow;
    }
    public static void Delete(AgentPolicy p,string name)
    {
        if(!AgentWire.Text(name,256)||string.IsNullOrWhiteSpace(name))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректное имя службы"));
        if(!p.ExcludedServices.Contains(name,StringComparer.OrdinalIgnoreCase)){
            if(p.ExcludedServices.Length>=256)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Список исключений полон: верните ненужные исключения перед удалением новых служб"));
            p.ExcludedServices=p.ExcludedServices.Append(name).ToArray();
        }
        p.Services.RemoveAll(r=>r.Name.Equals(name,StringComparison.OrdinalIgnoreCase));
    }
    public static void Restore(AgentPolicy p,string name)=>p.ExcludedServices=p.ExcludedServices.Where(n=>!n.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
    public static AgentFinding[] Findings(AgentPolicy p,IEnumerable<AgentService>? inventory=null)
    {
        var result=new List<AgentFinding>();
        foreach(var id in p.ServiceRoles.Where(id=>id is "sql" or "1c")){
            bool found=p.Services.Concat(p.ExcludedServices.Select(n=>new AgentServiceRule(n))).Any(r=>id=="sql"?Sql(new(r.Name,"","Unknown")):(r.Profiles?.Contains("1c")==true||OneC(new(r.Name,"","Unknown"))||(inventory?.Any(s=>Same(s.Name,r.Name)&&OneC(s))??false)));
            if(!found)result.Add(new("service-role:"+id,Title(id)+": основная служба не обнаружена; проверьте список и при необходимости добавьте её вручную",Severity.Warning,2));
        }
        if(p.ServiceProfileOverflow>0)result.Add(new("service-role:limit","Лимит служб: не добавлено "+p.ServiceProfileOverflow+" записей профилей; выберите нужные экземпляры вручную",Severity.Warning,2));
        return result.ToArray();
    }
}
