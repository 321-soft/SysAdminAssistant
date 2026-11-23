using SysAdminAssistant.Infrastructure;
internal static class Revision180Specs
{
    public static async Task Run(Action<string,bool> check)
    {
        var renewal=new SessionRenewal();var calls=0;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Prompt(CancellationToken token){Interlocked.Increment(ref calls);entered.SetResult();return await release.Task.WaitAsync(token);}
        var observed=renewal.Version;
        var first=renewal.RenewAsync(observed,Prompt,default);await entered.Task;
        var others=Enumerable.Range(0,20).Select(_=>renewal.RenewAsync(observed,Prompt,default)).ToArray();
        check("Session concurrent requests show one password dialog",calls==1&&!first.IsCompleted&&others.All(t=>!t.IsCompleted));
        release.SetResult(true);await Task.WhenAll(others.Append(first));
        check("Session renewed requests share one new generation",calls==1&&renewal.Version==observed+1);
        await renewal.RenewAsync(observed,_=>throw new Exception("stale 401 prompted"),default);
        check("Late old-session 401 does not prompt again",calls==1);
        await renewal.RenewAsync(renewal.Version,_=>{calls++;return Task.FromResult(true);},default);
        check("A later real expiry prompts again",calls==2&&renewal.Version==2);
        var cancelled=new SessionRenewal();var denied=0;
        async Task Reject(){try{await cancelled.RenewAsync(cancelled.Version,_=>{denied++;return Task.FromResult(false);},default);throw new Exception("Expected cancellation");}catch(SessionRenewalCanceledException){}}
        await Reject();await Reject();await Reject();
        check("Cancel prevents background prompt storm",denied==1&&cancelled.Version==0);
        cancelled.AllowPrompt();await cancelled.RenewAsync(0,_=>{denied++;return Task.FromResult(true);},default);
        check("Explicit reconnect resumes after cancelled prompt",denied==2&&cancelled.Version==1);
        using var token=new CancellationTokenSource();token.Cancel();
        try{await cancelled.RenewAsync(1,_=>throw new Exception("cancelled request prompted"),token.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
        check("Closing client cancels renewal without prompt",cancelled.Version==1);
        var fault=new SessionRenewal();
        try{await fault.RenewAsync(0,_=>throw new IOException("offline"),default);}catch(IOException){}
        try{await fault.RenewAsync(0,_=>throw new Exception("unexpected reprompt"),default);}catch(SessionRenewalCanceledException){}
        check("Failed prompt releases lock without background reprompt",fault.Version==0);
        fault.AllowPrompt();await fault.RenewAsync(0,_=>Task.FromResult(true),default);
        check("Failed prompt permits later explicit recovery",fault.Version==1);

        foreach(var status in new[]{System.Net.HttpStatusCode.OK,System.Net.HttpStatusCode.Forbidden,System.Net.HttpStatusCode.InternalServerError})
        {
            int sent=0,prompted=0;
            using var result=await SessionRequests.SendAsync(()=>{sent++;return Task.FromResult(new System.Net.Http.HttpResponseMessage(status));},new(),_=>{prompted++;return Task.FromResult(true);},default);
            check("HTTP result does not retry or prompt "+status,sent==1&&prompted==0&&result.StatusCode==status);
        }
        int requests=0,dialogs=0;
        using(var result=await SessionRequests.SendAsync(()=>Task.FromResult(new System.Net.Http.HttpResponseMessage(++requests==1?System.Net.HttpStatusCode.Unauthorized:System.Net.HttpStatusCode.OK)),new(),_=>{dialogs++;return Task.FromResult(true);},default))
            check("Definite 401 retries once after successful password",requests==2&&dialogs==1&&result.IsSuccessStatusCode);
        requests=dialogs=0;
        try{using var result=await SessionRequests.SendAsync(()=>{requests++;return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));},new(),_=>{dialogs++;return Task.FromResult(true);},default);throw new Exception("Expected cancelled retry");}catch(SessionRenewalCanceledException){}
        check("Repeated 401 never loops or replays a third command",requests==2&&dialogs==1);
        requests=dialogs=0;
        try{using var result=await SessionRequests.SendAsync(()=>{requests++;throw new System.Net.Http.HttpRequestException("TLS failed");},new(),_=>{dialogs++;return Task.FromResult(true);},default);}catch(System.Net.Http.HttpRequestException){}
        check("TLS or transport failure never prompts or resends",requests==1&&dialogs==0);
        requests=0;
        try{using var result=await SessionRequests.SendAsync(()=>{requests++;return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));},new(),_=>Task.FromResult(false),default);}catch(SessionRenewalCanceledException){}
        check("Cancelled password never replays original command",requests==1);
    }
}
