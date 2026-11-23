using SysAdminAssistant.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace SysAdminAssistant.Server;
public sealed class ServerHeartbeatWorker(ServerState state,IHostApplicationLifetime lifetime):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken token){
  while(!lifetime.ApplicationStarted.IsCancellationRequested){await Task.Delay(100,token);}
  using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
  do{
   var settings=state.Settings.ServerHeartbeat;
   if(!settings.Enabled){state.HeartbeatStatus=state.HeartbeatStatus with{Enabled=false,Message="Выключен. Для обслуживания выключите также PHP-наблюдатель на сайте."};}
   else{
    var attempt=DateTime.UtcNow;state.HeartbeatStatus=state.HeartbeatStatus with{Enabled=true,LastAttemptUtc=attempt};
    try{
     using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(15));
     var secret=LocalSecret.Unprotect(settings.ProtectedSecret);
     var ok=await ServerHeartbeat.ProbeAndSendAsync(async ct=>{await using var db=state.Factory.Create();_ = await db.Devices.AsNoTracking().Select(d=>d.Id).Take(1).ToArrayAsync(ct);return true;},ct=>ServerHeartbeat.SendAsync(settings.EndpointUrl,settings.ServerId,secret,ct),budget.Token);
     if(ok)state.HeartbeatStatus=new(true,attempt,DateTime.UtcNow,"PHP-наблюдатель подтвердил сигнал. Проверьте cron каждые 5 минут и Telegram командой test на сайте.");
    }catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
     catch{state.HeartbeatStatus=state.HeartbeatStatus with{Message="Сигнал не подтверждён: проверьте БД, интернет и настройку внешней проверки. Секретный URL в журналы не записывается."};}
   }
  }while(await timer.WaitForNextTickAsync(token));
 }
}
