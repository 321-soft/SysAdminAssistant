using System.Text.Json;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Server;
public sealed class TelegramPinsWorker(ServerState state,ILogger<TelegramPinsWorker> log):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken token){
  var pins=new TelegramProblemPins(state.DirectoryPath);
  while(!token.IsCancellationRequested){
   try{using var scope=AppTime.Scope(state.Settings.TimeZoneId);using var languageScope=SysAdminAssistant.Core.ProductText.Scope(state.Settings.NotificationLanguage);var settings=JsonSerializer.Deserialize<TelegramSettings>(JsonSerializer.Serialize(state.Settings.Telegram))!;
    if(settings.ChatId.Length>0&&settings.ProtectedToken.Length>0){var problems=await DailySummaryCollector.CollectAsync(state,token);await pins.SyncAsync(settings,problems,DateTime.UtcNow,token);}
   }catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
   catch(Exception e){log.LogWarning("Закреп Telegram не обновлён ({Kind}); обычные уведомления независимы",e.GetType().Name);}
   try{await Task.Delay(TimeSpan.FromSeconds(30),token);}catch(OperationCanceledException){break;}
  }
 }
}
