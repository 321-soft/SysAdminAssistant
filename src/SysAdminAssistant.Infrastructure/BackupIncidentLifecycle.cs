using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record IncidentGroupRow(Incident Incident,int[] IncidentIds,int Count,DateTime FirstAt,DateTime LastAt,string Key)
{
 public DateTime CreatedAt=>LastAt;public Severity Severity=>Incident.Severity;public string Source=>Incident.Source;public string Title=>Incident.Title;public bool Closed=>Incident.Closed;
 public string Details=>$"Повторов: {Count}; первое: {FirstAt:dd.MM.yyyy HH:mm:ss}; последнее: {LastAt:dd.MM.yyyy HH:mm:ss}.\nИстория исходных писем и результатов запусков сохранена.\n"+Incident.Details;
}
public static class BackupIncidentLifecycle
{
 public static bool ParsedBackup(ReportEvent r)=>!r.IsPending&&r.Result is BackupResult.Success or BackupResult.Warning or BackupResult.Failed;
 public static bool Fault(Incident i)=>i.Severity is Severity.Warning or Severity.Critical;
 public static string Key(SqlBackupJob job,ReportEvent r){
  // Strip only timestamps: paths, error codes and distinct causes remain significant.
  var text=Regex.Replace(r.Provider+"|"+r.Task+"|"+r.Description,@"\b(?:\d{4}-\d{2}-\d{2}|\d{2}[./]\d{2}[./]\d{4})(?:[ T]\d{2}:\d{2}(?::\d{2})?)?\b|\b\d{2}:\d{2}:\d{2}\b","<time>",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
  text=Regex.Replace(text,@"\s+"," ",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
  var description=r.Description.ToLowerInvariant();
  if(description.Contains("0x800700ce",StringComparison.Ordinal)||description.Contains("too long",StringComparison.Ordinal)&&(description.Contains("path",StringComparison.Ordinal)||description.Contains("file name",StringComparison.Ordinal)||description.Contains("filename",StringComparison.Ordinal))||description.Contains("слишком длин",StringComparison.Ordinal)&&(description.Contains("путь",StringComparison.Ordinal)||description.Contains("имя",StringComparison.Ordinal)||description.Contains("файл",StringComparison.Ordinal)))text=r.Provider+"|"+r.Task+"|path-too-long";

  return "backup-incident:"+job.Id+":"+BackupSchedule.ReportSlot(job,r)+":"+RemoteData.Hash((r.DeviceId??job.DeviceId)+"|"+r.Result+"|"+text);
 }
 public static IncidentGroupRow[] Group(IEnumerable<Incident> incidents,IEnumerable<ReportEvent> reports,IEnumerable<SqlBackupJob> jobs){
  var byReport=reports.ToDictionary(r=>r.Id);var byJob=jobs.ToDictionary(j=>j.Id);
  string Identity(Incident i)=>!i.Closed&&Fault(i)&&i.ReportId is int id&&byReport.TryGetValue(id,out var r)&&ParsedBackup(r)&&r.JobId is int jobId&&byJob.TryGetValue(jobId,out var j)?Key(j,r):"incident:"+i.Id;
  return incidents.GroupBy(Identity).Select(g=>{var rows=g.OrderByDescending(i=>i.CreatedAt).ThenByDescending(i=>i.Id).ToArray();var representative=rows.OrderByDescending(i=>i.Severity).ThenByDescending(i=>i.CreatedAt).First();return new IncidentGroupRow(representative,rows.Select(i=>i.Id).ToArray(),rows.Length,rows.Min(i=>i.CreatedAt),rows.Max(i=>i.CreatedAt),g.Key);}).OrderBy(g=>g.Closed).ThenByDescending(g=>g.LastAt).ToArray();
 }
 public static void CoalescePending(NetworkState state){
  foreach(var group in state.Notifications.Where(n=>n.HandledAt==null&&!n.Transition.StartsWith("ЗАКРЫТО",StringComparison.Ordinal)&&(n.EntityKey.StartsWith("backup-incident:",StringComparison.Ordinal)||n.EntityKey.StartsWith("schedule:",StringComparison.Ordinal))).GroupBy(n=>n.EntityKey)){
   var latest=group.OrderByDescending(n=>n.CreatedAt).ThenByDescending(n=>n.Id,StringComparer.Ordinal).First();latest.Severity=group.Max(n=>n.Severity);foreach(var prior in group.Where(n=>n.Id!=latest.Id)){prior.HandledAt=DateTime.UtcNow;prior.Result="Объединено с актуальным уведомлением о проблеме бэкапа";}
  }
 }
 public static async Task<HashSet<string>> ReconcileAsync(AssistantDb db,IReadOnlyList<ReportEvent> reports,IReadOnlyList<SqlBackupJob> jobs,DateTime now,CancellationToken token){
  var byReport=reports.ToDictionary(r=>r.Id);var byJob=jobs.ToDictionary(j=>j.Id);var incidents=await db.Incidents.Where(i=>!i.Closed&&i.ReportId!=null&&(i.Severity==Severity.Warning||i.Severity==Severity.Critical)).ToArrayAsync(token);var recovered=new HashSet<string>();bool changed=false;
  foreach(var i in incidents){if(!byReport.TryGetValue(i.ReportId!.Value,out var bad)||bad.JobId is not int jid||!ParsedBackup(bad)||!byJob.TryGetValue(jid,out var job))continue;
   var badAt=bad.OccurredAt??bad.ReceivedAt;if(badAt>now)continue;var slot=BackupSchedule.ReportSlot(job,bad);
   // Close only older failures of the SAME scheduled run after an actually parsed success.
   if(reports.Any(r=>r.JobId==jid&&(r.DeviceId??job.DeviceId)==(bad.DeviceId??job.DeviceId)&&!r.IsPending&&r.Result==BackupResult.Success&&r.Severity is not(Severity.Warning or Severity.Critical or Severity.Unknown)&&(r.OccurredAt??r.ReceivedAt)<=now&&((r.OccurredAt??r.ReceivedAt)>badAt||(r.OccurredAt??r.ReceivedAt)==badAt&&r.Id>bad.Id)&&BackupSchedule.ReportSlot(job,r)==slot)){recovered.Add(Key(job,bad));i.Closed=true;changed=true;}
  }
  if(changed)await db.SaveChangesAsync(token);return recovered;
 }
}
