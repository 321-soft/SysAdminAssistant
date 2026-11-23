namespace SysAdminAssistant.Infrastructure;
// The server is authoritative; private scopes have independent, non-sliding expiry.
public sealed record SessionLease(bool Remember,DateTime Expires)
{
    public static SessionLease Create(bool remember,DateTime now)=>new(remember,now.Add(remember?TimeSpan.FromHours(1):TimeSpan.FromHours(8)));
    public bool Expired(DateTime now)=>now>=Expires;
    public SessionLease Touch(DateTime now)=>Remember&&!Expired(now)?this with{Expires=now.AddHours(1)}:this;
}
// Monotonic time avoids wall-clock edits extending an offline client session.
public sealed class ConnectionLease(TimeProvider? clock=null)
{
    private readonly TimeProvider time=clock??TimeProvider.System;
    private readonly object sync=new();private long last;private bool active;
    public void Connected(){lock(sync){last=time.GetTimestamp();active=true;}}
    public bool Expired {get{lock(sync)return active&&time.GetElapsedTime(last,time.GetTimestamp())>=TimeSpan.FromHours(1);}}
    public void Clear(){lock(sync)active=false;}
}
