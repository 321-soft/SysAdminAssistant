using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class AgentSpecs
{
    public static async Task Run(string folder,Action<string,bool> check)
    {
        bool Rejected(Action action){try{action();return false;}catch(Exception e)when(e is ArgumentException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException){return true;}}
        var root=Path.Combine(folder,"agent-security");var store=new AgentStore(root,new string('A',64),s=>s,s=>s,new Dictionary<string,string>{["SysAdminAssistant.Agent.exe"]=new string('B',64)});
        AgentSignedBootstrap Package(int id)=>store.Package(new(id,"https://192.0.2.1:7443/",["192.0.2.1/32"],new(){["SysAdminAssistant.Agent.exe"]=new string('B',64)}),"Fixture");
        check("Agent device editor cannot sign arbitrary binary hashes",Rejected(()=>store.Package(new(1,"https://192.0.2.1:7443/",["192.0.2.1/32"],new(){["SysAdminAssistant.Agent.exe"]=new string('C',64)}),"Fixture")));
        var signed=Package(1);var b=AgentWire.Verify(signed);check("Agent configuration signature verifies",b.DeviceId==1);
        check("Agent configuration tampering rejected",Rejected(()=>AgentWire.Verify(signed with{Payload=signed.Payload.Replace("Fixture","Changed")})));
        check("Agent restricts endpoint to allowed subnet",Rejected(()=>AgentWire.Endpoint(new(){ServerUrl=b.ServerUrl,ServerSha256=b.ServerSha256,AllowedNetworks=["198.51.100.0/24"]})));
        check("Agent forbids HTTP endpoint",Rejected(()=>AgentWire.Endpoint(new(){ServerUrl="http://192.0.2.1/",ServerSha256=b.ServerSha256,AllowedNetworks=b.AllowedNetworks})));
        check("Agent forbids URL userinfo",Rejected(()=>AgentWire.Endpoint(new(){ServerUrl="https://user:pass@192.0.2.1/",ServerSha256=b.ServerSha256,AllowedNetworks=b.AllowedNetworks})));
        check("Agent IPv6 subnet comparison",AgentWire.Contains("2001:db8::/32",System.Net.IPAddress.Parse("2001:db8::1"))&&!AgentWire.Contains("2001:db8::/32",System.Net.IPAddress.Parse("2001:db9::1")));
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        AgentEnrollment Enrollment(AgentBootstrap bootstrap,ECDsa k){var e=new AgentEnrollment(bootstrap.EnrollmentId,bootstrap.Token,Convert.ToBase64String(k.ExportSubjectPublicKeyInfo()),"","FixtureMachine");return e with{Proof=Convert.ToBase64String(k.SignData(AgentWire.EnrollmentBytes(e),HashAlgorithmName.SHA256))};}
        var request=Enrollment(b,key);check("Agent invalid enrollment token rejected",Rejected(()=>store.Enroll(request with{Token=new string('0',64)})));
        var enrolled=store.Enroll(request);using(var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(enrolled.Certificate))){using var pub=cert.GetECDsaPublicKey()!;check("Agent certificate binds local public key",pub.ExportSubjectPublicKeyInfo().SequenceEqual(key.ExportSubjectPublicKeyInfo())&&!cert.HasPrivateKey);}
        check("Agent enrollment retry recovers lost response",store.Enroll(request).Certificate==enrolled.Certificate);
        check("Agent enrollment cannot be reused with another key",Rejected(()=>store.Enroll(Enrollment(b,other))));
        var report=new AgentReport{MachineName="FixtureMachine",Os="Windows fixture",AgentVersion="0.42.4.0",CpuName="Fixture CPU",CpuCount=4,CpuPercent=20,MemoryTotalBytes=16L*1073741824,MemoryAvailableBytes=8L*1073741824,UptimeSeconds=10000,CapturedUtc=DateTime.UtcNow,AppliedPolicyVersion=1,Services=[new("EventLog","Windows Event Log","Running"),new("RpcSs","RPC","Running"),new("Winmgmt","WMI","Running")]};
        AgentEnvelope Envelope(string id,long sequence,ECDsa k,AgentReport? data=null){var e=new AgentEnvelope(id,sequence,DateTimeOffset.UtcNow.ToUnixTimeSeconds(),JsonSerializer.Serialize(data??report,AgentWire.Json),"");return e with{Signature=Convert.ToBase64String(k.SignData(AgentWire.Bytes(e),HashAlgorithmName.SHA256))};}
        var first=Envelope(enrolled.AgentId,1,key);check("Agent cannot report before administrator approval",Rejected(()=>store.Accept(first)));
        store.Approve(1);var accepted=store.Accept(first);check("Agent approved report accepted",accepted.DeviceId==1&&store.Get(1)!.Report!.MachineName==report.MachineName);
        check("Device registry shows the version from the accepted report",store.ReportedVersions().GetValueOrDefault(1)=="0.42.4.0");
        check("Agent replayed sequence rejected",Rejected(()=>store.Accept(first)));
        bool resync=false;try{store.Accept(first);}catch(AgentSequenceException e){resync=e.NextSequence==2;}check("Agent restored counter can resynchronize only after signature proof",resync);
        var second=Envelope(enrolled.AgentId,2,key);check("Agent signed body tampering rejected",Rejected(()=>store.Accept(second with{Payload=second.Payload.Replace("FixtureMachine","OtherMachine")})));
        check("Agent wrong key rejected",Rejected(()=>store.Accept(Envelope(enrolled.AgentId,2,other))));
        var oldTime=second with{Timestamp=DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds()};oldTime=oldTime with{Signature=Convert.ToBase64String(key.SignData(AgentWire.Bytes(oldTime),HashAlgorithmName.SHA256))};check("Agent old timestamp rejected",Rejected(()=>store.Accept(oldTime)));
        var restarted=new AgentStore(root,new string('A',64),s=>s,s=>s,new Dictionary<string,string>{["SysAdminAssistant.Agent.exe"]=new string('B',64)});check("Agent sequence and link survive store restart",Rejected(()=>restarted.Accept(first))&&restarted.Get(1)!.Approved);check("Reported agent version survives store restart",restarted.ReportedVersions().GetValueOrDefault(1)=="0.42.4.0");
        var oldVersion=store.Get(1)!.Policy.Version;var policy=new AgentPolicy{StandardServices=false,Services=[new("CustomService",true,"Running",Severity.Warning,3)]};var saved=store.Save(new(1,policy,oldVersion));check("Agent custom service policy stored",saved.Services.Single().Name=="CustomService"&&saved.Version==oldVersion+1);
        check("Agent stale policy update rejected",Rejected(()=>store.Save(new(1,new(),oldVersion))));
        check("Agent unsafe polling frequency rejected",Rejected(()=>AgentWire.Validate(new AgentPolicy{IntervalSeconds=1})));
        check("Agent unknown command property rejected",Rejected(()=>JsonSerializer.Deserialize<AgentPolicy>("{\"RunPowerShell\":\"whoami\"}",AgentWire.Json)));
        check("Agent duplicate service rules rejected",Rejected(()=>AgentWire.Validate(new AgentPolicy{Services=[new("Svc"),new("svc")]})));
        var findings=AgentChecks.Evaluate(report,policy);check("Agent missing selected service is a finding",findings.Any(f=>f.Key=="service:CustomService"&&f.DelayMinutes==3));
        report.Services=[new("CustomService","Custom","Running")];check("Agent selected service recovery clears finding",!AgentChecks.Evaluate(report,policy).Any(f=>f.Key.StartsWith("service:")));
        report.UptimeSeconds=10;report.Services=[];check("Agent boot grace suppresses service alarm",!AgentChecks.Evaluate(report,policy).Any(f=>f.Key.StartsWith("service:")));report.UptimeSeconds=10000;
        report.CpuPercent=99;report.MemoryAvailableBytes=0;report.Disks=[new("C:\\",100L*1073741824,1L*1073741824)];findings=AgentChecks.Evaluate(report,policy);check("Agent CPU RAM and disk thresholds evaluated",findings.Any(f=>f.Key=="cpu")&&findings.Any(f=>f.Key=="ram")&&findings.Any(f=>f.Key=="disk:C:\\"));
        policy.ExcludedVolumes=["C:\\"];check("Agent disk exclusion respected",!AgentChecks.Evaluate(report,policy).Any(f=>f.Key.StartsWith("disk:")));
        check("Agent baseline monitors essential Windows services",AgentChecks.Defaults([]).Select(s=>s.Name).Order().SequenceEqual(new[]{"EventLog","RpcSs","Winmgmt"}.Order()));
        check("Agent DC defaults depend on installed role",AgentChecks.Defaults([new("NTDS","AD","Running"),new("Kdc","KDC","Running")]).Any(s=>s.Name=="Kdc")&&!AgentChecks.Defaults([]).Any(s=>s.Name=="Kdc"));
        check("Agent invalid metric range rejected",Rejected(()=>AgentWire.Validate(new AgentReport{MachineName="M",CpuCount=1,CpuPercent=101})));
        var rebooted=new AgentReport{MachineName="FixtureMachine",AgentVersion="0.42.5.0",CpuCount=4,CpuPercent=10,MemoryTotalBytes=100,MemoryAvailableBytes=50,UptimeSeconds=10,AppliedPolicyVersion=saved.Version};check("Agent uptime decrease identifies reboot",store.Accept(Envelope(enrolled.AgentId,2,key,rebooted)).Reboot);
        check("Next signed report replaces the displayed agent version",store.ReportedVersions().GetValueOrDefault(1)=="0.42.5.0");
        store.Revoke(1);check("Revoked agent version is removed from the registry",!store.ReportedVersions().ContainsKey(1));check("Agent revocation blocks valid signed reports",Rejected(()=>store.Accept(Envelope(enrolled.AgentId,3,key))));
        check("Agent view contains no enrollment token or CA private key",!JsonSerializer.Serialize(store.Get(1)).Contains(b.Token)&&!JsonSerializer.Serialize(store.Get(1)).Contains("ProtectedCa"));
        var adapters=new AgentReport{Networks=[new("Ethernet","up",0,0,1000000000,true),new("6to4 Adapter","down",0,0,0),new("VPN","up",12000000,0,100000000)]};
        var networkPolicy=new AgentPolicy{NetworkRules=[new("VPN")]};
        var cards=AgentGaugeModel.Build(adapters,networkPolicy).Where(g=>g.Key.StartsWith("network:")).Select(g=>g.Key);
        check("Only the primary adapter has a card; other real adapters still generate configured alerts",
            cards.SequenceEqual(["network:Ethernet"])&&AgentChecks.Evaluate(adapters,networkPolicy).Any(f=>f.Key=="network:VPN"));
        var extra=AgentWire.Verify(Package(2));var expired=Enrollment(extra,other);var indexPath=Path.Combine(root,"agent-index.json");var index=JsonSerializer.Deserialize<AgentIndex>(File.ReadAllText(indexPath),AgentWire.Json)!;index.Agents.Single(a=>a.DeviceId==2).EnrollmentExpires=DateTime.UtcNow.AddSeconds(-1);File.WriteAllText(indexPath,JsonSerializer.Serialize(index,AgentWire.Json));
        check("Agent expired registration permission rejected",Rejected(()=>store.Enroll(expired)));
        check("Agent manual Defender excluded from defaults",!AgentChecks.Defaults([new("WinDefend","Defender","Running","Manual")]).Any(r=>r.Name=="WinDefend"));
        check("Agent automatic active Defender included",AgentChecks.Defaults([new("WinDefend","Defender","Running","Automatic")]).Any(r=>r.Name=="WinDefend"));
        check("Agent dashboard distinguishes missing telemetry",DashboardState.Device(new(){AgentHealth="MISSING",AgentSummary="No data"},[],DateTime.Now,60,true).Lamp==LampState.Amber);
        check("Agent endpoint cannot include an API path",Rejected(()=>AgentWire.Endpoint(new(){ServerUrl="https://192.0.2.1/other",ServerSha256=b.ServerSha256,AllowedNetworks=b.AllowedNetworks})));
        check("Agent oversized policies rejected",Rejected(()=>AgentWire.Validate(new AgentPolicy{Services=Enumerable.Range(0,65).Select(i=>new AgentServiceRule("svc"+i)).ToList()})));
        var roles=new UserView("fixture","fixture",false,true,1,new(){["devices"]=1});
        check("Agent device reader can view",!Rejected(()=>AccessRules.Command(roles,"agent-view")));
        check("Agent device reader cannot approve or issue package",Rejected(()=>AccessRules.Command(roles,"agent-approve"))&&Rejected(()=>AccessRules.Command(roles,"agent-package")));
        var settings=new AppSettings{SqlitePath=Path.Combine(folder,"agent-schema.db")};var factory=new DatabaseFactory(settings);await factory.InitializeAsync();await using(var db=factory.Create()){db.Devices.Add(new(){Name="Agent schema",AgentHealth="OK",AgentLastSeen=DateTime.Now,AgentSummary="Fixture"});await db.SaveChangesAsync();await BindingSchema.EnsureAsync(db);check("Agent schema fields round trip",(await db.Devices.SingleAsync()).AgentSummary=="Fixture");}
        var rolesRoot=Path.Combine(folder,"agent-roles-persistent");
        AgentStore RolesStore()=>new(rolesRoot,new string('A',64),x=>x,x=>x,new Dictionary<string,string>{["SysAdminAssistant.Agent.exe"]=new string('B',64)});
        var rolesStore=RolesStore();var rolesPackage=rolesStore.Package(new(9,"https://192.0.2.1:7443/",["192.0.2.1/32"],new(){["SysAdminAssistant.Agent.exe"]=new string('B',64)}),"Roles");
        var rolesBootstrap=AgentWire.Verify(rolesPackage);var rolesEnrollment=rolesStore.Enroll(Enrollment(rolesBootstrap,key));rolesStore.Approve(9);
        var rolePolicy=rolesStore.Save(new(9,new AgentPolicy{StandardServices=false,RoleProfilesConfigured=true,ServiceRoles=["sql","file"]},rolesStore.Get(9)!.Policy.Version));
        var rolesReport=new AgentReport{MachineName="Roles",CpuName="CPU",CpuCount=2,CpuPercent=20,MemoryTotalBytes=1000,MemoryAvailableBytes=500,UptimeSeconds=10000,Services=[new("MSSQL$ERP","SQL ERP","Running","Automatic"),new("LanmanServer","SMB","Running","Automatic")]};
        var roleAccepted=rolesStore.Accept(Envelope(rolesEnrollment.AgentId,1,key,rolesReport));
        check("Server heartbeat discovers and compiles SQL role to flat rule",roleAccepted.Policy.Services.Any(s=>s.Name=="MSSQL$ERP"&&s.Profiles!.Contains("sql")));
        var roleVersion=roleAccepted.Policy.Version;rolesStore.Accept(Envelope(rolesEnrollment.AgentId,2,key,rolesReport));check("Unchanged heartbeat does not bump role policy version",rolesStore.Get(9)!.Policy.Version==roleVersion);
        rolesStore=RolesStore();var fromDisk=rolesStore.Get(9)!.Policy;check("Server role sources survive real store restart",fromDisk.Services.Any(s=>s.Name=="MSSQL$ERP"&&s.Profiles!.Contains("sql")));
        fromDisk.ServiceRoles=["file"];var afterRemoval=rolesStore.Save(new(9,fromDisk,fromDisk.Version));check("Server role removal drops SQL but retains SMB",!afterRemoval.Services.Any(s=>s.Name=="MSSQL$ERP")&&afterRemoval.Services.Any(s=>s.Name=="LanmanServer"));
        var deletePolicy=rolesStore.Get(9)!.Policy;AgentServiceProfiles.Delete(deletePolicy,"LanmanServer");rolesStore.Save(new(9,deletePolicy,deletePolicy.Version));rolesStore=RolesStore();
        check("Server stores deletion independently of client session",rolesStore.Get(9)!.Policy.ExcludedServices.Contains("LanmanServer")&&!rolesStore.Get(9)!.Policy.Services.Any(s=>s.Name=="LanmanServer"));
        using(var leaf=rolesStore.IssueServerCertificate("127.0.0.1",DateTime.UtcNow))File.WriteAllBytes(Path.Combine(rolesRoot,"server.pfx"),leaf.Export(X509ContentType.Pfx,"test-password"));
        using(var lifecycle=new CertificateLifecycle(rolesRoot,"test-password","127.0.0.1",rolesStore)){
            check("Trust counter zero is real before signed acknowledgement",lifecycle.Status().Confirmed==0&&lifecycle.Status().Agents==1);
            rolesReport.TrustVersion=lifecycle.Status().TrustVersion;rolesReport.AgentVersion="0.25.0";rolesStore.Accept(Envelope(rolesEnrollment.AgentId,3,key,rolesReport));
            check("Trust counter reads signed report despite lightweight Active view",rolesStore.Active().Single().Report==null&&lifecycle.Status().Confirmed==1&&lifecycle.Status().Agents==1);
            check("Certificate default status does not disclose agent identities",lifecycle.Status().AgentDetails==null);
            check("Administrator trust details include signed version and report time",lifecycle.Status(true).AgentDetails is {Length:1} details&&details[0].TrustVersion==rolesReport.TrustVersion&&details[0].LastSeenUtc!=null&&details[0].AgentVersion=="0.25.0");
            rolesStore.ResetTrust();check("New trust version stays unconfirmed until next signed acknowledgement",lifecycle.Status().Confirmed==0);
            rolesReport.TrustVersion=lifecycle.Status().TrustVersion;rolesStore.Accept(Envelope(rolesEnrollment.AgentId,4,key,rolesReport));
            var reopened=RolesStore();check("Trust confirmation survives persisted store reopening",reopened.TrustConfirmations().Single().TrustVersion==rolesReport.TrustVersion);
            var pending=AgentWire.Verify(rolesStore.Package(new(10,"https://192.0.2.1:7443/",["192.0.2.1/32"],new(){["SysAdminAssistant.Agent.exe"]=new string('B',64)}),"Pending"));rolesStore.Enroll(Enrollment(pending,other));rolesStore.Approve(10);
            check("Agent without report is pending rather than falsely confirmed",lifecycle.Status().Confirmed==1&&lifecycle.Status().Agents==2&&lifecycle.Status(true).AgentDetails!.Single(a=>a.DeviceId==10).TrustVersion==0);
            rolesStore.Revoke(9);check("Revoked agent excluded from certificate trust counts",lifecycle.Status().Confirmed==0&&lifecycle.Status().Agents==1);
        }
        await using(var db=factory.Create()){var d=await db.Devices.SingleAsync();d.Host="192.0.2.2";d.Status="OFFLINE";d.CheckedAt=DateTime.Now;d.AgentLastSeen=DateTime.Now;await db.SaveChangesAsync();}
        var notifications=new NetworkStore(Path.Combine(folder,"agent-icmp-alive"));settings.AvailabilityAlertMinutes=0;await ApplicationNotifications.CaptureAsync(factory,notifications,settings,DateTime.Now,default);
        check("Agent live telemetry prevents contradictory ICMP down notification",notifications.State(s=>!s.Notifications.Any(n=>n.Source.StartsWith("Устройства"))));
    }
}
