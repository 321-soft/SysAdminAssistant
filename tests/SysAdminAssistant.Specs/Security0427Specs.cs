using System.Net;
using System.Net.Http.Headers;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class Security0427Specs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        bool Blocks(Action action){try{action();return false;}catch(ArgumentException){return true;}}
        var old=new MailSettings{Host="mail.example.test",Port=993,User="reports",ProtectedPassword="protected-fixture"};
        var next=new MailSettings{Host="MAIL.example.test",Port=993,User="reports",Folder="Archive"};
        check("Stored IMAP secret can be reused for unchanged server, port and login",StoredCredential.MailPassword(next,old)=="protected-fixture");
        next.Host="other.example.test";
        check("Stored IMAP secret cannot follow an edited host",Blocks(()=>StoredCredential.MailPassword(next,old)));

        var store=new NetworkStore(Path.Combine(folder,"security-0427"));
        store.State(s=>{s.Connections.Add(new(){DeviceId=1,Port=8729,User="monitor",ProtectedPassword="protected-fixture"});return true;},true);
        store.BindLegacyHosts(new Dictionary<int,string>{{1,"192.0.2.10"}});
        var c=store.Connection(1);
        var saved=store.MonitorConnection(1)!;
        check("Existing RouterOS password is bound to the original device host",NetworkStore.Target("192.0.2.10",saved,new()).ProtectedPassword=="protected-fixture"&&Blocks(()=>NetworkStore.Target("192.0.2.11",saved,new())));
        c=store.Connection(1);
        var change=new NetworkConnectionSave(1,8729,"monitor","","","generic-network",new(),c.Hash);
        check("RouterOS connection save rejects stale secret on changed TLS pin or host",
            Blocks(()=>store.Save(change with{CertificateSha256=new string('A',64)},"192.0.2.10"))&&
            Blocks(()=>store.Save(change,"192.0.2.11")));
        check("Legacy RouterOS password cannot be used on a second device host",
            Blocks(()=>NetworkStore.Target("192.0.2.11",null,new(){Host="192.0.2.10",ProtectedPassword="protected-fixture"})));

        using var client=new HttpClient(new HeaderOnlyHandler()){BaseAddress=new Uri("https://example.test/"),Timeout=TimeSpan.FromSeconds(10)};
        using var safety=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        bool timedOut=false;
        try{_=await AgentHttp.SendAsync<object>(client,"agent/report-v3",new{},safety.Token,TimeSpan.FromMilliseconds(100));}
        catch(OperationCanceledException)when(!safety.IsCancellationRequested){timedOut=true;}
        catch(OperationCanceledException){}
        check("Agent request deadline also cancels a stalled response body",timedOut);
        using var response=new StreamContent(new StalledBody());
        bool clientTimedOut=false;
        try{_=await RemoteResponseBody.ReadJson<object>(response,safety.Token,TimeSpan.FromMilliseconds(100));}
        catch(TimeoutException){clientTimedOut=true;}
        check("Desktop API body has a deadline independent of HTTP headers",clientTimedOut);
        using var largeError=new StringContent(new string('x',5000));
        check("Desktop error preview stops at 2000 characters", (await RemoteResponseBody.ReadError(largeError,safety.Token)).Length==2000);
    }

    private sealed class HeaderOnlyHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var content=new StreamContent(new StalledBody());
            content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
    private sealed class StalledBody:Stream
    {
        public override bool CanRead=>true;
        public override bool CanSeek=>false;
        public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {await Task.Delay(Timeout.Infinite,cancellationToken);return 0;}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
