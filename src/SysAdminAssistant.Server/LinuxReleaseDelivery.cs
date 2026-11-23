using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;

internal sealed class LinuxReleaseDelivery(AgentStore agents)
{
    private readonly SemaphoreSlim slots=new(2);
    public async Task SendAsync(HttpContext context,CancellationToken token)
    {
        var request=await AgentEndpoints.ReadAsync<AgentReleaseRequest>(context,token);
        if(!slots.Wait(0)){
            context.Response.StatusCode=StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter="60";
            return;
        }
        try{
            await using var stream=agents.OpenLinuxReleaseFile(request);
            var remaining=stream.Length;
            if(remaining is <1 or >150_000_000)throw new InvalidDataException(ProductText.Get("Недоступен Linux-выпуск"));
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            context.Response.ContentType="application/octet-stream";
            context.Response.ContentLength=remaining;
            var buffer=new byte[65536];
            try{
                while(remaining>0){
                    var count=await stream.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,remaining)),deadline.Token);
                    if(count==0){context.Abort();return;}
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
                    remaining-=count;
                }
                await context.Response.Body.FlushAsync(deadline.Token);
            }catch(OperationCanceledException)when(deadline.IsCancellationRequested){context.Abort();}
            catch(IOException)when(context.Response.HasStarted){context.Abort();}
        }finally{slots.Release();}
    }
}
