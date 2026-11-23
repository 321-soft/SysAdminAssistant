using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
internal sealed class TelegramWorker(ServerState state, KscMonitor ksc, ILogger<TelegramWorker> log) : BackgroundService
{
    public sealed class Cursor { public int LastId { get; set; } public string Destination { get; set; } = ""; }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var path = Path.Combine(state.DirectoryPath, "telegram-cursor.json");
        Cursor? cursor = null;
        var nextSummaryObservation=DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(10);
            try
            {
                // Load once; malformed state fails closed instead of resending the history.
                if (cursor == null && File.Exists(path)) cursor = JsonSerializer.Deserialize<Cursor>(await File.ReadAllTextAsync(path, token)) ?? throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Пустая позиция Telegram"));
                using var timeScope=SysAdminAssistant.Core.AppTime.Scope(state.Settings.TimeZoneId);using var languageScope=SysAdminAssistant.Core.ProductText.Scope(state.Settings.NotificationLanguage);
                var settings = JsonSerializer.Deserialize<TelegramSettings>(JsonSerializer.Serialize(state.Settings.Telegram))!;
                if(state.Settings.DailySummary?.Enabled==true&&DateTime.UtcNow>=nextSummaryObservation){nextSummaryObservation=DateTime.UtcNow.AddMinutes(1);try{await DailySummaryCollector.ObserveAsync(state,token);await new DailySummaryStore(state.DirectoryPath).SendDueAsync(state.Settings.DailySummary,settings,state.Settings.TimeZoneId,DateTime.UtcNow,token);}catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}catch(Exception e){new DailySummaryStore(state.DirectoryPath).Failed();log.LogWarning("Ежедневная сводка: ошибка ({Kind}); обычные уведомления продолжают работать",e.GetType().Name);}}
                await state.Certificates.SendAsync(settings,state.ServerUrl,state.Settings.TimeZoneId,token);
                await using var db = state.Factory.Create();
                var devices=await db.Devices.AsNoTracking().ToDictionaryAsync(d=>d.Id,token);var jobs=await db.Jobs.AsNoTracking().ToDictionaryAsync(j=>j.Id,token);
                Func<NetworkNotification,bool> kscFilter;
                try{kscFilter=ksc.TelegramFilter();}
                catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){log.LogWarning("KSC Telegram: настройки недоступны ({Kind})",e.GetType().Name);kscFilter=_=>false;}
                await NetworkNotifications.FlushAsync(state.Routers.Store,settings,token,subject:n=>NotificationSubjects.Network(n,devices,jobs),kscFilter:kscFilter);
                var destination = settings.Enabled ? settings.ChatId + ":" + settings.ProtectedToken : "";
                if (cursor == null || cursor.Destination != destination || !settings.Enabled)
                {
                    cursor = new Cursor { LastId = await db.Incidents.MaxAsync(x => (int?)x.Id, token) ?? 0, Destination = destination };
                    await Save();
                }
                else
                {
                    var rows = await db.Incidents.AsNoTracking().Where(x => x.Id > cursor.LastId).OrderBy(x => x.Id).Take(20).ToArrayAsync(token);
                    foreach (var row in rows)
                    {
                        SysAdminAssistant.Core.ReportEvent? reportContext=null;
                        var topic=TelegramTopics.Infer("",row.Source,row.Title,row.Severity);
                        if(row.ReportId is int reportId){var report=await db.Reports.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==reportId,token);reportContext=report;if(report!=null)topic=report.Provider=="DMARC"?"mail.dmarc":report.JobId!=null?row.Severity==SysAdminAssistant.Core.Severity.Critical?"backup.failure":"backup.warning":"mail.device";}
                        if (settings.AcceptsEvent(row.Severity,topic) && !row.Closed && row.Source!="ICMP" && !(reportContext?.JobId!=null&&BackupIncidentLifecycle.ParsedBackup(reportContext)&&BackupIncidentLifecycle.Fault(row)))
                        {
                            await new TelegramMessageThreads(state.Routers.Store.DirectoryPath).SendAsync(settings,"incident:"+row.Id,"incident:"+row.Id,false,TelegramSender.Format(row,NotificationSubjects.Incident(row,reportContext,devices,jobs)),token);
                            await Task.Delay(1100, token);
                        }
                        cursor.LastId = row.Id; await Save();
                    }
                }
                if(settings.Enabled&&settings.TopicEnabled("recovery")){
                    var threads=new TelegramMessageThreads(state.Routers.Store.DirectoryPath);var opened=threads.OpenIssues(settings).Where(k=>k.StartsWith("incident:",StringComparison.Ordinal)).Select(k=>int.TryParse(k[9..],out var id)?id:0).Where(id=>id>0).ToArray();
                    if(opened.Length>0)foreach(var row in await db.Incidents.AsNoTracking().Where(i=>i.Closed&&opened.Contains(i.Id)).OrderBy(i=>i.Id).Take(20).ToArrayAsync(token)){
                        var report=row.ReportId is int rid?await db.Reports.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==rid,token):null;var topic=TelegramTopics.Infer("",row.Source,row.Title,row.Severity);if(!settings.AcceptsEvent(row.Severity,topic,true))continue;
                        var recovery=new NetworkNotification{Source=row.Source,Title=row.Title+" · событие отмечено решённым",Severity=row.Severity,Transition="ЗАКРЫТО / ВОССТАНОВЛЕНО"};
                        await threads.SendAsync(settings,"incident:"+row.Id+":closed","incident:"+row.Id,true,NetworkNotifications.Format(recovery,NotificationSubjects.Incident(row,report,devices,jobs)),token);await Task.Delay(1100,token);
                    }
                }
                state.TelegramError = "";
                async Task Save()
                {
                    var temp = path + ".tmp"; await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(cursor), token); File.Move(temp, path, true);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) { cursor = null; state.TelegramError = "Telegram: отправка не выполнена; повторная попытка через минуту. Проверьте настройки и доступ к интернету."; state.Touch(); log.LogWarning("Telegram: уведомление не отправлено ({Kind}); повтор через минуту", e.GetType().Name); delay = TimeSpan.FromSeconds(60); }
            try { await Task.Delay(delay, token); } catch (OperationCanceledException) { break; }
        }
    }
}
