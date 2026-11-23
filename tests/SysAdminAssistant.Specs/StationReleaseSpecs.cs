using System.Security.Cryptography;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

internal static class StationReleaseSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var directory=Directory.CreateTempSubdirectory("saa-station-update-");
        try
        {
            var now=DateTime.UtcNow;
            var stations=new WorkstationStore(directory.FullName);
            var enrollment=stations.IssueEnrollment();
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var id=Guid.NewGuid();
            StationRequest Request(string? payload=null,string token="")
            {
                var request=new StationRequest(id,Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),token,"PC-SPEC",
                    new DateTimeOffset(now).ToUnixTimeSeconds(),Guid.NewGuid().ToString("N"),payload,"");
                return request with {Signature=Convert.ToBase64String(key.SignData(StationWire.Bytes(request),HashAlgorithmName.SHA256))};
            }
            static bool Reject(Action action)
            {
                try {action();return false;}
                catch (Exception e) when(e is UnauthorizedAccessException or InvalidDataException or InvalidOperationException) {return true;}
            }
            stations.Report(Request(token:enrollment),now);
            check("Pending station cannot request automatic updates",Reject(()=>stations.AuthorizeUpdate(Request("0.46.0.0"),now)));
            stations.Decide(id,"approve");
            check("Station automatic update defaults to off",!stations.AuthorizeUpdate(Request("0.46.0.0"),now));
            stations.SetAutoUpdate(new(id,true));
            var request=Request("0.46.0.0");
            check("Approved pilot authorization survives store reload",new WorkstationStore(directory.FullName).AuthorizeUpdate(request,now));
            check("Station update nonce cannot be replayed",Reject(()=>stations.AuthorizeUpdate(request,now)));
            check("Update authorization does not mark station as online",stations.List(false,now).Single().Seen==now);
            var authority=new AgentStore(directory.FullName,new string('A',64),x=>x,x=>x);
            var cert=authority.AuthorityCertificate();
            var manifest=new StationRelease(id,request.Nonce,"0.46.1.0",1024,new string('A',64),new DateTimeOffset(now).AddMinutes(9).ToUnixTimeSeconds());
            string Header(SignedStationRelease signed)=>Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(signed,AgentWire.Json));
            var valid=authority.SignStationRelease(manifest);
            var current=new Version("0.46.0.0");
            check("Station accepts release signed by pinned authority",StationReleaseWire.Verify(Header(valid),cert,request,current,now)==manifest);
            check("Station rejects tampering, replay binding, downgrades, and expired release",
                Reject(()=>StationReleaseWire.Verify(Header(valid with { Payload=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(manifest with {Length=256},AgentWire.Json)) }),cert,request,current,now)) &&
                Reject(()=>StationReleaseWire.Verify(Header(valid),cert,Request("0.46.0.0"),current,now)) &&
                Reject(()=>StationReleaseWire.Verify(Header(valid),cert,request,new Version("0.46.2.0"),now)) &&
                Reject(()=>StationReleaseWire.Verify(Header(valid),cert,request,current,now.AddMinutes(11))));
            using var rotated=authority.IssueServerCertificate("192.168.150.254",now);
            check("Station trusts renewed HTTPS leaf signed by existing authority without old pin",
                ServerTrust.Accept(rotated,"192.168.150.254",[new string('A',64)],cert) &&
                !ServerTrust.Accept(rotated,"192.168.150.253",[new string('A',64)],cert));
            stations.Decide(id,"deny");
            check("Denied station loses authorization immediately",Reject(()=>stations.AuthorizeUpdate(Request("0.46.0.0"),now)));
        }
        finally {directory.Delete(true);}
    }
}
