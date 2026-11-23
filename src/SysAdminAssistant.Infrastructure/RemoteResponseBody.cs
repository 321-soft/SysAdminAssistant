using System.Net.Http.Json;

namespace SysAdminAssistant.Infrastructure;

internal static class RemoteResponseBody
{
    private static readonly TimeSpan DefaultTimeout=TimeSpan.FromSeconds(90);
    internal static Task<T?> ReadJson<T>(HttpContent content,CancellationToken token,TimeSpan? timeout=null)=>
        Read(ct=>content.ReadFromJsonAsync<T>(RemoteData.Json,ct),token,timeout);
    internal static Task<string> ReadError(HttpContent content,CancellationToken token)=>
        Read(async ct=>{
            await using var stream=await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader=new StreamReader(stream);
            var chars=new char[2000];
            var read=await reader.ReadBlockAsync(chars.AsMemory(),ct).ConfigureAwait(false);
            return new string(chars,0,read);
        },token);
    private static async Task<T> Read<T>(Func<CancellationToken,Task<T>> consume,CancellationToken token,TimeSpan? timeout=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout??DefaultTimeout);
        try{return await consume(deadline.Token).ConfigureAwait(false);}
        catch(OperationCanceledException e)when(!token.IsCancellationRequested&&deadline.IsCancellationRequested){
            throw new TimeoutException("Истекло время чтения ответа сервера.",e);
        }
    }
}
