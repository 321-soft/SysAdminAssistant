using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public static class ApplicationNotifications
{
    public static async Task CaptureAsync(DatabaseFactory factory,NetworkStore store,AppSettings settings,DateTime now,CancellationToken token,IReadOnlyList<string>? mailErrors=null)
    {
        await using var db=factory.Create();
        var devices=await db.Devices.AsNoTracking().ToArrayAsync(token);
        var services=await db.Services.AsNoTracking().ToArrayAsync(token);
        var connectivity=await db.Connectivity.AsNoTracking().FirstOrDefaultAsync(token);
        var payments=await db.Payments.AsNoTracking().ToArrayAsync(token);
        var jobs=await db.Jobs.AsNoTracking().ToArrayAsync(token);
        var reports=await db.Reports.AsNoTracking().Where(r=>r.JobId!=null).ToArrayAsync(token);
        var recoveredBackups=await BackupIncidentLifecycle.ReconcileAsync(db,reports,jobs,now,token);
        var openIncidents=await db.Incidents.AsNoTracking().Where(i=>!i.Closed).ToArrayAsync(token);
        var backupGroups=BackupIncidentLifecycle.Group(openIncidents,reports,jobs).Where(g=>g.Key.StartsWith("backup-incident:",StringComparison.Ordinal)).ToArray();
        store.State(state=>{
            MailUpdateStore.Capture(state,devices);
            void Transition(string key,string fingerprint,bool bad,Severity severity,string source,string title,string recovery,bool workingDaysOnly=false,int? deviceId=null)
            {
                var old=state.ApplicationStates.GetValueOrDefault(key,"");var next=(bad?"bad|":"ok|")+fingerprint;
                if(next==old)return;state.ApplicationStates[key]=next;
                if(workingDaysOnly||!bad&&key.StartsWith("backup-incident:",StringComparison.Ordinal))foreach(var pending in state.Notifications.Where(n=>n.EntityKey==key&&n.HandledAt==null)){pending.HandledAt=DateTime.UtcNow;pending.Result=workingDaysOnly?"Состояние платежа изменилось":"Проблема бэкапа закрыта до отправки";}
                if(bad||old.StartsWith("bad|",StringComparison.Ordinal))state.Notifications.Add(new(){DeviceId=deviceId,Topic=key.StartsWith("backup-incident:",StringComparison.Ordinal)?severity==Severity.Critical?"backup.failure":"backup.warning":"",EntityKey=key,WorkingDaysOnly=workingDaysOnly,WeekendMask=settings.Company.WeekendMask,Source=source,Title=bad?title:recovery,Severity=severity,Transition=bad?"ПРОБЛЕМА":"ЗАКРЫТО / ВОССТАНОВЛЕНО"});
            }
            var activeBackups=new HashSet<string>();
            foreach(var g in backupGroups){activeBackups.Add(g.Key);var r=reports.First(r=>r.Id==g.Incident.ReportId);var job=jobs.First(j=>j.Id==r.JobId);var device=devices.FirstOrDefault(d=>d.Id==(r.DeviceId??job.DeviceId));var source="Резервное копирование · "+job.Title+" · "+(device?.Name??job.Host)+(string.IsNullOrWhiteSpace(job.Host)?"":" · "+job.Host);
                Transition(g.Key,g.Severity.ToString(),true,g.Severity,source,g.Title+"\nПовторов: "+g.Count+"; первое: "+g.FirstAt.ToString("dd.MM.yyyy HH:mm:ss")+"; последнее: "+g.LastAt.ToString("dd.MM.yyyy HH:mm:ss"),"Подтверждено восстановление задания",deviceId:r.DeviceId??job.DeviceId);
                foreach(var pending in state.Notifications.Where(n=>n.EntityKey==g.Key&&n.HandledAt==null))pending.Title=g.Title+"\nПовторов: "+g.Count+"; первое: "+g.FirstAt.ToString("dd.MM.yyyy HH:mm:ss")+"; последнее: "+g.LastAt.ToString("dd.MM.yyyy HH:mm:ss");
            }
            foreach(var prior in state.ApplicationStates.Where(p=>p.Key.StartsWith("backup-incident:",StringComparison.Ordinal)&&p.Value.StartsWith("bad|",StringComparison.Ordinal)&&!activeBackups.Contains(p.Key)).ToArray()){
                var opening=state.Notifications.LastOrDefault(n=>n.EntityKey==prior.Key);Transition(prior.Key,"closed",false,opening?.Severity??Severity.Warning,opening?.Source??"Резервное копирование","",recoveredBackups.Contains(prior.Key)?"Подтверждено восстановление: получен более поздний успешный отчёт того же запуска. Прежние неудачи сохранены в истории.":"Проблема больше не числится активной; результаты прежних запусков сохранены в истории",deviceId:opening?.DeviceId);
            }
            if(mailErrors!=null)Transition("mail-intake",mailErrors.Count>0?"down":"up",mailErrors.Count>0,Severity.Warning,"Почта","Есть ошибки проверки IMAP: "+mailErrors.Count+".\nЧто проверить: откройте диагностику ящиков, проверьте IMAP, сеть, TLS, учётную запись/пароль приложения и права чтения. Не отключайте TLS и не присылай пароль в журнал. Состояние восстановится после успешной проверки.","Проверка почты снова проходит");
            var activeAvailability=new HashSet<string>();
            void Availability(string key,bool bad,DateTime observed,Severity severity,string source,string title,string recovery)
            {
                activeAvailability.Add(key);
                AvailabilityAlerts.Observe(state,key,bad,observed,settings.AvailabilityAlertMinutes,settings.PollSeconds,severity,source,title,recovery);
            }
            if(settings.MonitoringEnabled){
                foreach(var d in devices.Where(d=>d.Enabled&&d.MaintenanceUntil<=now||d.Enabled&&d.MaintenanceUntil==null)){
                    if(d.AgentLastSeen is DateTime agentSeen && agentSeen<=now && now-agentSeen<TimeSpan.FromMinutes(6) && d.AgentHealth is "OK" or "WARNING" or "CRITICAL"){
                        Availability("availability:device:"+d.Id+":"+RemoteData.Hash(d.Host),false,agentSeen,Severity.Warning,"Устройства · "+d.Name,"","Связь с устройством подтверждена агентом");continue;
                    }
                    if(d.CheckedAt==null||d.CheckedAt>now||now-d.CheckedAt>TimeSpan.FromMinutes(10)||d.Status is not("ONLINE" or "OFFLINE"))continue;
                    Availability("availability:device:"+d.Id+":"+RemoteData.Hash(d.Host),d.Status=="OFFLINE",d.CheckedAt.Value,Severity.Warning,"Устройства · "+d.Name,"Устройство недоступно\n"+AvailabilityIssueAdvice.Device(d,settings.AvailabilityAlertMinutes),"Устройство снова доступно");
                }
                if(connectivity!=null&&connectivity.CheckedAt<=now&&now-connectivity.CheckedAt<=TimeSpan.FromMinutes(10)){
                    Availability("availability:wan:"+RemoteData.Hash(settings.PingHost),connectivity.Ping!="OK",connectivity.CheckedAt,Severity.Warning,"Сеть сервера · WAN",(connectivity.Ping=="PARTIAL"?"Часть внешних ICMP-адресов недоступна":"Нет ответа от внешних ICMP-адресов")+"\n"+AvailabilityIssueAdvice.Group("WAN",connectivity,settings.AvailabilityAlertMinutes),"Все WAN-проверки снова проходят");
                    if(connectivity.Lan!="DISABLED")Availability("availability:lan:"+RemoteData.Hash(settings.LanPingHosts),connectivity.Lan!="OK",connectivity.CheckedAt,Severity.Warning,"Сеть сервера · LAN",(connectivity.Lan=="PARTIAL"?"Часть локальных ICMP-адресов недоступна":"Нет ответа от локальных ICMP-адресов")+"\n"+AvailabilityIssueAdvice.Group("LAN",connectivity,settings.AvailabilityAlertMinutes),"Все LAN-проверки снова проходят");
                    Availability("availability:dns:"+RemoteData.Hash(settings.DnsName),connectivity.Dns!="OK",connectivity.CheckedAt,Severity.Warning,"Сеть сервера","Не проходит проверка DNS\n"+AvailabilityIssueAdvice.Group("DNS",connectivity,settings.AvailabilityAlertMinutes),"Проверка DNS снова проходит");
                }
                foreach(var s in services){
                    if(s.CheckedAt==null||s.CheckedAt>now||now-s.CheckedAt>TimeSpan.FromMinutes(10)||s.Status=="UNKNOWN")continue;
                    Availability("availability:service:"+s.Id+":"+RemoteData.Hash(s.Type+":"+s.Address),s.Status!="ONLINE",s.CheckedAt.Value,Severity.Critical,"Сервисы · "+s.Name,"Проверка сервиса не проходит: "+s.Status+"\n"+AvailabilityIssueAdvice.Service(s,settings.AvailabilityAlertMinutes),"Сервис снова доступен");
                }
            }
            AvailabilityAlerts.Retain(state,activeAvailability,now);
            if(PaymentCalendar.IsWorkingDay(now,settings.Company.WeekendMask))foreach(var p in payments){
                var due=PaymentCalendar.EffectiveDueDate(p.DueDate,settings.Company.WeekendMask);
                var bad=!p.Paid&&(due<=now.Date||PaymentCalendar.WorkingDaysUntil(now,due,settings.Company.WeekendMask)<=settings.Company.PaymentSoonDays);
                var category=p.Paid?"paid":due<now.Date?"overdue":due==now.Date?"today":bad?"soon":"later";
                Transition("payment:"+p.Id,due.ToString("yyyyMMdd")+":"+category,bad,due<now.Date?Severity.Critical:Severity.Warning,"Счета · "+p.Title,(due<now.Date?"Платёж просрочен":due==now.Date?"Оплатить сегодня":"Приближается платёж")+" · срок "+due.ToString("dd.MM.yyyy"),"Платёж отмечен или срок перенесён",true);
            }
            foreach(var job in jobs.Where(j=>j.Active)){
                var times=BackupSchedule.Deadlines(job);var slots=BackupSchedule.EvaluateSlots(job,reports,now);
                for(int i=0;i<slots.Count;i++){
                    var lamp=slots[i];var missing=lamp.State==LampState.Red&&(lamp.Status=="Нет отчёта"||lamp.Status.StartsWith("Результат не распознан",StringComparison.Ordinal));
                    var due=Enumerable.Range(0,9).Select(n=>now.Date.AddDays(-n).AddMinutes(times[i])).Where(d=>d<=now&&(job.Days&(1<<(((int)d.DayOfWeek+6)%7)))!=0).FirstOrDefault();
                    Transition("schedule:"+job.Id+":"+times[i],missing?lamp.Status:"ready",missing,Severity.Critical,"Резервное копирование · "+lamp.Name+" · "+(devices.FirstOrDefault(d=>d.Id==job.DeviceId)?.Name??job.Host),lamp.Status,"Контроль отчёта восстановлен / ожидание следующего запуска");
                }
            }
            return true;
        },true);
    }
}
