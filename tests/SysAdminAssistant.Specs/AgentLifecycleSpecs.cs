using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class AgentLifecycleSpecs
{
    public static void Run(string folder,Action<string,bool> check)
    {
        bool Reject(Action action){try{action();return false;}catch(Exception e)when(e is UnauthorizedAccessException or ArgumentException or CryptographicException or InvalidOperationException or JsonException){return true;}}
        var root=Path.Combine(folder,"agent-lifecycle");var binaries=new Dictionary<string,string>{["SysAdminAssistant.Agent.exe"]=new string('B',64)};
        var store=new AgentStore(root,new string('A',64),s=>s,s=>s,binaries);
        var b=AgentWire.Verify(store.Package(new(1,"https://192.0.2.1:7443/",["192.0.2.1/32"],binaries),"Fixture"));
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var wrong=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var enrollment=new AgentEnrollment(b.EnrollmentId,b.Token,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),"","Fixture");enrollment=enrollment with{Proof=Convert.ToBase64String(key.SignData(AgentWire.EnrollmentBytes(enrollment),HashAlgorithmName.SHA256))};
        store.Enroll(enrollment);check("Recovery refuses unapproved agent",Reject(()=>store.AllowRecovery(1)));store.Approve(1);
        check("Valid certificate needs no recovery approval",Reject(()=>store.AllowRecovery(1)));
        var upgraded=AgentWire.Verify(store.Package(new(1,b.ServerUrl,b.AllowedNetworks,binaries,true),"Fixture"));
        check("Local upgrade package preserves agent identity without new enrollment token",upgraded.EnrollmentId==b.EnrollmentId&&upgraded.Token==""&&store.Get(1)!.Approved);
        check("Upgrade package cannot enroll fresh key",Reject(()=>store.Enroll(enrollment with{Token=upgraded.Token})));
        check("Upgrade cannot target nonexistent agent",Reject(()=>store.Package(new(2,b.ServerUrl,b.AllowedNetworks,binaries,true),"Fixture")));
        var path=Path.Combine(root,"agent-index.json");
        AgentIndex Load()=>JsonSerializer.Deserialize<AgentIndex>(File.ReadAllText(path),AgentWire.Json)!;
        void Save(AgentIndex value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,AgentWire.Json));
        var index=Load();using(var ca=X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(index.ProtectedCa),null,X509KeyStorageFlags.EphemeralKeySet)){
            var req=new CertificateRequest("CN=SAA-Agent-"+b.EnrollmentId,key,HashAlgorithmName.SHA256);
            using var expired=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-4),DateTimeOffset.UtcNow.AddDays(-1));index.Agents.Single().Certificate=Convert.ToBase64String(expired.RawData);Save(index);
        }
        AgentEnvelope Envelope(long seq,ECDsa signing){var report=new AgentReport{MachineName="Fixture",CpuCount=1,UptimeSeconds=1000,CapturedUtc=DateTime.UtcNow};var e=new AgentEnvelope(b.EnrollmentId,seq,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),JsonSerializer.Serialize(report,AgentWire.Json),"");return e with{Signature=Convert.ToBase64String(signing.SignData(AgentWire.Bytes(e),HashAlgorithmName.SHA256))};}
        check("Expired approved identity rejects a different key",Reject(()=>store.Accept(Envelope(1,wrong))));
        index=Load();index.Agents.Single().RecoveryUntil=DateTime.UtcNow.AddSeconds(-1);Save(index);
        var accepted=store.Accept(Envelope(1,key));
        check("Expired approved identity renews automatically without confirmation",accepted.DeviceId==1&&store.Get(1)!.LastSeenUtc!=null);
        using(var renewed=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(accepted.Reply.Certificate)))check("Expired agent recovers with same key and fresh certificate",renewed.NotAfter.ToUniversalTime()>DateTime.UtcNow.AddDays(80)&&store.Get(1)!.Approved&&Load().Agents.Single().RecoveryUntil==null);
        check("Recovered report cannot replay",Reject(()=>store.Accept(Envelope(1,key))));
        var initial=store.Trust();var initialDoc=AgentTrust.Verify(initial,b.IssuerCertificate);
        check("Initial trust includes pinned server only",initialDoc.Pins.SequenceEqual(new[]{b.ServerSha256}));
        using var serverKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);var csr=new CertificateRequest("CN=future-server",serverKey,HashAlgorithmName.SHA256);using var serverCert=csr.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddYears(1));
        var futurePin=serverCert.GetCertHashString(HashAlgorithmName.SHA256);var staged=store.StageTrust(new(Convert.ToBase64String(serverCert.RawData)));var stagedDoc=AgentTrust.Verify(staged,b.IssuerCertificate,initialDoc.Version);
        check("Rotation keeps old and future HTTPS certificate",stagedDoc.Pins.Contains(b.ServerSha256)&&stagedDoc.Pins.Contains(futurePin)&&stagedDoc.Version>initialDoc.Version);
        check("Rotation trust tampering rejected",Reject(()=>AgentTrust.Verify(staged with{Payload=staged.Payload.Replace(futurePin,new string('F',64))},b.IssuerCertificate)));
        check("Rotation trust rollback rejected",Reject(()=>AgentTrust.Verify(initial,b.IssuerCertificate,stagedDoc.Version)));
        using var rogueKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);var rogueReq=new CertificateRequest("CN=rogue",rogueKey,HashAlgorithmName.SHA256);using var rogue=rogueReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(1));
        check("Rotation file from other server rejected",Reject(()=>AgentTrust.Verify(staged,Convert.ToBase64String(rogue.RawData))));
        var sameStore=new AgentStore(root,b.ServerSha256,s=>s,s=>s,binaries);check("Trust rotation survives server restart",AgentTrust.Verify(sameStore.Trust(),b.IssuerCertificate).Pins.Contains(futurePin));
        var reset=AgentTrust.Verify(store.ResetTrust(),b.IssuerCertificate,stagedDoc.Version);check("Explicit reset retires former HTTPS pins",reset.Pins.SequenceEqual(new[]{b.ServerSha256})&&reset.Version>stagedDoc.Version);
        var changedServer=new AgentStore(root,new string('F',64),s=>s,s=>s,binaries);var recoveredTrust=AgentTrust.Verify(changedServer.Trust(),b.IssuerCertificate,reset.Version);
        check("Unplanned server HTTPS change exports newer trust without reinstall",recoveredTrust.Version>reset.Version&&recoveredTrust.Pins.Contains(new string('F',64)));
        check("Invalid public certificate cannot change trust",Reject(()=>store.StageTrust(new("QUJD"))));
        var editor=new UserView("editor","Editor",false,true,1,new(){["devices"]=2});
        check("Device editor cannot rotate global HTTPS trust",Reject(()=>AccessRules.Command(editor,"agent-trust-stage"))&&Reject(()=>AccessRules.Command(editor,"agent-trust-reset")));
        check("Device editor may explicitly authorize own agent recovery",!Reject(()=>AccessRules.Command(editor,"agent-recover")));
        store.Revoke(1);check("Recovery cannot reactivate revoked agent",Reject(()=>store.AllowRecovery(1))&&Reject(()=>store.Accept(Envelope(2,key))));
        var v1=JsonSerializer.Serialize(accepted.Reply,AgentWire.Json);check("Legacy report reply has no new trust fields",!v1.Contains("Trust")&&JsonSerializer.Deserialize<AgentReply>(v1,AgentWire.Json)!=null);
    }
}
