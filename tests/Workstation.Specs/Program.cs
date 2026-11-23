using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
var dir=Path.Combine(Path.GetTempPath(),"station-spec-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
try{
    var store=new WorkstationStore(dir);
    var token=store.Save(new(Rotate:true,Telegram:true,Chat:"-100123",Thread:7));
    var now=new DateTime(2026,11,13,9,0,0,DateTimeKind.Utc);
    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var id=Guid.NewGuid();
    var sample=new StationSample("Windows 11",3600,"DEMO\\user",[new("Ethernet","192.0.2.10",true)],15,30,3,null,"0.45.0");
    StationRequest Sign(DateTime at,string? payload=null,ECDsa? signer=null,Guid? requestId=null,string name="PC-01"){
        signer??=key;
        var r=new StationRequest(requestId??id,Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),payload==null?token:"",name,
            new DateTimeOffset(at).ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),payload,"");
        return r with {Signature=Convert.ToBase64String(signer.SignData(StationWire.Bytes(r),HashAlgorithmName.SHA256))};
    }
    static void Check(bool result,string message){if(!result)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    static void Reject(Action action){try{action();}catch(UnauthorizedAccessException){return;}throw new Exception("Expected rejection");}
    var raw=JsonSerializer.Serialize(sample,AgentWire.Json);
    Reject(()=>store.Report(Sign(now,raw),now));
    Check(store.List(false,now).Length==0,"Cannot register by sending telemetry");
    var secondToken=store.IssueEnrollment();
    Check(secondToken!=token,"Each package receives a separate enrollment key");
    var first=Sign(now);
    Check(store.Report(first,now).Status=="Pending","First contact requires manual confirmation");
    Reject(()=>store.Report(first,now));
    Reject(()=>store.Report(Sign(now,raw),now));
    Check(store.List(false,now).Single().Sample==null,"Pending entry contains no telemetry");
    store.Decide(id,"approve");
    Reject(()=>store.Report(Sign(now,null,other),now));
    for(int i=0;i<=16;i+=2){var at=now.AddMinutes(i);store.Report(Sign(at,raw,name:"PC-02"),at);}
    var later=now.AddMinutes(16);
    Check(store.List(false,later).Single().Name=="PC-02","Rename preserves identity");
    Check(store.Pending(later).Single().Alert.Code=="disk-critical","Sustained disk condition creates a notification");
    store.Sent(store.Pending(later),later);
    Check(store.Pending(later).Length==0,"Notification is deduplicated");
    var recovered=JsonSerializer.Serialize(sample with{DiskFree=60},AgentWire.Json);
    store.Report(Sign(later,recovered),later);
    Check(store.List(false,later).Single().Alerts.Length==0,"Recovery clears active alert");
    store.Decide(id,"deny");
    Check(store.List(false,later).Length==0&&store.List(true,later).Single().Sample==null,"Denial hides entry and clears telemetry");
    store.Decide(id,"delete");
    var reloaded=new WorkstationStore(dir);
    Check(reloaded.List(true,later).Length==0&&reloaded.Report(Sign(later,requestId:Guid.NewGuid()),later).Status=="Denied",
        "Deleting a denial preserves key block across restarts and new IDs");
}finally{Directory.Delete(dir,true);}
