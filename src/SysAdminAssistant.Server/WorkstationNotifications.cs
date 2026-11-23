using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
internal sealed class WorkstationNotifications(ServerState state,ILogger<WorkstationNotifications> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        while(await timer.WaitForNextTickAsync(token)){
            try{
                var c=state.Workstations.Config();
                var serverTelegram=state.Settings.Telegram;
                var chat=c.UseServerTelegram?serverTelegram.ChatId:c.Chat;
                var thread=c.UseServerTelegram?null:c.Thread;
                if(!c.Telegram||string.IsNullOrWhiteSpace(serverTelegram.ProtectedToken)||string.IsNullOrWhiteSpace(chat))continue;
                var items=state.Workstations.Pending(DateTime.UtcNow);if(items.Length==0)continue;
                var telegram=new TelegramSettings{Enabled=true,ChatId=chat,ProtectedToken=serverTelegram.ProtectedToken,Critical=c.Critical,Warning=c.Warning};
                await TelegramSender.SendAsync(telegram,"Рабочие станции\n"+string.Join("\n",items.Select(x=>$"{(x.Alert.Level==Severity.Critical?"🔴":"🟠")} {x.Name}: {x.Alert.Title}")),token,messageThreadId:thread);
                state.Workstations.Sent(items,DateTime.UtcNow);
            }catch(OperationCanceledException) when(token.IsCancellationRequested){return;}
            catch(Exception e){log.LogWarning("Telegram рабочих станций: {Type}",e.GetType().Name);}
        }
    }
}
