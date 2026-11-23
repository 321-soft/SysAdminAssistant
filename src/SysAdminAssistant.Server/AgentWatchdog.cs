namespace SysAdminAssistant.Server;
public sealed class AgentWatchdog(ServerState state,ILogger<AgentWatchdog> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while(!token.IsCancellationRequested){try{await state.Gate.WaitAsync(token);try{await AgentMonitoring.SweepAsync(state,token);}finally{state.Gate.Release();}}catch(OperationCanceledException)when(token.IsCancellationRequested){break;}catch(Exception e){log.LogWarning("Agent watchdog failed: {Kind}",e.GetType().Name);}try{await Task.Delay(TimeSpan.FromSeconds(15),token);}catch(OperationCanceledException){break;}}
    }
}
