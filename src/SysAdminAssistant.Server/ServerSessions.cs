using System.Collections.Concurrent;
using System.Security.Cryptography;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Server;
public sealed class ServerSessions(UserDirectory users,TimeProvider? clock=null)
{
    private readonly TimeProvider time=clock??TimeProvider.System;
    private DateTime Now=>time.GetUtcNow().UtcDateTime;
    private sealed record Session(SessionLease Lease, string Address, string UserId, int Revision, DateTime VaultUntil,DateTime InventoryUntil);
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly LoginAttempts attempts=new();
    private readonly SemaphoreSlim passwordGate = new(2);
    public RemoteToken? Login(RemoteLogin login, string address)
    {
        if (login.User.Length > 100 || login.Password.Length > 256 || !attempts.AllowPeer(address,Now) || !passwordGate.Wait(0)) return null;
        UserView? actor;
        try {
            var account=users.FindAccountId(login.User);
            if(account!=null&&!attempts.AllowAccount("login",account,Now))return null;
            actor=users.Authenticate(login.User,login.Password);
        } finally { passwordGate.Release(); }
        if (actor == null) return null;
        foreach (var (key, value) in sessions) if (value.Lease.Expired(Now)) sessions.TryRemove(key, out _);
        if (sessions.Count >= 512) return null;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var lease = SessionLease.Create(login.RememberSession,Now);
        sessions[Digest(token)] = new(lease, address, actor.Id, actor.Revision, DateTime.MinValue,DateTime.MinValue); return new(token, lease.Expires);
    }
    private static string Digest(string token) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    private static string? Token(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.Ordinal) && value.Length == 71 ? value[7..] : null;
    }
    public UserView? Actor(HttpContext context)
    {
        var token = Token(context); if (token == null) return null;
        var key = Digest(token);
        while(sessions.TryGetValue(key,out var s)) {
            var actor=users.Current(s.UserId,s.Revision);var now=Now;
            if(s.Lease.Expired(now)||actor==null){sessions.TryRemove(new KeyValuePair<string,Session>(key,s));return null;}
            if(s.Address!=context.Connection.RemoteIpAddress?.ToString())return null;
            if(!s.Lease.Remember)return actor;
            if(sessions.TryUpdate(key,s with{Lease=s.Lease.Touch(now)},s))return actor;
        }
        return null;
    }

    public bool UnlockVault(HttpContext context,string password)=>Unlock(context,password,false);
    public bool UnlockInventory(HttpContext context,string password)=>Unlock(context,password,true);
    private bool Unlock(HttpContext context,string password,bool inventory)
    {
        var actor=Actor(context);if(actor==null||password.Length>256||!attempts.AllowAccount(inventory?"inventory":"vault",actor.Id,Now)||!passwordGate.Wait(0))return false;
        try{
            if(users.Authenticate(actor.Login,password)?.Id!=actor.Id)return false;
            var key=Digest(Token(context)!);
            while(sessions.TryGetValue(key,out var s)){
                if(s.Lease.Expired(Now)||users.Current(s.UserId,s.Revision)==null)return false;
                var next=inventory?s with{InventoryUntil=Now.AddMinutes(5)}:s with{VaultUntil=Now.AddMinutes(5)};
                if(sessions.TryUpdate(key,next,s))return true;
            }
            return false;
        }finally{passwordGate.Release();}
    }
    public bool InventoryUnlocked(HttpContext context)=>Actor(context)!=null&&sessions.TryGetValue(Digest(Token(context)!),out var s)&&s.InventoryUntil>Now;
    public void LockInventory(HttpContext context){var token=Token(context);if(token==null)return;var key=Digest(token);while(sessions.TryGetValue(key,out var s))if(sessions.TryUpdate(key,s with{InventoryUntil=DateTime.MinValue},s))return;}
    public bool VaultUnlocked(HttpContext context) => Actor(context) != null && sessions.TryGetValue(Digest(Token(context)!),out var s) && s.VaultUntil > Now;
    public void LockVault(HttpContext context)
    {
        var token=Token(context); if(token==null)return; var key=Digest(token);
        while(sessions.TryGetValue(key,out var s))if(sessions.TryUpdate(key,s with { VaultUntil=DateTime.MinValue },s))return;
    }
    public bool Valid(HttpContext context) => Actor(context) != null;
    public void Logout(HttpContext context) { var token = Token(context); if (token != null) sessions.TryRemove(Digest(token), out _); }
}
