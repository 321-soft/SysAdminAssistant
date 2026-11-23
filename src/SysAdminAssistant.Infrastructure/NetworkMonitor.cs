using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed class NetworkMonitor(NetworkStore store) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate=new(1);
    private readonly RouterSessionPool sessions=new();
    private readonly RouterUpdateCatalog updates=new();
    public ValueTask DisposeAsync()=>sessions.DisposeAsync();
    public RouterSessionPool Sessions=>sessions;
    public NetworkStore Store=>store;
    public async Task<RouterMonitorRow[]> ListAsync(DatabaseFactory factory,CancellationToken token=default)
    {
        await using var db=factory.Create();var devices=await db.Devices.AsNoTracking().Where(d=>d.Type=="MikroTik"||d.Type=="Коммутатор").ToArrayAsync(token);
        return store.State(state=>devices.Select(d=>{
            var h=state.Histories.SingleOrDefault(h=>h.DeviceId==d.Id)??new(){DeviceId=d.Id};if(h.Host!=d.Host)h=new(){DeviceId=d.Id,Audit=h.Audit.Host==d.Host?h.Audit:new()};var configured=h.Configured||d.Type=="Коммутатор";
            var stale=h.CheckedAt==null||(DateTime.UtcNow-h.CheckedAt.Value).TotalMinutes>10;
            var color=d.MaintenanceUntil>AppTime.Now||!d.Enabled?LampState.Gray:d.Type=="Коммутатор"?(d.CheckedAt==null||AppTime.Now-d.CheckedAt>TimeSpan.FromMinutes(10)?LampState.Gray:d.Status=="ONLINE"?LampState.Green:d.Status=="OFFLINE"?LampState.Red:LampState.Gray):NetworkSignals.Color(h.Problems,configured,h.Error.Length==0,stale);
            color=RouterAuditStore.Color(color,h.Audit,d.Type=="MikroTik"&&d.Enabled&&(d.MaintenanceUntil==null||d.MaintenanceUntil<=AppTime.Now));
            return new RouterMonitorRow(d.Id,d.Name,d.Host,h.CheckedAt?.ToServerTime(),h.Snapshot,h.Error,h.Problems.Where(p=>p.Open).ToArray(),configured,color,d.Type,MailUpdates.Overview(d.OsUpdatesJson,d.MailUpdatesJson),d.MailUpdatesJson,h.Audit.Result?.Critical??0,h.Audit.Result?.Warnings??0,h.Audit.Result?.CoverageLabel??"конфигурация ещё не проверена",h.Audit.Result?.Findings.Count(f=>f.Ignored)??0);
        }).OrderBy(r=>r.State==LampState.Red?0:r.State==LampState.Amber?1:r.State==LampState.Gray?2:3).ThenBy(r=>r.Name).ToArray());
    }
    public async Task CollectAsync(DatabaseFactory factory,RouterSettings legacy,CancellationToken token=default,Func<RouterSettings,CancellationToken,Task<RouterSnapshot>>? read=null,int availabilityDelaySeconds=0)
    {
        if(!await gate.WaitAsync(0,token))return;
        try{
            await using var db=factory.Create();var devices=await db.Devices.AsNoTracking().Where(d=>d.Enabled&&d.Type=="MikroTik").ToArrayAsync(token);var activeIds=devices.Select(d=>d.Id).ToHashSet();store.State(s=>{foreach(var h in s.Histories.Where(h=>!activeIds.Contains(h.DeviceId))){var prefix="router-audit:"+h.DeviceId+":";foreach(var n in s.Notifications.Where(n=>n.EntityKey.StartsWith(prefix)&&n.HandledAt==null)){n.HandledAt=DateTime.UtcNow;n.Result="Устройство выключено или удалено";}foreach(var key in s.ApplicationStates.Keys.Where(k=>k.StartsWith(prefix)).ToArray())s.ApplicationStates.Remove(key);}return true;},true);var dictionaries=store.Dictionaries();await sessions.RetainAsync(devices.Select(d=>d.Id));
            await Parallel.ForEachAsync(devices,new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=token},async(device,ct)=>{
                var saved=store.MonitorConnection(device.Id);var config=saved??new NetworkConnection{DeviceId=device.Id,Port=legacy.Port,User=legacy.User,ProtectedPassword=legacy.ProtectedPassword,CertificateSha256=legacy.CertificateSha256};
                if(config.User.Length==0){await sessions.RemoveAsync(device.Id);return;}
                var dictionary=dictionaries.SingleOrDefault(d=>d.Id==config.DictionaryId)??NetworkDictionaries.Generic();
                var rules=NetworkLogRules.Effective(dictionary,config.Interfaces,config.Rules);
                var effective=new NetworkDictionary{Id=dictionary.Id,Name=dictionary.Name,Rules=rules};
                var startedUtc=DateTime.UtcNow;RouterSnapshot? snapshot=null;string error="";using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(40));
                try{var target=NetworkStore.Target(device.Host,saved,legacy);if(read!=null)snapshot=await read(target,budget.Token);else snapshot=await sessions.ReadAsync(device.Id,target,ct);}
                catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
                catch(OperationCanceledException){error="MikroTik: превышено время опроса";}
                catch(Exception e)when(e is InvalidOperationException or ArgumentException or IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or CryptographicException or FormatException){error=RouterMonitor.ErrorText(e);}
                // Ignore a response for an address changed while the request was in flight.
                await using(var current=factory.Create())if(!await current.Devices.AnyAsync(d=>d.Id==device.Id&&d.Enabled&&d.Host==device.Host&&d.Type=="MikroTik",ct))return;
                var now=DateTime.UtcNow;
                store.State(state=>{
                    var currentConfig=state.Connections.SingleOrDefault(c=>c.DeviceId==device.Id);
                    if(currentConfig!=null&&RemoteData.Hash(currentConfig)!=RemoteData.Hash(config))return false;
                    var h=state.Histories.SingleOrDefault(h=>h.DeviceId==device.Id);if(h==null){h=new(){DeviceId=device.Id};state.Histories.Add(h);}if(h.Host!=device.Host){h.Problems.Clear();h.Logs.Clear();h.Polls.Clear();h.LastSuccessfulPollUtc=null;h.LogIntakeVersion=0;h.Snapshot=null;h.LogsInitialized=false;h.InterfaceStates.Clear();h.LogBlindSince=null;h.LastCompleteLogs=null;}h.Host=device.Host;h.CheckedAt=now;h.Configured=true;h.Error=error;
                    h.Polls.Add(new(startedUtc,now,snapshot!=null,error));h.Polls=h.Polls.TakeLast(200).ToList();if(snapshot!=null)h.LastSuccessfulPollUtc=now;
                    if(snapshot!=null&&snapshot.LogError.Length==0){if(h.LogBlindSince!=null&&h.LastCompleteLogs!=null)foreach(var p in h.Problems.Where(p=>p.Open&&p.Kind=="Log"))p.LastSeen+=now-h.LogBlindSince.Value;h.LogBlindSince=null;}else h.LogBlindSince??=now;
                    var connectionRule=new NetworkRule{Id="connection",Name="Доступ к мониторингу",DelaySeconds=30};
                    if(device.MaintenanceUntil<=AppTime.Now||device.MaintenanceUntil==null)NetworkSignals.Observe(h.Problems,connectionRule,"API-SSL",snapshot!=null,now);
                    if(snapshot?.AuditConfig!=null){if(h.Audit.Host!=""&&h.Audit.Host!=device.Host)h.Audit=new();h.Audit.Host=device.Host;if(h.Audit.Result==null||h.Audit.Result.CheckedUtc<snapshot.AuditConfig.CapturedUtc)RouterAuditStore.Apply(state,h,RouterAudit.Analyze(snapshot.AuditConfig,h.Audit.ManualWan),device.Name,device.MaintenanceUntil==null||device.MaintenanceUntil<=AppTime.Now);}
                    if(snapshot!=null){
                        if(device.MaintenanceUntil<=AppTime.Now||device.MaintenanceUntil==null)NetworkSignals.Observe(h.Problems,new(){Id="log-reader",Name="Чтение логов RouterOS",Severity=Severity.Warning,DelaySeconds=0},"/log",snapshot.LogError.Length==0,now);
                        var previousSnapshot=h.Snapshot;h.Snapshot=snapshot with{Logs=null,AuditConfig=null};NetworkSignals.Interfaces(h.Problems,snapshot.Interfaces.Select(p=>new NetworkPortState(p.GetValueOrDefault("name",""),p.GetValueOrDefault("running") is "true" or "yes",p.GetValueOrDefault("disabled") is "true" or "yes",p.GetValueOrDefault("comment",""))),effective,new Dictionary<string,string[]>(),now,device.MaintenanceUntil>AppTime.Now);
                        var fresh=RouterLogIntake.Collect(h,snapshot,config.User,effective,now);
                        NetworkFlaps.Collect(h,snapshot,rules,fresh,now);
                        var beforeUptime=NetworkLogRules.UptimeSeconds(previousSnapshot?.Resource.GetValueOrDefault("uptime","")??"");
                        var afterUptime=NetworkLogRules.UptimeSeconds(snapshot.Resource.GetValueOrDefault("uptime",""));
                        if(beforeUptime!=null&&afterUptime!=null&&afterUptime+5<beforeUptime&&!fresh.Any(l=>l.Message.Contains("rebooted",StringComparison.OrdinalIgnoreCase))){
                            var reboot=new NetworkLog{Key=Guid.NewGuid().ToString("N"),ReceivedAt=now,Topics="system,critical",Message="router rebooted (uptime decreased)",Meaning="Обнаружено опросом: время работы уменьшилось; причина перезагрузки не установлена"};h.Logs.Add(reboot);fresh.Add(reboot);
                        }
                        if(snapshot.LogError.Length==0){
                            if(device.MaintenanceUntil<=AppTime.Now||device.MaintenanceUntil==null)NetworkLogRules.Evaluate(h.Problems,h.Logs,fresh,rules,now);
                            h.LogsInitialized=true;h.LastCompleteLogs=now;
                        }
                        h.Logs=h.Logs.TakeLast(1000).ToList();h.Problems=h.Problems.Where(p=>p.Open).Concat(h.Problems.Where(p=>!p.Open).OrderByDescending(p=>p.LastSeen).Take(2000)).ToList();if(snapshot.LogError.Length>0)h.Error="Состояние прочитано; логи недоступны: "+snapshot.LogError;
                    }
                    UpdateNotifications.Suppress(state);
                    NetworkNotifications.Capture(state,h,device.Name,rules,now,availabilityDelaySeconds);
                    return true;
                },true);
                if(snapshot!=null&&read==null){
                    using var catalogBudget=CancellationTokenSource.CreateLinkedTokenSource(ct);catalogBudget.CancelAfter(TimeSpan.FromSeconds(12));
                    try{var status=await updates.Read(snapshot.Resource.GetValueOrDefault("version",""),catalogBudget.Token,config.UpdateChannel);var json=System.Text.Json.JsonSerializer.Serialize(status);await using var updateDb=factory.Create();await updateDb.Devices.Where(d=>d.Id==device.Id&&d.Host==device.Host).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.OsUpdatesJson,json),catalogBudget.Token);}
                    catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
                    catch(Exception e)when(e is OperationCanceledException or IOException or HttpRequestException or InvalidOperationException){}
                }
            });
        }finally{gate.Release();}
    }
}
