using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace SysAdminAssistant.Core;
public sealed record StationAddress(string Adapter,string Address,bool Gateway);
public sealed record StationSample(string Os,long Uptime,string? User,StationAddress[] Addresses,double? Cpu,double? Ram,double? DiskFree,OsUpdateStatus? Updates,string AgentVersion,string? Serial=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StationPeripheralScan? Peripherals=null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StationHardware? Hardware=null);
public sealed record StationMemoryModule(string Slot,string Manufacturer,string Model,string Serial,long Bytes,int SpeedMhz,string Kind="",string FormFactor="");
public sealed record StationVolume(string Name,string Identity,string AliasOf,long? TotalBytes=null,long? FreeBytes=null);
public sealed record StationPhysicalDisk(string Model,string Serial,long Bytes,string Interface);
public sealed record StationHardware(string Cpu,long MemoryBytes,long DiskBytes,string Manufacturer="",string Model="",string Motherboard="",string Bios="",
    StationMemoryModule[]? MemoryModules=null,int? MemorySlots=null,StationPhysicalDisk[]? Drives=null,string[]? Graphics=null,StationVolume[]? Volumes=null,string Serial="",int? InstalledModules=null,bool Partial=false,long? InstalledMemoryBytes=null,bool MemoryComplete=false,bool DrivesComplete=false,bool GraphicsComplete=false,DateTime? ScannedUtc=null,string MotherboardSerial="");

public sealed record StationRequest(Guid Id,string Key,string Enrollment,string Name,long Timestamp,string Nonce,string? Payload,string Signature);
public sealed record StationReply(string Status);
public sealed record StationAlert(string Code,string Title,Severity Level,DateTime Since);
public sealed record StationView(Guid Id,string Name,string Status,DateTime Seen,StationSample? Sample,StationAlert[] Alerts, bool AutoUpdate=false);
public sealed record StationUpdatePolicy(Guid Id, bool Enabled);
public sealed record StationConfig(bool Telegram=false,string Chat="",int? Thread=null,bool Critical=true,bool Warning=false,string LatestAgent="",bool Rotate=false,bool UseServerTelegram=false);
public static class StationWire
{
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static byte[] Bytes(StationRequest r)=>JsonSerializer.SerializeToUtf8Bytes(r with {Signature=""},AgentWire.Json);
    public static void Verify(StationRequest r,DateTime now)
    {
        if(r.Id==Guid.Empty||r.Key is not {Length:>=120 and <=180}||r.Name is not {Length:>0 and <=128}||r.Name.Any(char.IsControl)
           ||r.Nonce is not {Length:32}||!r.Nonce.All(Uri.IsHexDigit)||r.Enrollment is not {Length:0 or 64}
           ||r.Payload?.Length>40000||r.Signature is not {Length:88}
           ||Math.Abs((decimal)new DateTimeOffset(now).ToUnixTimeSeconds()-r.Timestamp)>300)
            throw new UnauthorizedAccessException("Некорректный запрос станции");
        using var key=ECDsa.Create();
        try{
            var bytes=Convert.FromBase64String(r.Key);key.ImportSubjectPublicKeyInfo(bytes,out var read);
            if(read!=bytes.Length||key.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7"
                ||!key.VerifyData(Bytes(r),Convert.FromBase64String(r.Signature),HashAlgorithmName.SHA256))
                throw new UnauthorizedAccessException("Неверная подпись станции");
        }catch(CryptographicException){throw new UnauthorizedAccessException("Неверный ключ станции");}
    }
    public static StationSample Sample(string payload)
    {
        var s=JsonSerializer.Deserialize<StationSample>(payload,AgentWire.Json)??throw new ArgumentException("Нет отчёта");
        static bool Text(string? v,int n)=>v!=null&&v.Length<=n&&!v.Any(char.IsControl);
        static bool Percent(double? p)=>p==null||p is >=0 and <=100;
        if(!Text(s.Os,160)||!Text(s.AgentVersion,32)||s.Serial!=null&&!Text(s.Serial,128)||s.User!=null&&!Text(s.User,256)||s.Uptime is <0 or >3155760000
           ||s.Addresses is not {Length:<=96}||s.Addresses.Any(a=>a==null||!Text(a.Adapter,120)||!Text(a.Address,64)||!System.Net.IPAddress.TryParse(a.Address,out _))
           ||!Percent(s.Cpu)||!Percent(s.Ram)||!Percent(s.DiskFree)
           ||s.Updates is {} u&&(u.PendingCount is <0 or >1000||u.Items is not {Length:<=20}||u.Items.Any(x=>!Text(x,200))
           ||!Text(u.Detail,500)||u.State is not ("Current" or "Unknown" or "Available")))
            throw new ArgumentException("Некорректные показатели станции");
        if(s.Hardware is {} hardware && !HardwareValidation.Valid(hardware))
            throw new ArgumentException("Некорректные характеристики станции");
        if(s.Peripherals is {} scan && !StationPeripherals.Valid(scan)) throw new ArgumentException("Некорректный снимок периферии");
        return s;
    }
    public static IEnumerable<(string Code,string Title,Severity Level,int Minutes)> Conditions(StationSample s)
    {
        if(s.DiskFree is <5)yield return ("disk-critical","На системном диске менее 5%",Severity.Critical,15);
        else if(s.DiskFree is <15)yield return ("disk-warning","На системном диске менее 15%",Severity.Warning,15);
        if(s.Cpu is >90)yield return ("cpu","Длительная высокая нагрузка CPU",Severity.Warning,20);
        if(s.Ram is >90)yield return ("ram","Длительная высокая нагрузка RAM",Severity.Warning,20);
        if(s.Updates?.RebootRequired==true)yield return ("reboot","Ожидается перезагрузка",Severity.Warning,0);
    }
    public static DateTime Workdays(DateTime date,int days)
    {
        while(days>0){date=date.AddDays(1);if(date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))days--;}
        return date;
    }
}
