using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed class OsUpdateStatus
{
 public string State {get;set;}="Unknown"; public DateTime? CheckedUtc {get;set;} public string Installed {get;set;}="";public string Available {get;set;}=""; public bool RebootRequired {get;set;} public int PendingCount {get;set;} public string[] Items {get;set;}=[];public string Detail {get;set;}="";
 public bool HasUpdates=>State=="Available"||PendingCount>0||RebootRequired;public bool Stale=>CheckedUtc==null||DateTime.UtcNow-CheckedUtc>TimeSpan.FromDays(1);
 public static OsUpdateStatus Normalize(OsUpdateStatus value){value.Items??=[];value.Detail??="";value.Installed??="";value.Available??="";if(value.PendingCount>0||value.State!="Current"&&value.Items.Length>0)value.State="Available";return value;}
 public static OsUpdateStatus Merge(OsUpdateStatus previous,OsUpdateStatus current){current=Normalize(current);if(current.State=="Unknown"&&!current.HasUpdates&&previous.HasUpdates){previous.Detail="Последние сведения: "+current.Detail+" · прежнее наличие обновлений пока не опровергнуто";return previous;}return current;}
 public static OsUpdateStatus Parse(string s){try{return Normalize(JsonSerializer.Deserialize<OsUpdateStatus>(s,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new());}catch{return new();}}
}
public sealed class AgentEventRule
{
 [System.Text.Json.Serialization.JsonIgnore] public string DisplayName=>ProductLabels.AgentRule(this);
 public string Id {get;set;}=Guid.NewGuid().ToString("N"); public string Name {get;set;}="";public bool Enabled {get;set;}=true;public string Channel {get;set;}="System";public string Provider {get;set;}="";public int[] Ids {get;set;}=[];public Severity Severity {get;set;}=Severity.Warning;public int Threshold {get;set;}=1;public int CriticalThreshold {get;set;} public int WindowMinutes {get;set;}=5;public int QuietMinutes {get;set;}public bool Notify {get;set;}=true;public string Contains {get;set;}="";
}
public sealed class AgentEventPolicy {public int Version {get;set;}=1;public bool Enabled {get;set;}=true;public bool Updates {get;set;}=true;public List<AgentEventRule> Rules {get;set;}=AgentEvents.Standard();}
public sealed record AgentJournalEvent(string Key,string Channel,string Provider,int Id,long RecordId,DateTime TimeUtc,string Message,string Source="",int? Level=null);
public sealed record AgentJournalHealth(string Channel,string State,string Detail);
public sealed record AgentEventBatch(string Id,AgentJournalEvent[] Events,AgentJournalHealth[] Health);
public sealed record AgentReplyV3(AgentPolicy Policy,string Certificate,AgentSignedTrust Trust,AgentEventPolicy Events,string Ack);
public sealed record AgentEventsRequest(int DeviceId);
public sealed record AgentEventsSave(int DeviceId,AgentEventPolicy Policy,int ExpectedVersion);
public sealed record AgentEventResolve(int DeviceId,string Id);
public static class AgentEvents
{
 public static bool IgnoredFloppy(AgentJournalEvent e)=>e.Channel=="Linux"&&e.Provider.Equals("journald",StringComparison.OrdinalIgnoreCase)&&e.Id==1002&&!e.Message.Contains('\n')&&System.Text.RegularExpressions.Regex.IsMatch(e.Message,@"\bI/O error,\s*dev fd[0-9]+,\s*sector [0-9]+\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));

 public static bool HealthyNtfs(AgentJournalEvent e){if(e.Channel!="System"||e.Id!=98||e.Provider is not("Ntfs" or "Microsoft-Windows-Ntfs"))return false;if(e.Level is 1 or 2 or 3)return false;var text=e.Message;return e.Level==4||text.Contains("is healthy",StringComparison.OrdinalIgnoreCase)&&text.Contains("no action is needed",StringComparison.OrdinalIgnoreCase)||text.Contains("исправен",StringComparison.OrdinalIgnoreCase)&&!text.Contains("неисправен",StringComparison.OrdinalIgnoreCase)&&!text.Contains("не исправен",StringComparison.OrdinalIgnoreCase)&&text.Contains("не требуется",StringComparison.OrdinalIgnoreCase);}
 public static readonly string[] Channels=["Linux","System","Application","Security","Microsoft-Windows-Windows Defender/Operational","Microsoft-Windows-WindowsUpdateClient/Operational","Directory Service","DNS Server"];
 public static List<AgentEventRule> Standard(){var list=new List<AgentEventRule>();
 void Add(string id,string name,string channel,string provider,int[] ids,Severity level=Severity.Warning,int threshold=1,int quiet=0)=>list.Add(new(){Id=id,Name=name,Channel=channel,Provider=provider,Ids=ids,Severity=level,Threshold=threshold,QuietMinutes=quiet});
 Add("power","Неожиданная перезагрузка","System","Microsoft-Windows-Kernel-Power",[41],Severity.Critical);
 Add("shutdown","Аварийное завершение Windows","System","EventLog",[6008],Severity.Critical);
 Add("bsod","Сбой ядра / BSOD","System","Microsoft-Windows-WER-SystemErrorReporting",[1001],Severity.Critical);
 Add("planned","Плановая перезагрузка","System","User32",[1074],Severity.Info);
 Add("ntfs","Ошибка файловой системы","System","Ntfs",[55,98],Severity.Critical);
 Add("ntfs2","Ошибка файловой системы","System","Microsoft-Windows-Ntfs",[55,98],Severity.Critical);
 Add("whea","Аппаратная ошибка","System","Microsoft-Windows-WHEA-Logger",[18],Severity.Critical);
 Add("disk","Повторяющиеся ошибки диска","System","disk",[7,11,51,153],Severity.Critical,3);
 Add("stor","Сброс контроллера хранения","System","storahci",[129],Severity.Warning,3);
 Add("service","Служба аварийно завершилась","System","Service Control Manager",[7031,7034],Severity.Warning,3,10);
 Add("new-service","Установлена служба","System","Service Control Manager",[7045]);
 Add("app","Сбои приложения","Application","Application Error",[1000],Severity.Warning,3,10);
 Add("dotnet","Необработанные ошибки .NET","Application",".NET Runtime",[1026],Severity.Warning,3,10);
 Add("memory","Исчерпана память","System","Microsoft-Windows-Resource-Exhaustion-Detector",[2004]);
 Add("fail-login","Неудачные входы","Security","Microsoft-Windows-Security-Auditing",[4625],Severity.Warning,5,60);
 list.Single(r=>r.Id=="fail-login").CriticalThreshold=20;
 Add("logon-success","Успешный вход после серии ошибок","Security","Microsoft-Windows-Security-Auditing",[4624],Severity.Critical);
 Add("lock","Блокировка учётной записи","Security","Microsoft-Windows-Security-Auditing",[4740]);
 Add("audit-clear","Очищен журнал безопасности","Security","Microsoft-Windows-Eventlog",[1102],Severity.Critical);
 Add("audit-policy","Изменена политика аудита","Security","Microsoft-Windows-Security-Auditing",[4719]);
 Add("group","Изменён состав группы безопасности","Security","Microsoft-Windows-Security-Auditing",[4728,4732,4756]);
 Add("threat","Defender: угроза или ошибка устранения","Microsoft-Windows-Windows Defender/Operational","Microsoft-Windows-Windows Defender",[1116,1118,1119],Severity.Critical);
 Add("defender-off","Defender: защита отключена","Microsoft-Windows-Windows Defender/Operational","Microsoft-Windows-Windows Defender",[5001]);
 Add("defender-on","Defender: защита включена","Microsoft-Windows-Windows Defender/Operational","Microsoft-Windows-Windows Defender",[5000],Severity.Info);
 Add("updates-failed","Ошибка установки обновления","Microsoft-Windows-WindowsUpdateClient/Operational","Microsoft-Windows-WindowsUpdateClient",[20]);return list;}
 public static List<AgentEventRule> StandardLinux()=>[
 new(){Id="linux-oom",Name="Исчерпана память (OOM)",Channel="Linux",Provider="journald",Ids=[1001],Severity=Severity.Critical},
 new(){Id="linux-storage",Name="Ошибка диска или файловой системы",Channel="Linux",Provider="journald",Ids=[1002],Severity=Severity.Critical},
 new(){Id="linux-login",Name="Неудачные SSH-входы",Channel="Linux",Provider="journald",Ids=[1003],Severity=Severity.Warning,Threshold=5,CriticalThreshold=20,WindowMinutes=10,QuietMinutes=60},
 new(){Id="linux-service",Name="Аварийное завершение службы",Channel="Linux",Provider="journald",Ids=[1004],Severity=Severity.Warning},
 new(){Id="linux-kernel",Name="Ошибка ядра",Channel="Linux",Provider="journald",Ids=[1005],Severity=Severity.Critical}
 ];
 public static List<AgentEventRule> Role(string role)=>role switch{
 "SQL"=>[new(){Id="sql-io",Name="SQL: ошибки целостности / ввода-вывода",Channel="Application",Provider="MSSQLSERVER",Ids=[823,824],Severity=Severity.Critical},new(){Id="sql-retry",Name="SQL: повторные чтения",Channel="Application",Provider="MSSQLSERVER",Ids=[825],Threshold=3}],
 "AD"=>[new(){Id="ad",Name="AD: репликация",Channel="Directory Service",Provider="Microsoft-Windows-ActiveDirectory_DomainService",Ids=[1311,1566,1865,1925,2042,2095],Severity=Severity.Critical}],
 "DNS"=>[new(){Id="dns",Name="DNS: ошибки службы",Channel="DNS Server",Provider="Microsoft-Windows-DNS-Server-Service",Ids=[4000,4007,4013,4015],Threshold=3}],_=>[]};
 public static void Validate(AgentEventPolicy p){if(p.Version<1||p.Rules==null||p.Rules.Count>64||p.Rules.Select(x=>x.Id).Distinct().Count()!=p.Rules.Count)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректная политика журналов"));foreach(var v in p.Rules)if((v.Id=="logon-success"&&(v.Ids==null||v.Ids.Length!=1||v.Ids[0]!=4624||v.Channel!="Security"||v.Provider!="Microsoft-Windows-Security-Auditing"))||!AgentWire.Text(v.Id,100)||v.Id.Length==0||!AgentWire.Text(v.Name,160)||!Channels.Contains(v.Channel)||!AgentWire.Text(v.Provider,160)||v.Provider.IndexOfAny(['\'', '"','<','>','&'])>=0||v.Provider.Length==0||v.Ids==null||v.Ids.Length is <1 or >16||v.Ids.Any(x=>x is <0 or >65535)||v.CriticalThreshold is <0 or >100000||v.Threshold is <1 or >10000||v.WindowMinutes is <1 or >1440||v.QuietMinutes is <0 or >1440||v.Severity is not(Severity.Info or Severity.Warning or Severity.Critical)||!AgentWire.Text(v.Contains,256))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректное правило журнала"));}
 public static void Validate(AgentEventBatch b){if(!AgentWire.Text(b.Id,100)||b.Events==null||b.Events.Length>32||b.Health==null||b.Health.Length>16)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный пакет событий"));foreach(var e in b.Events)if(!AgentWire.Text(e.Key,160)||!Channels.Contains(e.Channel)||!AgentWire.Text(e.Provider,160)||e.Id is <0 or >65535||e.Level is <0 or >5||e.RecordId<0||e.Message==null||e.Message.Length>1000||!AgentWire.Text(e.Source,256)||e.TimeUtc.Kind!=DateTimeKind.Utc||e.TimeUtc>DateTime.UtcNow.AddMinutes(10))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректное событие"));foreach(var h in b.Health)if(!Channels.Contains(h.Channel)||h.State is not("OK" or "Gap" or "Denied" or "Unavailable" or "Absent" or "Backlog")||!AgentWire.Text(h.Detail,256)||(h.State=="Absent"&&h.Channel!="Microsoft-Windows-Windows Defender/Operational"))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректное состояние журнала"));}
}
