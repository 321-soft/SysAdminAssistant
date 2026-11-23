using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Server;
// Start after the host lifetime has connected to SCM, and before monitoring workers.
internal sealed class DatabaseStartup(ServerState state,ILogger<DatabaseStartup> log):IHostedService
{
    public async Task StartAsync(CancellationToken token)
    {
        if(state.Settings.DatabaseProvider==DatabaseProvider.SqlServer)
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            for(int attempt=1;;attempt++)
            {
                await using var probe=state.Factory.Create();
                if(await probe.Database.CanConnectAsync(deadline.Token))break;
                log.LogWarning("SQL Server is not ready at service startup (attempt {Attempt}); retrying in 5 seconds",attempt);
                await Task.Delay(TimeSpan.FromSeconds(5),deadline.Token);
            }
        }
        await state.InitializeAsync(token);
    }
    public Task StopAsync(CancellationToken token)=>Task.CompletedTask;
}
