namespace SysAdminAssistant.Infrastructure;

public sealed class SessionRenewalCanceledException : OperationCanceledException
{
    public SessionRenewalCanceledException() : base("Повторный вход отменён. Для подключения нажмите «Обновить данные».") { }
}

// One prompt for concurrent requests, including late 401 responses from the previous session.
public sealed class SessionRenewal
{
    private readonly SemaphoreSlim gate = new(1,1);
    private long version;
    private volatile bool declined;
    public long Version => Interlocked.Read(ref version);
    public void AllowPrompt() => declined=false;
    public void PausePrompts() => declined=true;
    public async Task RenewAsync(long observed, Func<CancellationToken,Task<bool>> prompt, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if(Version!=observed)return;
            if(declined)throw new SessionRenewalCanceledException();
            declined=true;
            if(!await prompt(token).ConfigureAwait(false))throw new SessionRenewalCanceledException();
            Interlocked.Increment(ref version);declined=false;
        }
        finally { gate.Release(); }
    }
}

public static class SessionRequests
{
    public static async Task<System.Net.Http.HttpResponseMessage> SendAsync(
        Func<Task<System.Net.Http.HttpResponseMessage>> send, SessionRenewal renewal,
        Func<CancellationToken,Task<bool>>? prompt, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var version=renewal.Version;
        var response=await send().ConfigureAwait(false);
        if(response.StatusCode!=System.Net.HttpStatusCode.Unauthorized||prompt==null)return response;
        response.Dispose();
        await renewal.RenewAsync(version,prompt,token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var retry=await send().ConfigureAwait(false);
        if(retry.StatusCode==System.Net.HttpStatusCode.Unauthorized)
        {
            retry.Dispose();renewal.PausePrompts();throw new SessionRenewalCanceledException();
        }
        return retry;
    }
}
