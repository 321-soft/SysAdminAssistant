using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public static class AgentMonitoring
{
    public static async Task RefreshHealth(ServerState state,int id,CancellationToken token){var a=state.Agents.Get(id);if(a?.Report==null||a.LastSeenUtc==null||a.LastSeenUtc<DateTime.UtcNow.AddMinutes(-6))return;var f=AgentMetricHistory.EffectiveFindings(state.DirectoryPath,id,a.Report,a.Policy,AgentChecks.Evaluate(a.Report,a.Policy)).Concat(state.EventStore(id).Findings(id)).ToArray();var health=f.Any(x=>x.Severity==Severity.Critical)?"CRITICAL":f.Length>0?"WARNING":"OK";var summary=f.Length==0?"Показатели, службы и журналы в норме":string.Join("; ",f.Select(x=>x.Title));if(summary.Length>4000)summary=summary[..4000];await using var db=state.Factory.Create();await db.Devices.Where(d=>d.Id==id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.AgentHealth,health).SetProperty(d=>d.AgentSummary,summary),token);state.Touch();}
    public static void Cancel(ServerState state,int id)=>state.Routers.Store.State(s=>{
        bool Match(string key)=>key.StartsWith("agent:"+id+":",StringComparison.Ordinal)||key=="agent-link:"+id||key.StartsWith("journal:"+id+":")||key=="os-updates:"+id;
        foreach(var key in s.AvailabilityAlerts.Keys.Where(Match).ToArray())s.AvailabilityAlerts.Remove(key);
        foreach(var n in s.Notifications.Where(n=>Match(n.EntityKey)&&n.HandledAt==null)){n.HandledAt=DateTime.UtcNow;n.Result="Агент отключён или устройство на обслуживании";}return true;
    },true);
    public static async Task<AgentReply> ReportAsync(ServerState state,AgentEnvelope envelope,CancellationToken token)
    {
        var deviceId=state.Agents.DeviceId(envelope.AgentId);
        await using var db=state.Factory.Create();var device=await db.Devices.SingleOrDefaultAsync(d=>d.Id==deviceId&&d.Enabled&&d.Type!="MikroTik"&&d.Type!="Коммутатор",token);if(device==null)throw new UnauthorizedAccessException(global::SysAdminAssistant.Core.ProductText.Get("Устройство отключено или отсутствует"));
        var accepted=state.Agents.Accept(envelope);var now=AppTime.Now;
        var linux=accepted.Report.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase);
        var eventStore=linux?state.LinuxAgentEvents:state.AgentEvents;eventStore.Ingest(deviceId,accepted.Report.EventBatch,accepted.Report.Updates,device.MaintenanceUntil>now,DateTime.UtcNow);if(linux)eventStore.RecoverLinux(deviceId,accepted.Report,accepted.Policy,DateTime.UtcNow);else eventStore.Recover(deviceId,accepted.Report,accepted.Policy,DateTime.UtcNow,state.Agents.Get(deviceId)?.RecentReboots);eventStore.Flush(deviceId,state.Routers.Store);
        var observedFindings=AgentChecks.Evaluate(accepted.Report,accepted.Policy);
        var findings=AgentMetricHistory.EffectiveFindings(state.DirectoryPath,deviceId,accepted.Report,accepted.Policy,observedFindings).Concat(eventStore.Findings(deviceId)).ToArray();
        AgentMetricHistory.Capture(state.DirectoryPath,deviceId,accepted.Report,accepted.Policy,observedFindings);
        if(accepted.Report.Updates!=null){var json=System.Text.Json.JsonSerializer.Serialize(OsUpdateStatus.Merge(OsUpdateStatus.Parse(device.OsUpdatesJson),accepted.Report.Updates),new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});await db.Devices.Where(d=>d.Id==deviceId).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.OsUpdatesJson,json),token);}
        var health=findings.Any(f=>f.Severity==Severity.Critical)?"CRITICAL":findings.Length>0?"WARNING":"OK";
        var summary=findings.Length==0?"Показатели и службы в норме":string.Join("; ",findings.Select(f=>f.Title));
        if(summary.Length>4000)summary=summary[..4000]+"…";
        await db.Devices.Where(d=>d.Id==deviceId).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.AgentLastSeen,(DateTime?)now).SetProperty(d=>d.AgentHealth,health).SetProperty(d=>d.AgentSummary,summary),token);
        if(device.MaintenanceUntil>now)Cancel(state,deviceId);
        state.Routers.Store.State(s=>{
            var prefix="agent:"+deviceId+":";var active=findings.Select(f=>prefix+f.Key).ToHashSet();
            foreach(var old in s.AvailabilityAlerts.Keys.Where(k=>k.StartsWith(prefix,StringComparison.Ordinal)&&!active.Contains(k)&&LinuxPolicy.CanRecover(k[prefix.Length..],accepted.Report,accepted.Policy,s.AvailabilityAlerts[k].FailedSinceUtc)).ToArray())AvailabilityAlerts.Observe(s,old,false,now,0,accepted.Policy.IntervalSeconds,Severity.Warning,"Агент · "+accepted.Report.MachineName,"","Показатель восстановлен");
            foreach(var f in observedFindings.Where(f=>!f.Key.StartsWith("journal:")).Where(_=>device.MaintenanceUntil<=now||device.MaintenanceUntil==null))AvailabilityAlerts.Observe(s,prefix+f.Key,true,now,f.DelayMinutes,accepted.Policy.IntervalSeconds,f.Severity,"Агент · "+accepted.Report.MachineName,f.Title+"\n"+AgentIssueAdvice.Metric(f,accepted.Report,accepted.Policy,true),"Показатель восстановлен",AgentIssueSignature.For(f,accepted.Report));
            AvailabilityAlerts.Observe(s,"agent-link:"+deviceId,false,now,accepted.Policy.AlertMinutes,accepted.Policy.IntervalSeconds,Severity.Warning,"Агент · "+accepted.Report.MachineName,"","Связь с агентом восстановлена");
            if(accepted.Reboot && (linux||accepted.Report.EventBatch==null) && (device.MaintenanceUntil<=now||device.MaintenanceUntil==null))s.Notifications.Add(new(){EntityKey=prefix+"reboot",Source="Агент · "+accepted.Report.MachineName,Title="Обнаружена перезагрузка по уменьшению uptime. Причина не установлена\n"+AgentIssueAdvice.Journal(linux?"linux-reboot":"reboot","",state.Agents.Get(deviceId)!.RecentReboots.Length,DateTime.UtcNow,null,[],0,false),Severity=state.Agents.Get(deviceId)!.RecentReboots.Count(t=>t>DateTime.UtcNow.AddMinutes(-30))>=3?Severity.Critical:Severity.Warning,CreatedAt=DateTime.UtcNow});
            return true;
        },true);state.Touch();return accepted.Reply;
    }
    public static async Task SweepAsync(ServerState state,CancellationToken token)
    {
        var now=AppTime.Now;await using var db=state.Factory.Create();var devices=await db.Devices.AsNoTracking().Where(d=>d.Enabled).ToDictionaryAsync(d=>d.Id,token);
        foreach(var agent in state.Agents.Active()){
            if(!devices.TryGetValue(agent.DeviceId,out var device)||device.MaintenanceUntil>now){Cancel(state,agent.DeviceId);continue;}
            if((device.OsUpdatesJson.Length==0||!OsUpdateStatus.Parse(device.OsUpdatesJson).HasUpdates)&&state.Agents.Get(agent.DeviceId)?.Report?.Updates is {HasUpdates:true} recovered){
                var json=System.Text.Json.JsonSerializer.Serialize(OsUpdateStatus.Normalize(recovered));await db.Devices.Where(d=>d.Id==device.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.OsUpdatesJson,json),token);state.Touch();
            }
            if(agent.LastSeenUtc==null)continue;
            state.EventStore(agent.DeviceId).Flush(agent.DeviceId,state.Routers.Store);
            var expired=DateTime.UtcNow-agent.LastSeenUtc>TimeSpan.FromSeconds(Math.Max(60,agent.Policy.IntervalSeconds*2));
            if(expired){
                state.Routers.Store.State(s=>{AvailabilityAlerts.Observe(s,"agent-link:"+agent.DeviceId,true,now,agent.Policy.AlertMinutes,agent.Policy.IntervalSeconds,Severity.Warning,"Агент · "+device.Name,"Нет свежих данных агента\n"+AgentIssueAdvice.Connection(agent.LastSeenUtc,DateTime.UtcNow,agent.Policy.IntervalSeconds,state.Agents.Get(agent.DeviceId)?.Report?.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)==true),"Связь с агентом восстановлена");return true;},true);
                if(await db.Devices.Where(d=>d.Id==device.Id&&d.AgentHealth!="MISSING").ExecuteUpdateAsync(s=>s.SetProperty(d=>d.AgentHealth,"MISSING").SetProperty(d=>d.AgentSummary,"Нет свежих данных агента; доступность ICMP проверяется отдельно"),token)>0)state.Touch();
            }
        }
    }
}
