using SysAdminAssistant.Infrastructure;
static class SessionLeaseSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var now=new DateTime(2026,10,26,0,0,0,DateTimeKind.Utc);
        var remembered=SessionLease.Create(true,now);check("Session remembered lease is one hour",remembered.Expires==now.AddHours(1));
        check("Session 59 minute interruption survives",!remembered.Expired(now.AddMinutes(59)));
        check("Session exact hour expires",remembered.Expired(now.AddHours(1)));
        var touched=remembered.Touch(now.AddMinutes(59));check("Session successful contact restarts hour",touched.Expires==now.AddMinutes(119));
        check("Session expired lease cannot revive",remembered.Touch(now.AddHours(2))==remembered);
        var lease=remembered;for(var i=1;i<=48;i++)lease=lease.Touch(now.AddMinutes(i*30));check("Session connected beyond eight hours survives",!lease.Expired(now.AddHours(24)));
        var ordinary=SessionLease.Create(false,now);check("Session unselected checkbox keeps eight hour expiry",ordinary.Expires==now.AddHours(8));check("Session ordinary activity does not extend",ordinary.Touch(now.AddHours(7))==ordinary);
        var clock=new Clock(now);var connection=new ConnectionLease(clock);connection.Connected();clock.Advance(TimeSpan.FromMinutes(59));check("Client short outage does not expire",!connection.Expired);clock.Advance(TimeSpan.FromMinutes(1));check("Client monotonic hour expires",connection.Expired);connection.Connected();check("Client authenticated reconnect resets outage",!connection.Expired);clock.Wall=clock.Wall.AddYears(5);check("Client wall clock change does not expire",!connection.Expired);clock.Advance(TimeSpan.FromHours(2));check("Client sleep long gap expires",connection.Expired);connection.Clear();check("Client disposal clears lease",!connection.Expired);
        var json=System.Text.Json.JsonSerializer.Deserialize<RemoteLogin>("{\"User\":\"old\",\"Password\":\"fixture\"}");check("Session legacy login stays compatible",json!=null&&!json.RememberSession);
    }
    private sealed class Clock(DateTime now):TimeProvider{public DateTime Wall=now;private long tick;public override DateTimeOffset GetUtcNow()=>new(Wall);public override long GetTimestamp()=>tick;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public void Advance(TimeSpan step){tick+=step.Ticks;Wall+=step;}}
}
