using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Core;
using Microsoft.EntityFrameworkCore;
namespace SysAdminAssistant.Server;

public sealed class RouterMonitoringWorker(ServerState state,ILogger<RouterMonitoringWorker> log):BackgroundService
{
 public override async Task StopAsync(CancellationToken token){await base.StopAsync(token);await state.Routers.DisposeAsync();}
 protected override Task ExecuteAsync(CancellationToken token)=>IndependentPollLoop.RunAsync(async()=>{
   using var scope=AppTime.Scope(state.Settings.TimeZoneId);
   if(state.Settings.MonitoringEnabled)await state.Routers.CollectAsync(state.Factory,state.Settings.Router,token,availabilityDelaySeconds:state.Settings.AvailabilityAlertMinutes*60);
   state.Touch();
  },()=>TimeSpan.FromSeconds(Math.Clamp(state.Settings.PollSeconds,15,3600)),state.RouterWake,
  e=>log.LogWarning("Ошибка сетевого опроса: {Kind}",e.GetType().Name),token);

}

// The audit shares the persistent read-only API session for this device.
public sealed class RouterConfigurationWorker(ServerState state,ILogger<RouterConfigurationWorker> log):BackgroundService
{
 private readonly Dictionary<int,DateTime> retry=[];
 protected override async Task ExecuteAsync(CancellationToken token){
  while(!token.IsCancellationRequested){
   try{
    using var scope=AppTime.Scope(state.Settings.TimeZoneId);
    if(state.Settings.MonitoringEnabled){await using var db=state.Factory.Create();var devices=await db.Devices.AsNoTracking().Where(d=>d.Enabled&&d.Type=="MikroTik").ToArrayAsync(token);
     foreach(var d in devices){
      if(retry.TryGetValue(d.Id,out var due)&&due>DateTime.UtcNow)continue;
      var fresh=state.Routers.Store.State(s=>s.Histories.FirstOrDefault(h=>h.DeviceId==d.Id)?.Audit.Result?.CheckedUtc);
      if(fresh>DateTime.UtcNow.AddMinutes(-15))continue;
      retry[d.Id]=DateTime.UtcNow.AddMinutes(15);
      try{await RouterAuditRefresh.RunAsync(state.Routers,state.Factory,d.Id,token);state.Touch();}
      catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
      catch(Exception e){log.LogWarning("Не завершена проверка конфигурации устройства {Id}: {Kind}",d.Id,e.GetType().Name);}
     }
    }
   }catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
   catch(Exception e){log.LogWarning("Ошибка фоновой проверки конфигураций: {Kind}",e.GetType().Name);}
   try{await Task.Delay(TimeSpan.FromMinutes(1),token);}catch(OperationCanceledException){break;}
  }
 }
}

public sealed class AvailabilityMonitoringWorker(ServerState state,ILogger<AvailabilityMonitoringWorker> log):BackgroundService
{
 protected override Task ExecuteAsync(CancellationToken token)=>IndependentPollLoop.RunAsync(async()=>{
  var settings=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(state.Settings,RemoteData.Json),RemoteData.Json)!;
  using var scope=AppTime.Scope(settings.TimeZoneId);
  if(!settings.MonitoringEnabled)return;
  var factory=new DatabaseFactory(settings);var monitor=new MonitoringService(factory,settings);
  await monitor.CheckAsync(token);
  await ApplicationNotifications.CaptureAsync(factory,state.Routers.Store,settings,AppTime.Now,token,state.MonitorErrors);
  state.Touch();
 },()=>TimeSpan.FromSeconds(Math.Clamp(state.Settings.PollSeconds,15,3600)),state.AvailabilityWake,
 e=>log.LogWarning("Ошибка проверки доступности: {Kind}",e.GetType().Name),token);
}
