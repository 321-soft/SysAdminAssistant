namespace SysAdminAssistant.Infrastructure;
public static class IndependentPollLoop
{
 public static async Task RunAsync(Func<Task> poll,Func<TimeSpan> interval,SemaphoreSlim wake,Action<Exception> failed,CancellationToken token){
  while(!token.IsCancellationRequested){
   try{await poll().ConfigureAwait(false);}
   catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
   catch(Exception e){failed(e);}
   try{await wake.WaitAsync(interval(),token).ConfigureAwait(false);}catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
  }
 }
}
