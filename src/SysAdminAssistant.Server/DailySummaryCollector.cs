using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public static class DailySummaryCollector {
 public static async Task ObserveAsync(ServerState state,CancellationToken token)=>new DailySummaryStore(state.DirectoryPath).Observe(await CollectAsync(state,token),DateTime.UtcNow);
 public static async Task<SummaryProblem[]> CollectAsync(ServerState state,CancellationToken token){
  await using var db=state.Factory.Create();var problems=new List<SummaryProblem>();
  var jobRows=await db.Jobs.AsNoTracking().ToArrayAsync(token);var reportRows=await db.Reports.AsNoTracking().Where(r=>r.JobId!=null).ToArrayAsync(token);var recovered=await BackupIncidentLifecycle.ReconcileAsync(db,reportRows,jobRows,AppTime.Now,token);if(recovered.Count>0)state.Touch();
  var currentIncidents=await db.Incidents.AsNoTracking().Where(i=>!i.Closed).ToArrayAsync(token);var backupGroups=BackupIncidentLifecycle.Group(currentIncidents,reportRows,jobRows).Where(g=>g.Key.StartsWith("backup-incident:",StringComparison.Ordinal)).ToArray();
  var jobReportIds=reportRows.Select(r=>r.Id).ToHashSet();
  foreach(var i in await db.Incidents.AsNoTracking().Where(i=>!i.Closed&&i.Source!="ICMP").ToArrayAsync(token))if(i.ReportId==null||!jobReportIds.Contains(i.ReportId.Value))problems.Add(new("incident:"+i.Id,"mail",i.Severity,i.Source,i.Title));
  var devices=await db.Devices.AsNoTracking().ToDictionaryAsync(d=>d.Id,token);
  state.Routers.Store.State(s=>{
   foreach(var h in s.Histories){if(!devices.TryGetValue(h.DeviceId,out var device)||!device.Enabled||device.MaintenanceUntil>AppTime.Now)continue;var name=devices.GetValueOrDefault(h.DeviceId)?.Name??("Устройство #"+h.DeviceId);foreach(var p in h.Problems.Where(p=>p.Open))problems.Add(new("network:"+p.Id,"routers",p.Severity,name,p.Title));
    foreach(var p in h.Audit.Result?.DisplayFindings.Where(p=>!p.Ignored&&p.Severity is Severity.Warning or Severity.Critical)??[])problems.Add(new("audit:"+h.DeviceId+":"+p.Key,"routers",p.Severity,name,p.Title));}
   foreach(var p in s.ApplicationStates.Where(p=>p.Value.StartsWith("bad|")&&p.Key=="mail-intake")){var n=s.Notifications.LastOrDefault(n=>n.EntityKey==p.Key);if(n!=null)problems.Add(new(p.Key,"mail",n.Severity,n.Source,n.Title));}
   foreach(var p in s.AvailabilityAlerts.Where(p=>p.Value.NotificationId!=null&&!p.Key.StartsWith("agent:")&&!p.Key.StartsWith("agent-link:"))){var n=s.Notifications.LastOrDefault(n=>n.Id==p.Value.NotificationId);problems.Add(new(p.Key,"availability",n?.Severity??Severity.Warning,n?.Source??"Доступность",n?.Title??("Проверка не проходит: "+p.Key)));}return true;
  });
  var now=AppTime.Now;
  foreach(var p in await db.Payments.AsNoTracking().Where(p=>!p.Paid).ToArrayAsync(token)){var due=PaymentCalendar.EffectiveDueDate(p.DueDate,state.Settings.Company.WeekendMask);if(due<now.Date||PaymentCalendar.WorkingDaysUntil(now,due,state.Settings.Company.WeekendMask)<=state.Settings.Company.PaymentSoonDays)problems.Add(new("payment:"+p.Id,"payments",due<now.Date?Severity.Critical:Severity.Warning,"Счета · "+p.Title,"Срок оплаты "+due.ToString("dd.MM.yyyy")));}
  var reports=reportRows;
  foreach(var g in backupGroups){var report=reportRows.First(r=>r.Id==g.Incident.ReportId);var job=jobRows.First(j=>j.Id==report.JobId);if(!job.Active)continue;var device=devices.GetValueOrDefault(job.DeviceId??0);problems.Add(new(g.Key,"backup",g.Severity,(device?.Name??job.Host)+" · "+job.Title,g.Title+" · повторов: "+g.Count));}
  foreach(var job in jobRows.Where(j=>j.Active)){foreach(var row in BackupQueries.RunRows(job,reports,now)){var lamp=row.Schedule;if(lamp.State is LampState.Red or LampState.Amber){var hasFault=backupGroups.Any(g=>g.Key.StartsWith("backup-incident:"+job.Id+":"+row.Slot+":",StringComparison.Ordinal));if(!hasFault||lamp.Status=="Нет отчёта"||lamp.Status.StartsWith("Результат не распознан",StringComparison.Ordinal))problems.Add(new("backup:"+job.Id+":"+row.Slot,"backup",lamp.State==LampState.Red?Severity.Critical:Severity.Warning,(devices.GetValueOrDefault(job.DeviceId??0)?.Name??job.Host)+" · "+lamp.Name,lamp.Status));}}}
  foreach(var a in state.Agents.Active()){var name=devices.GetValueOrDefault(a.DeviceId)?.Name??a.MachineName;var full=state.Agents.Get(a.DeviceId);if(full==null)continue;
   foreach(var f in state.EventStore(a.DeviceId).Findings(a.DeviceId))problems.Add(new("agent:"+a.DeviceId+":"+f.Key,"agents",f.Severity,name,f.Title));
   if(full.LastSeenUtc==null||full.LastSeenUtc<DateTime.UtcNow.AddMinutes(-6)){problems.Add(new("agent-link:"+a.DeviceId,"agents",Severity.Warning,name,"Нет свежих данных агента"));}
   foreach(var f in full.Findings.GroupBy(f=>f.Key).Select(g=>g.First()))problems.Add(new("agent:"+a.DeviceId+":"+f.Key,"agents",f.Severity,name,f.Title));
  }
  return problems.GroupBy(p=>p.Key).Select(g=>g.OrderByDescending(p=>p.Severity).First()).ToArray();
 }
}
