using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class RouterReadSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        foreach(var path in new[]{"/system/reboot","/system/package/update/set","/system/package/update/install","/system/routerboard/upgrade","/ip/firewall/filter/set"}) {
            bool denied=false;try{RouterOsClient.RequireReadCommand([path]);}catch(ArgumentException){denied=true;}check("Router transport rejects write "+path,denied);
        }
        bool split=false;try{RouterOsClient.RequireReadCommand(["/system/resource/print","","/system/reboot"]);}catch(ArgumentException){split=true;}
        check("Router transport rejects sentence splitting",split);
        var reader=new UserView("reader","reader",false,true,1,new(){["routers"]=1});
        foreach(var command in new[]{"router-monitor-list","router-monitor-refresh","router-snapshot"})AccessRules.Command(reader,command);
        check("Router reader can use monitoring operations",true);
        bool blocked=false;try{AccessRules.Command(reader,"router-reboot");}catch(UnauthorizedAccessException){blocked=true;}check("Router ACL rejects writes",blocked);
        check("Router certificate pin normalizes separators",RouterOsClient.NormalizePin(string.Join(':',Enumerable.Repeat("aa",32)))==new string('A',64));
        bool bad=false;try{RouterOsClient.NormalizePin("abcd");}catch(ArgumentException){bad=true;}check("Invalid Router certificate pin rejected",bad);
        using var key=RSA.Create(2048);var request=new CertificateRequest("CN=router-fixture",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(san.Build());
        using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(1));
        // Schannel requires a usable key provider; do not use an ephemeral CNG key on Windows.
        var pfx=generated.Export(X509ContentType.Pfx,"");
        using var certificate=X509CertificateLoader.LoadPkcs12(pfx,"",OperatingSystem.IsWindows()?X509KeyStorageFlags.UserKeySet:X509KeyStorageFlags.EphemeralKeySet);
        CryptographicOperations.ZeroMemory(pfx);
        check("Router TLS fixture certificate has server private key",certificate.HasPrivateKey);
        async Task<bool> Connect(bool pinned,bool wrongPin=false,int statsFailure=0) {
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));int commands=0;Exception? serverFailure=null;
            var server=Task.Run(async()=>{
                try {
                    using var tcp=await listener.AcceptTcpClientAsync(timeout.Token);await using var ssl=new SslStream(tcp.GetStream(),false);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=certificate,EnabledSslProtocols=SslProtocols.Tls12},timeout.Token);
                    while(commands<6+RouterAuditReader.Commands.Count-1+(statsFailure==0?0:1)) {
                        var words=new List<string>();
                        while(true){var first=await ReadByte(ssl,timeout.Token);int length=first;if(first>=0x80&&first<0xc0)length=((first&0x3f)<<8)|await ReadByte(ssl,timeout.Token);if(length==0)break;var bytes=new byte[length];await ssl.ReadExactlyAsync(bytes,timeout.Token);words.Add(Encoding.UTF8.GetString(bytes));}
                        RouterOsClient.RequireReadCommand(words.ToArray());commands++;
                        if(statsFailure!=0&&words[0]=="/interface/print"&&words.Contains("=stats=")){
                            if(statsFailure==2)await WriteSentence(ssl,new[]{"!trap","=message=stats unsupported"},timeout.Token);
                            await WriteSentence(ssl,new[]{"!done"},timeout.Token);continue;
                        }
                        if(words[0]=="/ip/firewall/address-list/print"){
                            if(!words.Contains("?list=fixture-large"))throw new InvalidDataException("Unfiltered address list request");using var wire=new MemoryStream();void Sentence(params string[] values){foreach(var value in values){var bytes=Encoding.UTF8.GetBytes(value);wire.Write(RouterOsClient.EncodeLength(bytes.Length));wire.Write(bytes);}wire.WriteByte(0);}
                            for(int n=0;n<50000;n++)Sentence("!re","=list=fixture-large","=address=198.18."+(n/256)+"."+(n%256));Sentence("!done");await ssl.WriteAsync(wire.ToArray(),timeout.Token);await ssl.FlushAsync(timeout.Token);continue;
                        }
                        var values=words[0] switch{"/ip/firewall/filter/print"=>new[]{"=.id=*F","=chain=input","=action=accept","=src-address-list=fixture-large"},"/system/identity/print"=>new[]{"=name=fixture"},"/system/resource/print"=>new[]{"=board-name=CHR","=version=fixture","=cpu-load=3","=uptime=1d"},"/system/routerboard/print"=>new[]{"=routerboard=false"},"/interface/print"=>new[]{"=name=ether1","=type=ether","=running=true","=disabled=false"},_=>Array.Empty<string>()};
                        if(values.Length>0)await WriteSentence(ssl,new[]{"!re"}.Concat(values),timeout.Token);await WriteSentence(ssl,new[]{"!done"},timeout.Token);
                    }
                }catch(Exception e)when(e is AuthenticationException or IOException or OperationCanceledException){serverFailure=e;}
            });
            bool result=false;RouterConnectionException? clientFailure=null;
            try {
                await using var client=new RouterOsClient();await client.ConnectAsync(new RouterSettings{Host="127.0.0.1",Port=port,User="read-fixture",CertificateSha256=wrongPin?new string('F',64):pinned?certificate.GetCertHashString(HashAlgorithmName.SHA256):""},timeout.Token);
                var snapshot=await client.SnapshotAsync(timeout.Token);result=snapshot.Resource["board-name"]=="CHR"&&snapshot.Interfaces.Count==1;check("Router API streams and normalizes 50000 referenced addresses",snapshot.AuditConfig?.Rows("address-lists4").Count==1&&snapshot.AuditConfig.Errors.Count==0);
            }catch(RouterConnectionException e){clientFailure=e;}
            finally{listener.Stop();await server;}
            if(clientFailure!=null && (pinned&&!wrongPin || clientFailure.Failure!=RouterFailure.Certificate))
                throw new InvalidOperationException($"Router TLS fixture failed: client={clientFailure.Failure}: {clientFailure.Message}; server={serverFailure?.GetType().Name}: {serverFailure?.Message}",clientFailure);
            if(!pinned||wrongPin)check(wrongPin?"Wrong Router pin is diagnosed as certificate rejection":"TLS trust failure has actionable error instead of generic 500",clientFailure?.Failure==RouterFailure.Certificate&&commands==0);
            if(pinned&&!wrongPin)check("Pinned API-SSL snapshot uses only login and print commands",serverFailure==null&&commands==6+RouterAuditReader.Commands.Count-1+(statsFailure==0?0:1));
            return result;
        }
        check("Untrusted Router certificate is not silently accepted",!await Connect(false));
        check("Verified SHA256 enables real TLS read-only snapshot",await Connect(true));
        check("Empty interface statistics fall back to read-only interface listing",await Connect(true,statsFailure:1));
        check("Rejected interface statistics fall back to read-only interface listing",await Connect(true,statsFailure:2));
        check("Wrong SHA256 never authenticates Router TLS",!await Connect(true,true));
        var f=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"router-monitor.db")});await f.InitializeAsync();
        await using(var db=f.Create()){db.Devices.AddRange(new Device{Name="Good",Type="MikroTik",Host="good"},new Device{Name="Bad",Type="MikroTik",Host="bad"},new Device{Name="Not router",Host="other"});await db.SaveChangesAsync();}
        var monitor=new RouterMonitor();var settings=new RouterSettings{Host="good",User="read"};var blank=await monitor.ListAsync(f,settings);
        check("Router list includes each router once and no ordinary device",blank.Length==2&&blank.All(r=>r.State==LampState.Gray));
        var sample=new RouterSnapshot(new(),new(){{"board-name","fixture"}},new(),[]);
        await monitor.CollectAsync(f,settings,read:(config,_)=>config.Host=="bad"?Task.FromException<RouterSnapshot>(new RouterConnectionException("Fixture network error")):Task.FromResult(sample));
        var rows=await monitor.ListAsync(f,settings);check("One Router failure does not block healthy router",rows.Single(r=>r.Host=="bad").State==LampState.Red&&rows.Single(r=>r.Host=="good").State==LampState.Green&&rows.All(r=>r.CheckedAt!=null));
        await monitor.CollectAsync(f,settings,read:(_,_)=>Task.FromResult(sample));check("Router recovery clears previous error",(await monitor.ListAsync(f,settings)).All(r=>r.State==LampState.Green));
        var networkStore=new NetworkStore(Path.Combine(folder,"network-monitor"));
        networkStore.Import(System.Text.Json.JsonSerializer.Serialize(new NetworkDictionaryPackage{Dictionaries=[new(){Id="test-network",Name="Test",Rules=[new(){Id="wan",Name="WAN",Interfaces=["ether1"],DelaySeconds=0}]}]}));
        int goodId,badId;
        await using(var db=f.Create()){goodId=(await db.Devices.SingleAsync(d=>d.Host=="good")).Id;badId=(await db.Devices.SingleAsync(d=>d.Host=="bad")).Id;}
        networkStore.State(state=>{foreach(var id in new[]{goodId,badId})state.Connections.Add(new(){DeviceId=id,User="read",DictionaryId="test-network"});return true;},true);
        var actual=new NetworkMonitor(networkStore);
        RouterSnapshot Sample(bool running)=>sample with{Interfaces=[new(){{"name","ether1"},{"running",running?"true":"false"}}],Logs=[new(){{".id","*1"},{"time","oct/19 12:00:00"},{"topics","interface,info"},{"message","ether1 link down"}}]};
        await actual.CollectAsync(f,new(),read:(_,_)=>Task.FromResult(Sample(false)));
        check("Network actual monitor evaluates interface rules",(await actual.ListAsync(f)).All(r=>r.State==LampState.Red&&r.Problems!.Length==1));
        await actual.CollectAsync(f,new(),read:(_,_)=>Task.FromResult(Sample(true)));
        check("Network actual monitor waits for second recovery poll",(await actual.ListAsync(f)).All(r=>r.State==LampState.Red));
        await actual.CollectAsync(f,new(),read:(_,_)=>Task.FromResult(Sample(true)));
        check("Network actual monitor automatically clears confirmed alarms",(await actual.ListAsync(f)).All(r=>r.State==LampState.Green&&r.Problems!.Length==0));
        check("Network log deduplication retains RouterOS timestamp",networkStore.State(state=>state.Histories.All(h=>h.Logs.Count(l=>l.RouterTime=="oct/19 12:00:00")==1&&h.Problems.Any(p=>p.RestoredAt!=null))));
        check("Network monitor state persists across restart",(await new NetworkMonitor(new NetworkStore(networkStore.DirectoryPath)).ListAsync(f)).All(r=>r.State==LampState.Green));
        await actual.CollectAsync(f,new(),read:async(config,_)=>{
            if(config.Host=="good"){await using var db=f.Create();var d=await db.Devices.SingleAsync(d=>d.Id==goodId);d.Host="new-address";await db.SaveChangesAsync();}
            return Sample(false);
        });
        check("Network address change ignores obsolete in-flight response",(await actual.ListAsync(f)).Single(r=>r.Id==goodId).State==LampState.Gray);
        var routerReader=new UserView("reader","reader",false,true,1,new(){["routers"]=1});
        foreach(var command in new[]{"router-events","router-connection","router-dictionaries"})AccessRules.Command(routerReader,command);
        check("Network reader can inspect events and dictionaries",true);
        bool configDenied=false;try{AccessRules.Command(routerReader,"router-connection-save");}catch(UnauthorizedAccessException){configDenied=true;}
        check("Network reader cannot change connection settings",configDenied);

        var securityFactory=new DatabaseFactory(new AppSettings{SqlitePath=Path.Combine(folder,"actual-security-monitor.db")});await securityFactory.InitializeAsync();int securityId;
        await using(var db=securityFactory.Create()){var d=new Device{Name="Security router",Type="MikroTik",Host="security-fixture"};db.Devices.Add(d);await db.SaveChangesAsync();securityId=d.Id;}
        var securityStore=new NetworkStore(Path.Combine(folder,"actual-security-state"));securityStore.State(s=>{s.Connections.Add(new(){DeviceId=securityId,User="reader"});return true;},true);
        var securityMonitor=new NetworkMonitor(securityStore);
        RouterSnapshot SecuritySnapshot(string uptime)=>new(new(),new(){{"uptime",uptime}},new(),[],[new(){{".id","*1"},{"time","old"},{"topics","system,critical"},{"message","router rebooted"}}]);
        await securityMonitor.CollectAsync(securityFactory,new(),read:(_,_)=>Task.FromResult(SecuritySnapshot("1000s")));
        check("Security actual monitor baselines old reboot log",securityStore.State(s=>s.Notifications.Count)==0);
        await securityMonitor.CollectAsync(securityFactory,new(),read:(_,_)=>Task.FromResult(SecuritySnapshot("5s")));
        check("Security uptime decrease generates critical reboot and Telegram outbox",securityStore.State(s=>s.Notifications.Count(n=>n.Severity==Severity.Critical))==1&&(await securityMonitor.ListAsync(securityFactory)).Single().State==LampState.Red);
        await securityMonitor.CollectAsync(securityFactory,new(),read:(_,_)=>Task.FromResult(SecuritySnapshot("10s")));
        check("Security actual monitor does not resend persistent reboot",securityStore.State(s=>s.Notifications.Count)==1);
        bool resolveDenied=false;try{AccessRules.Command(routerReader,"router-problem-resolve");}catch(UnauthorizedAccessException){resolveDenied=true;}
        check("Security router reader cannot resolve alarms",resolveDenied);

    }
    private static async Task<int> ReadByte(Stream stream,CancellationToken token){var bytes=new byte[1];await stream.ReadExactlyAsync(bytes,token);return bytes[0];}
    private static async Task WriteSentence(Stream stream,IEnumerable<string> words,CancellationToken token){foreach(var word in words){var bytes=Encoding.UTF8.GetBytes(word);await stream.WriteAsync(RouterOsClient.EncodeLength(bytes.Length),token);await stream.WriteAsync(bytes,token);}await stream.WriteAsync(new byte[]{0},token);await stream.FlushAsync(token);}
}
