using System.Collections.Concurrent;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public interface IRouterReadSession:IAsyncDisposable {
    Task ConnectAsync(RouterSettings settings,CancellationToken token=default);
    Task<RouterAuditConfig> AuditAsync(CancellationToken token);
    Task<IReadOnlyList<Dictionary<string,string>>> GetFirewallAsync(CancellationToken token=default);
    Task<RouterSnapshot> SnapshotAsync(CancellationToken token=default);
}
public sealed class RouterSessionPool(Func<IRouterReadSession>? create=null) : IAsyncDisposable
{
    private sealed record Session(string Signature,IRouterReadSession Client);
    private readonly ConcurrentDictionary<int,Session> sessions=new();
    private readonly ConcurrentDictionary<int,SemaphoreSlim> locks=new();
    public Task<RouterSnapshot> ReadAsync(int id,RouterSettings config,CancellationToken token)=>UseAsync(id,config,(c,t)=>c.SnapshotAsync(t),token);
    public Task<RouterSnapshot> FullSnapshotAsync(int id,RouterSettings config,CancellationToken token)=>UseAsync(id,config,async(c,t)=>(await c.SnapshotAsync(t)) with {AuditConfig=await c.AuditAsync(t)},token,90);
    public Task<RouterAuditConfig> AuditAsync(int id,RouterSettings config,CancellationToken token)=>UseAsync(id,config,(c,t)=>c.AuditAsync(t),token,50);
    public Task<IReadOnlyList<Dictionary<string,string>>> FirewallAsync(int id,RouterSettings config,CancellationToken token)=>UseAsync(id,config,(c,t)=>c.GetFirewallAsync(t),token);
    private async Task<T> UseAsync<T>(int id,RouterSettings config,Func<IRouterReadSession,CancellationToken,Task<T>> read,CancellationToken token,int seconds=40)
    {
        var mutex=locks.GetOrAdd(id,_=>new(1,1));await mutex.WaitAsync(token);
        try {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(seconds));token=budget.Token;
        var signature=RemoteData.Hash(config);
        if(sessions.TryGetValue(id,out var previous)&&previous.Signature!=signature){sessions.TryRemove(id,out _);await previous.Client.DisposeAsync();}
        if(!sessions.TryGetValue(id,out var current)){
            var client=create?.Invoke()??new RouterOsClient{IncludeAuditInSnapshot=false,IncludeClockInSnapshot=true};try{await client.ConnectAsync(config,token);}catch{await client.DisposeAsync();throw;}
            current=new(signature,client);sessions[id]=current;
        }
        try{return await read(current.Client,token);}
        catch{sessions.TryRemove(id,out _);await current.Client.DisposeAsync();throw;}
        } finally{mutex.Release();}
    }
    public async Task RemoveAsync(int id){var mutex=locks.GetOrAdd(id,_=>new(1,1));await mutex.WaitAsync();try{if(sessions.TryRemove(id,out var session))await session.Client.DisposeAsync();}finally{mutex.Release();}}
    public async Task RetainAsync(IEnumerable<int> ids){var active=ids.ToHashSet();foreach(var id in sessions.Keys.Where(id=>!active.Contains(id)))await RemoveAsync(id);}
    public async ValueTask DisposeAsync(){await RetainAsync([]);}
}
