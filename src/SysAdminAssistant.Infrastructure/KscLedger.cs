using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record KscIssue(string Key,int DeviceId,string Source,string Host,string Title,Severity Severity,string Recovery,bool Notify,long Count,DateTime FirstUtc,DateTime LastUtc){public KscEvent? LatestEvent {get;init;} public string? LatestEventSummary=>LatestEvent?.Summary; public string? AttackerIp {get;init;} public DateTime? ResolvedUtc {get;init;} public string? Resolution {get;init;} public string? EventCode {get;init;} public long LastEventId {get;init;} public string? ComputerName {get;init;} public string Computer=>ComputerName??(Host.Length==0?"Сервер администрирования":Host);}
public sealed record KscSectionView(bool Active,KscStatus Status,KscTelegramOptions Telegram,KscIssue[] Problems){public KscIssue[] Resolved {get;init;}=[];}
public sealed class KscLedger{
 public Guid Generation{get;set;}
 public long Position{get;set;}
 public Dictionary<string,int> Hosts{get;set;}=[];
 public List<KscRule> Rules{get;set;}=[];
 public List<KscIssue> Open{get;set;}=[];
 public List<KscIssue> Resolved{get;set;}=[];
 public void Close(KscIssue issue,DateTime now,string reason){
  Open.RemoveAll(p=>p.Key==issue.Key);
  Resolved.Add(issue with{ResolvedUtc=now,Resolution=reason});
  if(Resolved.Count>256)Resolved.RemoveRange(0,Resolved.Count-256);
 }
 public void Apply(Guid generation,KscBatch b,IReadOnlyDictionary<int,string> devices,List<NetworkNotification> queue,DateTime now)=>Apply(generation,b,devices,queue,now,false);
 public void ApplyStandalone(Guid generation,KscBatch b,List<NetworkNotification> queue,DateTime now)=>Apply(generation,b,null,queue,now,true);
 private void Apply(Guid generation,KscBatch b,IReadOnlyDictionary<int,string>? devices,List<NetworkNotification> queue,DateTime now,bool standalone){
  if(Generation!=generation)throw new InvalidOperationException("Источник KSC сменился");
  // Validate the entire page before changing incidents or the notification queue.
  // A partial/reordered page must never advance the durable cursor.
  if(b.Rows==null||b.Rows.Length>128||b.Cursor<0)throw new InvalidDataException("KSC: неверная страница событий");
  long previous=0;
  foreach(var e in b.Rows){
   if(e==null||e.Id<=previous||e.Id>b.Cursor||string.IsNullOrWhiteSpace(e.Kind)||e.Host==null)
    throw new InvalidDataException("KSC: нарушен порядок или формат страницы событий");
   previous=e.Id;
  }
  if(b.Rows.Length>0&&previous!=b.Cursor)throw new InvalidDataException("KSC: курсор не соответствует последнему событию");
  foreach(var e in b.Rows.Where(e=>e.Id>Position)){
   int id=0;string? name=e.Host.Length==0?"Сервер администрирования":e.Computer;
   if(!standalone&&(!Hosts.TryGetValue(e.Host,out id)||!devices!.TryGetValue(id,out name)))continue;
   string source="KSC · "+name;
   foreach(var ended in Open.Where(p=>p.Source==generation.ToString("N")&&p.DeviceId==id&&p.Host==e.Host&&p.Recovery!=""&&p.Recovery==e.Kind).ToArray()){
    Close(ended,now,e.Kind);if(ended.Notify)queue.Add(new(){EntityKey=ended.Key,DeviceId=standalone?null:id,Topic="ksc.security",Severity=ended.Severity,Source=source,Title=ended.Title+" · "+e.Kind,Transition="ЗАКРЫТО / ВОССТАНОВЛЕНО",CreatedAt=now});
   }
   if(e.RdpBruteForce){
    var key="ksc:"+generation.ToString("N")+":"+id+":"+AgentWire.Hash(e.Host)+":rdp-"+AgentWire.Hash(e.AttackerIp??"unknown");
    var old=Open.Find(p=>p.Key.StartsWith(key+":",StringComparison.Ordinal));
    if(old!=null)Open.Remove(old);
    var attack=old==null
      ?new KscIssue(key+":"+e.Id,id,generation.ToString("N"),e.Host,"Подбор пароля RDP",Severity.Warning,"",true,1,now,now){AttackerIp=e.AttackerIp,EventCode=e.Kind,ComputerName=e.ComputerName,LastEventId=e.Id,LatestEvent=e}
      :old with{Count=old.Count+1,LastUtc=now,LastEventId=e.Id,LatestEvent=e};
    Open.Add(attack);
    if(old==null&&!e.IpBlocked)queue.Add(new(){EntityKey=attack.Key,DeviceId=standalone?null:id,Topic="ksc.security",Severity=attack.Severity,Source=source,Title=attack.Title+" · "+e.AttackerIp,CreatedAt=now});
   }
   if(e.IpBlocked&&e.AttackerIp is {} ip){
    foreach(var blocked in Open.Where(p=>p.Source==generation.ToString("N")&&p.DeviceId==id&&p.Host==e.Host&&p.AttackerIp==ip).ToArray()){
     Close(blocked,now,"KSC подтвердил блокировку IP "+ip);
     if(blocked.Notify)queue.Add(new(){EntityKey=blocked.Key,DeviceId=standalone?null:id,Topic="ksc.security",Severity=blocked.Severity,Source=source,Title=blocked.Title+" · IP заблокирован: "+ip,Transition="ЗАКРЫТО / ВОССТАНОВЛЕНО",CreatedAt=now});
    }
   }
   if(e.RdpBruteForce)continue;
   foreach(var r in Rules.Where(r=>r.Enabled&&r.OpenEvent==e.Kind)){
    var key="ksc:"+generation.ToString("N")+":"+id+":"+AgentWire.Hash(e.Host)+":"+r.Id;
    var i=Open.FindIndex(p=>p.Key.StartsWith(key+":",StringComparison.Ordinal));if(i>=0){var p=Open[i];Open[i]=p with{Count=p.Count+1,LastUtc=now,EventCode=e.Kind,LastEventId=e.Id,LatestEvent=e};continue;}
    key+=":"+e.Id;Open.Add(new(key,id,generation.ToString("N"),e.Host,r.Id=="virus-found"&&r.Title=="Обнаружен вирус"?"Срабатывание защиты":r.Title,r.Level,r.RecoveryEvent,r.Notify,1,now,now){EventCode=e.Kind,LastEventId=e.Id,LatestEvent=e});
    if(r.Notify)queue.Add(new(){EntityKey=key,DeviceId=standalone?null:id,Topic="ksc.security",Severity=r.Level,Source=source,Title=(r.Id=="virus-found"&&r.Title=="Обнаружен вирус"?e.EventName:r.Title)+" · "+e.Kind,CreatedAt=now});
   }
  }
  Position=Math.Max(Position,b.Cursor);
 }
}
