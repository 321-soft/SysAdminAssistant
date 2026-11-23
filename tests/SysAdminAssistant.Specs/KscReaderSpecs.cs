using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class KscReaderSpecs
{
    private static readonly KscConnection Target = new("https://ksc.example:13299/", "reader", "do-not-disclose", "", true);

    public static async Task Run(Action<string, bool> check)
    {
        var stored=Target with{DeviceId=7};
        var canonical=(stored with{Address="https://KSC.EXAMPLE:13299",Domain="ignored"}).Normalize();
        check("KSC normalizes saved endpoint and internal authentication scope",canonical.SameAccount(stored.Normalize()));
        check("KSC saved password cannot follow another endpoint, login, device or auth mode",
            !(canonical with{Address="https://other.example:13299/"}).SameAccount(stored.Normalize())
            &&!(canonical with{Login="someone"}).SameAccount(stored.Normalize())
            &&!(canonical with{DeviceId=8}).SameAccount(stored.Normalize())
            &&!(canonical with{Internal=false}).SameAccount(stored.Normalize()));
        var metadata=JsonSerializer.Serialize(stored.PublicView(true));
        check("KSC profile metadata never returns the stored password",!metadata.Contains(Target.Password)&&stored.PublicView(true).PasswordSaved);
        using var signingKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now=DateTime.UtcNow;
        var certificateRequest=new CertificateRequest("CN=ksc.example",signingKey,HashAlgorithmName.SHA256);
        using var certificate=certificateRequest.CreateSelfSigned(now.AddDays(-1),now.AddDays(1));
        var pin=certificate.GetCertHash(HashAlgorithmName.SHA256);
        var hex=Convert.ToHexString(pin);
        var pinned=stored with{CertificateSha256=string.Join(':',pin.Select(b=>b.ToString("x2")))};
        var roundTrip=JsonSerializer.Deserialize<KscConnection>(JsonSerializer.Serialize(pinned.Normalize()))!;
        check("KSC fingerprint normalizes, persists, and is returned in public settings",
            roundTrip.CertificateSha256==hex&&roundTrip.PublicView(false).CertificateSha256==hex&&!JsonSerializer.Serialize(roundTrip.PublicView(false)).Contains(Target.Password));
        check("KSC legacy configuration retains standard Windows trust", JsonSerializer.Deserialize<KscConnection>(JsonSerializer.Serialize(stored).Replace(",\"CertificateSha256\":\"\"",""))!.Normalize().CertificateSha256=="");
        foreach(var invalid in new[]{new string('a',40),new string('z',64),":"}){
            var rejected=false;
            try{_=KscConnection.NormalizeCertificateSha256(invalid);}catch(ArgumentException){rejected=true;}
            check("KSC rejects malformed or SHA-1 certificate fingerprint",rejected);
        }
        var endpoint=new Uri(Target.Address);
        string? Validate(SslPolicyErrors error=SslPolicyErrors.None,X509ChainStatusFlags flags=X509ChainStatusFlags.NoError,
            string? url=null,byte[]? expected=null,DateTime? at=null)
            =>KscReader.CertificateError(endpoint,url==null?endpoint:new Uri(url),expected??pin,certificate,error,flags,at??now);
        check("KSC exact pin allows only untrusted-root or partial-chain failures",
            Validate(SslPolicyErrors.RemoteCertificateChainErrors,X509ChainStatusFlags.UntrustedRoot|X509ChainStatusFlags.PartialChain)==null);
        check("KSC rotated certificate fails even when trusted by Windows", Validate(expected:new byte[32])?.Contains("не совпадает")==true);
        check("KSC pin cannot bypass hostname, validity, revocation or signature",
            Validate(SslPolicyErrors.RemoteCertificateNameMismatch)!=null&&Validate(at:now.AddDays(2))!=null&&Validate(at:now.AddDays(-2))!=null&&
            Validate(SslPolicyErrors.RemoteCertificateChainErrors,X509ChainStatusFlags.Revoked|X509ChainStatusFlags.UntrustedRoot)!=null&&
            Validate(SslPolicyErrors.RemoteCertificateChainErrors,X509ChainStatusFlags.NotSignatureValid)!=null);
        check("KSC pin cannot transfer to another port, hostname or plaintext URL",
            Validate(url:"https://ksc.example:443/")!=null&&Validate(url:"https://other.example:13299/")!=null&&Validate(url:"http://ksc.example:13299/")!=null);
        // Match the RouterReadSpecs fixture: Schannel needs a Windows-backed server key.
        using var serverKey=RSA.Create(2048);
        var request=new CertificateRequest("CN=127.0.0.1",serverKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},true));
        var san=new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var generated=request.CreateSelfSigned(now.AddDays(-1),now.AddDays(1));
        var pfx=generated.Export(X509ContentType.Pfx,"");
        using var localCertificate=X509CertificateLoader.LoadPkcs12(pfx,"",OperatingSystem.IsWindows()
            ?X509KeyStorageFlags.UserKeySet:X509KeyStorageFlags.EphemeralKeySet);
        CryptographicOperations.ZeroMemory(pfx);
        async Task<(string ClientError,string ServerError,bool ReceivedHttp,bool SentCredentials)> Handshake(string fingerprint)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start();
            try{
                var port=((IPEndPoint)listener.LocalEndpoint).Port;
                var host=Task.Run(async()=>{
                    bool received=false,authenticated=false;
                    try{
                        using var socket=await listener.AcceptTcpClientAsync(timeout.Token);
                        await using var tls=new SslStream(socket.GetStream());
                        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{
                            ServerCertificate=localCertificate,EnabledSslProtocols=SslProtocols.Tls12
                        },timeout.Token);
                        using var reader=new StreamReader(tls,leaveOpen:true);
                        received=!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token));
                        if(!received)return ("",false,false);
                        int length=0;bool chunked=false;
                        while(await reader.ReadLineAsync(timeout.Token) is {Length:>0} line){
                            authenticated|=line.StartsWith("Authorization: KSCBasic ",StringComparison.OrdinalIgnoreCase);
                            if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))
                                length=int.Parse(line["Content-Length:".Length..].Trim(),System.Globalization.CultureInfo.InvariantCulture);
                            if(line.StartsWith("Transfer-Encoding:",StringComparison.OrdinalIgnoreCase))
                                chunked=line.Contains("chunked",StringComparison.OrdinalIgnoreCase);
                        }
                        async Task Consume(int count){
                            if(count<0||count>64||await reader.ReadBlockAsync(new char[count],timeout.Token)!=count)
                                throw new InvalidDataException("Incomplete or oversized fixture request.");
                        }
                        if(chunked){
                            while(true){
                                var size=Convert.ToInt32(await reader.ReadLineAsync(timeout.Token)??throw new EndOfStreamException(),16);
                                if(size==0){await reader.ReadLineAsync(timeout.Token);break;}
                                await Consume(size);
                                await reader.ReadLineAsync(timeout.Token);
                            }
                        }else await Consume(length);
                        await tls.WriteAsync("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),timeout.Token);
                        return ("",received,authenticated);
                    }catch(Exception e)when(e is IOException or AuthenticationException or OperationCanceledException){
                        return ($"{e.GetType().Name}: {e.GetBaseException().Message}",received,authenticated);
                    }
                });
                string failure="";
                try{await KscReader.ProbeAsync(Target with{Address=$"https://127.0.0.1:{port}/",CertificateSha256=fingerprint},timeout.Token);}
                catch(Exception e)when(e is KscConnectionException or OperationCanceledException){failure=e.Message;}
                var (serverError,received,credentials)=await host;
                return(failure,serverError,received,credentials);
            }finally{listener.Stop();}
        }
        var validHandshake=await Handshake(localCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        check($"KSC TLS handler sends HTTP only after accepting the exact pin (client: {validHandshake.ClientError}; fixture: {validHandshake.ServerError})",
            validHandshake.ReceivedHttp&&validHandshake.SentCredentials&&validHandshake.ClientError.Contains("HTTP 401"));
        var invalidHandshake=await Handshake(new string('0',64));
        check($"KSC TLS handler blocks HTTP and explains an unexpected certificate (client: {invalidHandshake.ClientError}; fixture: {invalidHandshake.ServerError})",
            !invalidHandshake.ReceivedHttp&&!invalidHandshake.SentCredentials&&invalidHandshake.ClientError.Contains("не совпадает"));
        foreach(var (kind,reason) in new[]{(HttpRequestError.SecureConnectionError,"TLS"),(HttpRequestError.NameResolutionError,"не разрешается"),(HttpRequestError.ConnectionError,"порт OpenAPI")}){
            try{await KscReader.ReadForTest(Target,1,default,new Fixture{TransportFailure=kind});check("KSC transport error is classified",false);}
            catch(KscConnectionException e){check("KSC transport error gives stage and safe reason",e.Message.Contains("Session.StartSession")&&e.Message.Contains(reason)&&!e.Message.Contains(Target.Password));}
        }
        var exclusive=await KscReader.ReadForTest(Target,1,default,new Fixture{ExclusiveEnd=true});
        check("KSC accepts half-open API ranges without losing the next event",exclusive.Cursor==2&&exclusive.Rows is [{Id:2}]);
        var exclusiveInitial=await KscReader.ReadForTest(Target,null,default,new Fixture{ExclusiveEnd=true});
        check("KSC half-open initial attachment establishes the same baseline",exclusiveInitial.Cursor==2&&exclusiveInitial.Rows.Length==0);
        var healthy = new Fixture();
        var findings=await KscReader.ReadFindingsCore(Target,"host-1","GNRL_EV_VIRUS_FOUND",1,2,5,default,new Fixture{ThreatDetails=true,ShortPages=true});
        check("KSC incident details return the threat and object for the selected host",
            findings.Events is [{Id:1,Threat:"Test.Trojan",ObjectName:"test.exe",Description:"Detected",Sha256.Length:64}]);
        check("KSC links a later action by UID and host, not by filename or username alone",
            findings.Events.Single().Actions is [{Id:4,Code:"GNRL_EV_OBJECT_QUARANTINED"}]);
        var enriched=await KscReader.ReadForTest(Target,2,default,new Fixture(),["old-host"]);
        check("KSC resolves an existing host even without new events",enriched.Rows.Length==0&&enriched.ComputerNames.GetValueOrDefault("old-host")=="WORKSTATION-42");
        var fallback=await KscReader.ReadForTest(Target,1,default,new Fixture{DenyHostLookup=true},[]);
        check("KSC host lookup denial does not block event ingestion",fallback.Rows.Length==1&&fallback.Rows[0].Host=="host-1"&&fallback.ComputerNames.Count==0);
        var batch = await KscReader.ReadForTest(Target, 1, default, healthy);
        check("KSC API reads typed event fields and resumes at saved cursor", batch.Cursor == 2 && batch.Rows is [{ Id: 2, Host: "host-1", Kind: "protection.off" }]);
        check("KSC API uses password only to start session", healthy.AuthenticatedRequests == 1 && healthy.SessionRequests == healthy.Calls.Count - 1);
        check("KSC API releases iterator and session after success", healthy.Calls.TakeLast(2).SequenceEqual(new[] { "EventProcessing.ReleaseIterator", "Session.EndSession" }));

        foreach (var status in new[] { 401, 403, 404, 429, 500 })
        {
            var broken = new Fixture { FailureStatus = status };
            Exception? error = null;
            try { await KscReader.ReadForTest(Target, 1, default, broken); }
            catch (IOException e) { error = e; }
            check($"KSC HTTP {status} returns actionable error without response secrets", error != null && error.Message.Contains($"HTTP {status}") && !error.Message.Contains(Target.Password) && !error.Message.Contains("response-secret"));
            check($"KSC HTTP {status} still closes iterator and session", broken.Calls.TakeLast(2).SequenceEqual(new[] { "EventProcessing.ReleaseIterator", "Session.EndSession" }));
        }

        foreach (var malformed in new[] { "not json", "[]", "{}", "{\"PxgRetVal\":\"not a number\"}", "{\"PxgError\":{\"detail\":\"response-secret\"}}" })
        {
            var broken = new Fixture { CountReply = malformed };
            Exception? error = null;
            try { await KscReader.ReadForTest(Target, 1, default, broken); }
            catch (Exception e) when (e is IOException or InvalidDataException) { error = e; }
            check("KSC malformed API reply fails without returning a successful batch", error != null && !error.Message.Contains("response-secret"));
            check("KSC malformed API reply releases acquired resources", broken.Calls.Contains("EventProcessing.ReleaseIterator") && broken.Calls.Contains("Session.EndSession"));
        }

        var denied=new Fixture{CreateReply="""{"PxgError":{"code":1184,"subcode":17,"message":"response-secret"}}"""};
        try { await KscReader.ReadForTest(Target, 1, default, denied); check("KSC reports OpenAPI error code and subcode without exposing upstream text",false); }
        catch(KscConnectionException e) {
            check("KSC reports OpenAPI error code and subcode without exposing upstream text",
                e.Message.Contains("CreateEventProcessing")&&e.Message.Contains("код 1184, подкод 17")&&e.Message.Contains("Доступ запрещён")&&!e.Message.Contains("response-secret"));
        }

        var cleanup = new Fixture { FailureStatus = 403, FailCleanup = true };
        try { await KscReader.ReadForTest(Target, 1, default, cleanup); check("KSC cleanup cannot hide original failure", false); }
        catch (IOException e) { check("KSC cleanup cannot hide original failure", e.Message.Contains("HTTP 403") && e.Message.Contains("GetRecordCount")); }

        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await KscReader.ReadForTest(Target, 1, cancel.Token, new Fixture()); check("KSC cancellation remains cancellation", false); }
        catch (OperationCanceledException) { check("KSC cancellation remains cancellation", true); }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public int AuthenticatedRequests { get; private set; }
        public int SessionRequests { get; private set; }
        public HttpRequestError? TransportFailure {get;init;}
        public int FailureStatus { get; init; }
        public string? CreateReply { get; init; }
        public string? CountReply { get; init; }
        public bool FailCleanup { get; init; }
        public bool DenyHostLookup { get; init; }
        public bool ThreatDetails { get; init; }
        public bool ExclusiveEnd {get;init;}
        public bool ShortPages { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(TransportFailure is {} kind)throw new HttpRequestException(kind,Target.Password);
            var name = request.RequestUri!.Segments.Last(); Calls.Add(name);
            if (request.Headers.Contains("Authorization")) AuthenticatedRequests++;
            if (request.Headers.TryGetValues("X-KSC-Session", out var values) && values.Single() == "fixture-session") SessionRequests++;
            if (name == "EventProcessing.GetRecordCount" && FailureStatus != 0)
                return Reply("response-secret", (HttpStatusCode)FailureStatus);
            if (FailCleanup && name is "EventProcessing.ReleaseIterator" or "Session.EndSession")
                return Reply("cleanup failure", HttpStatusCode.InternalServerError);
            if (name == "Session.StartSession") return Reply("{\"PxgRetVal\":\"fixture-session\"}");
            if (name == "EventProcessingFactory.CreateEventProcessing")
            {
                using var args=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var sort=args.RootElement.GetProperty("vecFieldsToOrder").EnumerateArray().Single();
                var fields=sort.GetProperty("value");
                if(sort.GetProperty("type").GetString()!="params"||
                    !fields.TryGetProperty("Name",out var field)||field.GetString()!="event_db_id"||
                    !fields.TryGetProperty("Asc",out var ascending)||!ascending.GetBoolean()||
                    fields.TryGetProperty("name",out _)||fields.TryGetProperty("asc",out _))
                    return Reply("""{"PxgError":{"code":1125}}""");
                return Reply(CreateReply ?? """{"strIteratorId":"fixture-iterator"}""");
            }
            if (name == "EventProcessing.GetRecordCount") return Reply(CountReply ?? (ThreatDetails?"{\"PxgRetVal\":5}":"{\"PxgRetVal\":2}"));
            if (name == "EventProcessing.GetRecordRange")
            {
                using var args = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var start = args.RootElement.GetProperty("nStart").GetInt32();
                var end = args.RootElement.GetProperty("nEnd").GetInt32();
                if(ExclusiveEnd)end--;
                if(ShortPages)end=Math.Min(end,start+1);
                if(ThreatDetails){
                    var fixtures=Enumerable.Range(start,end-start+1).Select(i=>new{
                        event_db_id=i+1,hostname=i is 1 or 2?"another-host":"host-1",
                        event_type=i<2?"GNRL_EV_VIRUS_FOUND":"GNRL_EV_OBJECT_QUARANTINED",
                        GNRL_EA_PARAM_1=new string('a',64),GNRL_EA_PARAM_2="test.exe",GNRL_EA_PARAM_5="Test.Trojan",
                        GNRL_EA_PARAM_6=i==4?"someone": "dfba1f9b-83de-4d66-aa31-bb5a1402439a",
                        GNRL_EA_DESCRIPTION="Detected"
                    }).ToArray();
                    return Reply(JsonSerializer.Serialize(new{pParamsEvents=new{KLEVP_EVENT_RANGE_ARRAY=fixtures}}));
                }
                var rows = Enumerable.Range(start, end - start + 1).Select(i => new { type = "params", value = new {
                    event_db_id = new { type = "long", value = i + 1 },
                    hostname = "host-1", event_type = "protection.off", GNRL_EA_SEVERITY = 4
                }}).ToArray();
                return Reply(JsonSerializer.Serialize(new { pParamsEvents = new { type = "params", value = new { KLEVP_EVENT_RANGE_ARRAY = rows } } }));
            }
            if(name=="HostGroup.GetHostInfo"){
                using var args=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                if(DenyHostLookup||args.RootElement.GetProperty("strHostName").GetString()!="old-host"||
                    !args.RootElement.GetProperty("pFields2Return").EnumerateArray().Any(x=>x.GetString()=="KLHST_WKS_DN"))
                    return Reply("""{"PxgError":{"code":1193}}""");
                return Reply("""{"PxgRetVal":{"type":"params","value":{"KLHST_WKS_DN":{"type":"string","value":"WORKSTATION-42"}}}}""");
            }
            if (name is "EventProcessing.ReleaseIterator" or "Session.EndSession") return Reply("{}");
            throw new InvalidOperationException("Unexpected fixture method");
        }
        private static HttpResponseMessage Reply(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
