namespace SysAdminAssistant.Server;
public sealed class CertificateWorker(ServerState state,ILogger<CertificateWorker> log):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken token){while(!token.IsCancellationRequested){try{if(state.Certificates.Maintain(DateTime.UtcNow))state.Touch();}catch(Exception e)when(e is not OperationCanceledException){state.Certificates.Failed();state.Touch();log.LogWarning("Продление сертификата: {Kind}",e.GetType().Name);}try{await Task.Delay(TimeSpan.FromMinutes(1),token);}catch(OperationCanceledException){break;}}}
}
