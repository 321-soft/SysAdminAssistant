using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record RouterMonitorRow(int? Id,string Name,string Host,DateTime? CheckedAt,RouterSnapshot? Snapshot,string Error,NetworkProblem[]? Problems=null,bool Configured=true,LampState? NetworkState=null,string Kind="MikroTik",OsUpdateStatus? Updates=null,string MailUpdatesJson="",int AuditCritical=0,int AuditWarnings=0,string AuditConclusion="",int AuditIgnored=0)
{
    public LampState State=>NetworkState??(Error.Length>0?LampState.Red:Snapshot==null?LampState.Gray:LampState.Green);
    public string Model=>Snapshot?.Resource.GetValueOrDefault("board-name","—")??"—";
    public string Version=>Snapshot?.Resource.GetValueOrDefault("version","—")??"—";
    public string Cpu=>(Snapshot?.Resource.GetValueOrDefault("cpu-load","—")??"—")+"%";
    public string Uptime=>Snapshot?.Resource.GetValueOrDefault("uptime","—")??"—";
    public string Status=>Kind=="Коммутатор"?(State==LampState.Green?"Доступен · ICMP":State==LampState.Red?"Недоступен · ICMP":"Ожидается проверка / обслуживание"):Error.Length>0?Error:!Configured?"Подключение не настроено":AuditCritical+AuditWarnings>0?$"Конфигурация: критичных {AuditCritical} · предупреждений {AuditWarnings}":(Problems?.Any(p=>p.Open&&p.Severity is Severity.Critical or Severity.Warning)??false)?$"Критических: {Problems.Count(p=>p.Open&&p.Severity==Severity.Critical)} · предупреждений: {Problems.Count(p=>p.Open&&p.Severity==Severity.Warning)}":Snapshot==null?"Ожидается опрос":"Работает · "+AuditConclusion+(AuditIgnored>0?" · исключений "+AuditIgnored:"");
}
public sealed class RouterMonitor
{
    private readonly ConcurrentDictionary<string,RouterMonitorRow> rows=new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim collectGate=new(1);
    public async Task<RouterMonitorRow[]> ListAsync(DatabaseFactory factory,RouterSettings settings,CancellationToken token=default)
    {
        await using var db=factory.Create();var targets=await db.Devices.AsNoTracking().Where(d=>d.Enabled&&d.Type=="MikroTik").OrderBy(d=>d.Name).Select(d=>new RouterTarget(d.Id,d.Name,d.Host)).ToArrayAsync(token);
        var list=targets.Select(t=>rows.TryGetValue(t.Host,out var value)?value with{Id=t.Id,Name=t.Name}:new RouterMonitorRow(t.Id,t.Name,t.Host,null,null,"")).ToList();
        if(settings.Host.Length>0&&!targets.Any(t=>t.Host.Equals(settings.Host,StringComparison.OrdinalIgnoreCase)))list.Add(rows.TryGetValue(settings.Host,out var value)?value with{Id=null,Name="MikroTik из настроек"}:new(null,"MikroTik из настроек",settings.Host,null,null,""));
        return list.OrderBy(r=>r.State==LampState.Red?0:r.State==LampState.Gray?1:2).ThenBy(r=>r.Name).ToArray();
    }
    public async Task CollectAsync(DatabaseFactory factory,RouterSettings settings,CancellationToken token=default,Func<RouterSettings,CancellationToken,Task<RouterSnapshot>>? read=null)
    {
        if(!await collectGate.WaitAsync(0,token))return;
        try {
            var targets=await ListAsync(factory,settings,token);
            await Parallel.ForEachAsync(targets,new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=token},async(target,ct)=>{
                var config=NetworkStore.Target(target.Host,null,settings);
                try{var snapshot=read!=null?await read(config,ct):await ReadAsync(config,ct);rows[target.Host]=target with{Snapshot=snapshot,Error="",CheckedAt=AppTime.Now};}
                catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
                catch(Exception e)when(e is RouterConnectionException or ArgumentException or InvalidOperationException or System.IO.IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException or System.Security.Cryptography.CryptographicException){rows[target.Host]=target with{Snapshot=null,Error=ErrorText(e),CheckedAt=AppTime.Now};}
            });
        }finally{collectGate.Release();}
    }
    public static string ErrorText(Exception error)=>error is System.Security.Cryptography.CryptographicException?"MikroTik: сохранённый пароль недоступен службе. Сохраните пароль повторно в настройках подключения.":error.Message;
    private static async Task<RouterSnapshot> ReadAsync(RouterSettings config,CancellationToken token){await using var client=new RouterOsClient();await client.ConnectAsync(config,token);return await client.SnapshotAsync(token);}
}
