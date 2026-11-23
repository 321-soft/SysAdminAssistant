using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;
public sealed class AgentPolicy
{
    public int Version {get;set;}=1;
    public int IntervalSeconds {get;set;}=30;
    public int AlertMinutes {get;set;}=2;
    public int BootGraceMinutes {get;set;}=5;
    public int CpuPercent {get;set;}=90;
    public int RamPercent {get;set;}=90;
    public int DiskFreePercent {get;set;}=10;
    public int DiskFreeGb {get;set;}=10;
    public string[] ExcludedVolumes {get;set;}=[];
    public bool StandardServices {get;set;}=true;
    public List<AgentServiceRule> Services {get;set;}=[];
    public bool RoleProfilesConfigured {get;set;}
    public string[] ServiceRoles {get;set;}=[];
    public string[] ExcludedServices {get;set;}=[];
    public int ServiceProfileOverflow {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public List<AgentNetworkRule>? NetworkRules {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LinuxOptions? Linux {get;set;}
}
public sealed record AgentNetworkRule(string Name,int WarnPercent=90,int DelayMinutes=2,bool Enabled=true,int CapacityMbps=0);
public sealed record LinuxInstallCommand(int DeviceId,string Command,DateTime ExpiresUtc);
public sealed record AgentServiceRule(string Name,bool Enabled=true,string Expected="Running",Severity Severity=Severity.Critical,int DelayMinutes=2,string[]? Profiles=null,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? MaxIntervalMinutes=null)
{
    [JsonIgnore] public string Origin=>AgentServiceProfiles.Origin(this);
}
public sealed record AgentService(string Name,string DisplayName,string Status,string StartMode="Unknown");
public sealed record AgentDisk(string Name,long TotalBytes,long FreeBytes,string Error="");
public sealed record AgentNetwork(string Name,string State,double ReceiveBytesPerSecond,double SendBytesPerSecond,long LinkBitsPerSecond,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool DefaultGateway=false);
public sealed class AgentReport
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public LinuxTelemetry? Linux {get;set;}
    public string AgentVersion {get;set;}="";
    public string AgentUpdaterStatus {get;set;}="";
    public AgentEventBatch? EventBatch {get;set;}
    public OsUpdateStatus? Updates {get;set;}
    public long TrustVersion {get;set;}
    public string MachineName {get;set;}="";
    public string Os {get;set;}="";
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StationHardware? Hardware {get;set;}
    public string CpuName {get;set;}="";
    public int CpuCount {get;set;}
    public double? CpuPercent {get;set;}
    public long MemoryTotalBytes {get;set;}
    public long MemoryAvailableBytes {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public long SwapTotalBytes {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public long SwapFreeBytes {get;set;}
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public AgentNetwork[]? Networks {get;set;}
    public long UptimeSeconds {get;set;}
    public DateTime CapturedUtc {get;set;}
    public int AppliedPolicyVersion {get;set;}
    public AgentDisk[] Disks {get;set;}=[];
    public AgentService[] Services {get;set;}=[];
    public string[] Errors {get;set;}=[];
}
public sealed class AgentBootstrap
{
    public string EnrollmentId {get;set;}="";
    public string Token {get;set;}="";
    public int DeviceId {get;set;}
    public string DeviceName {get;set;}="";
    public string ServerUrl {get;set;}="";
    public string ServerSha256 {get;set;}="";
    public string[] AllowedNetworks {get;set;}=[];
    public DateTime ExpiresUtc {get;set;}
    public string IssuerCertificate {get;set;}="";
    public Dictionary<string,string> Binaries {get;set;}=[];
}
public sealed record AgentSignedBootstrap(string Payload,string Signature);
public sealed record AgentEnrollment(string EnrollmentId,string Token,string PublicKey,string Proof,string MachineName);
public sealed record AgentEnrollmentResult(string AgentId,string Certificate,AgentPolicy Policy);
public sealed record AgentEnvelope(string AgentId,long Sequence,long Timestamp,string Payload,string Signature);
public sealed record AgentSequenceNotice(long NextSequence);
public sealed class AgentSequenceException(long next):UnauthorizedAccessException("Последовательность отчётов требует синхронизации") {public long NextSequence {get;}=next;}
public sealed record AgentReply(AgentPolicy Policy,string Certificate);
public sealed record AgentFinding(string Key,string Title,Severity Severity,int DelayMinutes);
public static class AgentWire
{
    public static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,MaxDepth=20};
    public static byte[] Bytes(AgentEnvelope e)=>Encoding.UTF8.GetBytes("SAA-AGENT-REPORT-1\n"+e.AgentId+"\n"+e.Sequence+"\n"+e.Timestamp+"\n"+e.Payload);
    public static byte[] EnrollmentBytes(AgentEnrollment e)=>Encoding.UTF8.GetBytes("SAA-AGENT-ENROLL-1\n"+e.EnrollmentId+"\n"+e.Token+"\n"+e.PublicKey+"\n"+e.MachineName);
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Text(string? value,int max)=>value!=null&&value.Length<=max&&!value.Any(c=>char.IsControl(c));
    public static Uri Endpoint(AgentBootstrap b)
    {
        if(!Uri.TryCreate(b.ServerUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo!=""||uri.AbsolutePath!="/"||uri.Query!=""||uri.Fragment!=""||!IPAddress.TryParse(uri.Host.Trim('[',']'),out var ip))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Агент принимает только HTTPS-адрес сервера с IP и портом"));
        if(b.ServerSha256.Length!=64||!b.ServerSha256.All(Uri.IsHexDigit)||b.AllowedNetworks.Length is <1 or >32||!b.AllowedNetworks.Any(n=>Contains(n,ip)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Адрес сервера вне разрешённых подсетей или неверен отпечаток"));
        foreach(var n in b.AllowedNetworks)_=Contains(n,ip);return uri;
    }
    public static bool Contains(string cidr,IPAddress ip)
    {
        var parts=cidr.Split('/');if(parts.Length!=2||!IPAddress.TryParse(parts[0],out var network)||!int.TryParse(parts[1],out var prefix))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная подсеть агента"));
        var a=network.GetAddressBytes();var b=ip.GetAddressBytes();if(prefix<0||prefix>a.Length*8)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная маска подсети"));if(a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++){var bits=Math.Clamp(prefix-i*8,0,8);var mask=(byte)(0xff<<(8-bits));if((a[i]&mask)!=(b[i]&mask))return false;}return true;
    }
    public static void Validate(AgentPolicy p)
    {
        if(p.Version<1||p.IntervalSeconds is <15 or >300||p.AlertMinutes is <0 or >1440||p.BootGraceMinutes is <0 or >60||p.CpuPercent is <1 or >100||p.RamPercent is <1 or >100||p.DiskFreePercent is <0 or >95||p.DiskFreeGb is <0 or >100000||p.ExcludedVolumes==null||p.ExcludedVolumes.Length>64||p.ExcludedVolumes.Any(x=>!Text(x,64))||p.Services==null||p.Services.Count>64)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректная политика агента"));
        if(p.NetworkRules!=null&&(p.NetworkRules.Count>64||p.NetworkRules.Any(n=>n==null||!Text(n.Name,64)||n.Name.Length==0||n.WarnPercent is <1 or >100||n.DelayMinutes is <0 or >1440||n.CapacityMbps is <0 or >1000000)||p.NetworkRules.Select(n=>n.Name).Distinct(StringComparer.Ordinal).Count()!=p.NetworkRules.Count))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные пороги интерфейсов"));
        LinuxPolicy.Validate(p);
        AgentServiceProfiles.Validate(p);
        if(p.Services.Any(s=>s==null))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустая запись службы"));
        if(p.Services.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.Services.Count||p.Services.Any(s=>!Text(s.Name,256)||string.IsNullOrWhiteSpace(s.Name)||s.Expected is not("Running" or "Stopped")||s.Severity is not(Severity.Warning or Severity.Critical)||s.DelayMinutes is <0 or >1440))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный список служб"));
    }
    public static void Validate(AgentReport r)
    {
        if(!Text(r.AgentVersion,50)||!Text(r.AgentUpdaterStatus,500))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверная версия или состояние обновления агента"));
        if(r.Linux!=null)LinuxPolicy.ValidateTelemetry(r.Linux);
        if(r.EventBatch!=null)AgentEvents.Validate(r.EventBatch);
        if(r.Updates is {} u && (u.State is not("Unknown" or "Available" or "Current")||u.Items==null||u.Items.Length>20||u.Items.Any(x=>!Text(x,200))||!Text(u.Installed,100)||!Text(u.Available,100)||!Text(u.Detail,500)||u.PendingCount is <0 or >100000||u.CheckedUtc>DateTime.UtcNow.AddMinutes(10)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректные сведения об обновлениях"));
        if(r.Hardware is {} hardware&&!HardwareValidation.Valid(hardware))throw new ArgumentException("Некорректные характеристики оборудования");
        if(r.TrustVersion is <0 or >9000000000000||!Text(r.MachineName,128)||r.MachineName.Length==0||!Text(r.Os,512)||!Text(r.CpuName,256)||r.CpuCount is <1 or >4096||r.CpuPercent is <0 or >100||r.CpuPercent is double c&&!double.IsFinite(c)||r.MemoryTotalBytes<0||r.MemoryAvailableBytes<0||r.MemoryAvailableBytes>r.MemoryTotalBytes||r.UptimeSeconds is <0 or >3155760000||r.Disks==null||r.Disks.Length>64||r.Services==null||r.Services.Length>1024||r.Errors==null||r.Errors.Length>16)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Некорректный отчёт агента"));
        if(r.SwapTotalBytes<0||r.SwapFreeBytes<0||r.SwapFreeBytes>r.SwapTotalBytes||r.Networks is { } nets&&(nets.Length>64||nets.Any(n=>n==null||!Text(n.Name,64)||!Text(n.State,32)||n.LinkBitsPerSecond<0||!double.IsFinite(n.ReceiveBytesPerSecond)||!double.IsFinite(n.SendBytesPerSecond)||n.ReceiveBytesPerSecond<0||n.SendBytesPerSecond<0)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверные показатели swap или сети"));
        if(r.Disks.Any(d=>!Text(d.Name,64)||d.TotalBytes<0||d.FreeBytes<0||d.FreeBytes>d.TotalBytes||!Text(d.Error,256))||r.Services.Any(s=>s==null||!Text(s.Name,256)||!Text(s.DisplayName,256)||!Text(s.StartMode,32)||s.Status is not("Running" or "Stopped" or "Paused" or "StartPending" or "StopPending" or "ContinuePending" or "PausePending" or "Unknown"))||r.Errors.Any(e=>!Text(e,256))||r.Services.Select(s=>s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Services.Length||r.Disks.Select(d=>d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.Disks.Length)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Недопустимые поля отчёта агента"));
    }
    public static AgentBootstrap Verify(AgentSignedBootstrap signed)
    {
        if(signed.Payload.Length>200000||signed.Signature.Length>256)throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Слишком большой пакет"));
        var b=JsonSerializer.Deserialize<AgentBootstrap>(signed.Payload,Json)??throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Пустой пакет"));
        using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(b.IssuerCertificate));using var key=cert.GetECDsaPublicKey()??throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Неверный издатель"));
        if(!key.VerifyData(Encoding.UTF8.GetBytes(signed.Payload),Convert.FromBase64String(signed.Signature),HashAlgorithmName.SHA256))throw new CryptographicException(global::SysAdminAssistant.Core.ProductText.Get("Подпись конфигурации не совпала"));
        _=Endpoint(b);if(b.Binaries.Count is <1 or >300||b.Binaries.Any(x=>Path.GetFileName(x.Key)!=x.Key||x.Key.Length==0||x.Key.StartsWith('.')||x.Key.EndsWith('.')||x.Key.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('.' or '-' or '_'))||x.Key.Contains('\\')||x.Key.Contains(':')||x.Key is "." or ".."||x.Key.Length>150||x.Value.Length!=64||!x.Value.All(Uri.IsHexDigit)))throw new ArgumentException(global::SysAdminAssistant.Core.ProductText.Get("Неверный состав агента"));return b;
    }
}
public static class AgentChecks
{
    public static AgentServiceRule[] Defaults(IEnumerable<AgentService> inventory,bool includeDomain=true)
    {
        var rows=inventory.ToArray();var names=rows.Select(s=>s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseline=new[]{"EventLog","RpcSs","Winmgmt"}.ToList();
        if(rows.Any(s=>s.Name.Equals("WinDefend",StringComparison.OrdinalIgnoreCase)&&s.Status=="Running"&&s.StartMode=="Automatic"))baseline.Add("WinDefend");
        if(includeDomain&&names.Contains("NTDS"))baseline.AddRange(["NTDS","DNS","Kdc","Netlogon"]);
        return baseline.Where(n=>n is "EventLog" or "RpcSs" or "Winmgmt"||names.Contains(n)).Distinct(StringComparer.OrdinalIgnoreCase).Select(s=>new AgentServiceRule(s)).ToArray();
    }
    public static AgentFinding[] Evaluate(AgentReport r,AgentPolicy p)
    {
        var findings=new List<AgentFinding>();
        if(r.Errors.Length>0||r.CpuPercent==null||r.MemoryTotalBytes==0)findings.Add(new("collector","Не все показатели прочитаны",Severity.Warning,p.AlertMinutes));
        if(r.CpuPercent>=p.CpuPercent)findings.Add(new("cpu",$"Высокая загрузка CPU: {r.CpuPercent:0.0}% (порог {p.CpuPercent}%)",Severity.Warning,p.AlertMinutes));
        if(r.MemoryTotalBytes>0&&100d*(r.MemoryTotalBytes-r.MemoryAvailableBytes)/r.MemoryTotalBytes>=p.RamPercent)findings.Add(new("ram",$"RAM занято {100d*(r.MemoryTotalBytes-r.MemoryAvailableBytes)/r.MemoryTotalBytes:0.0}% (порог {p.RamPercent}%)",Severity.Warning,p.AlertMinutes));
        if(r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase))findings.AddRange(LinuxPolicy.Disks(r,p));
        else foreach(var d in r.Disks.Where(d=>!p.ExcludedVolumes.Contains(d.Name,StringComparer.OrdinalIgnoreCase))){
            if(d.Error!="")findings.Add(new("disk:"+d.Name,"Не удалось прочитать том "+d.Name,Severity.Warning,p.AlertMinutes));
            else if(d.TotalBytes>0&&(p.DiskFreePercent>0&&100d*d.FreeBytes/d.TotalBytes<=p.DiskFreePercent||p.DiskFreeGb>0&&d.FreeBytes<=p.DiskFreeGb*1073741824L))findings.Add(new("disk:"+d.Name,"Мало места на "+d.Name+": свободно "+AgentIssueAdvice.Size(d.FreeBytes)+" из "+AgentIssueAdvice.Size(d.TotalBytes),Severity.Critical,p.AlertMinutes));
        }
        if(p.NetworkRules!=null)
            foreach(var rule in p.NetworkRules.Where(n=>n.Enabled)){
                var nic=r.Networks?.FirstOrDefault(n=>n.Name.Equals(rule.Name,StringComparison.Ordinal));
                var capacity=rule.CapacityMbps>0?(long)rule.CapacityMbps*1_000_000:nic?.LinkBitsPerSecond??0;
                if(nic!=null&&nic.State=="up"&&capacity>0&&Math.Max(nic.ReceiveBytesPerSecond,nic.SendBytesPerSecond)*8/capacity*100>=rule.WarnPercent)
                    findings.Add(new("network:"+rule.Name,"Высокая загрузка интерфейса "+rule.Name,Severity.Warning,rule.DelayMinutes));
            }
        if(r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase))findings.AddRange(LinuxPolicy.Systemd(r,p));
        if(r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase)&&p.Linux is {SwapUsedPercent:>0} options&&r.SwapTotalBytes>0&&100d*(r.SwapTotalBytes-r.SwapFreeBytes)/r.SwapTotalBytes>=options.SwapUsedPercent)findings.Add(new("swap","Высокое использование swap",Severity.Warning,options.SwapDelayMinutes));
        if(r.Linux?.UpdateDelivery is {State:"Rejected" or "RollbackFailed" or "RolledBack"} delivery)findings.Add(new("update-delivery:"+delivery.Code,"Ошибка доставки обновления: "+delivery.Code,delivery.State=="RollbackFailed"?Severity.Critical:Severity.Warning,0));
        if(r.UptimeSeconds>=p.BootGraceMinutes*60){
            bool linux=r.Os.StartsWith("Linux ",StringComparison.OrdinalIgnoreCase);
            var rules=(linux?p.Services.Where(s=>!p.ExcludedServices.Contains(s.Name,StringComparer.OrdinalIgnoreCase)):AgentServiceProfiles.Effective(p,r.Services)).ToDictionary(s=>s.Name,StringComparer.OrdinalIgnoreCase);
            if(!linux)findings.AddRange(AgentServiceProfiles.Findings(p,r.Services));
            foreach(var rule in rules.Values.Where(s=>s.Enabled&&(!linux||!r.Errors.Contains("Не удалось получить службы systemd",StringComparer.Ordinal))))if((!linux||r.Linux==null)&&r.Services.FirstOrDefault(s=>s.Name.Equals(rule.Name,StringComparison.OrdinalIgnoreCase))?.Status!=rule.Expected)findings.Add(new("service:"+rule.Name,"Служба "+rule.Name+": "+(r.Services.FirstOrDefault(s=>s.Name.Equals(rule.Name,StringComparison.OrdinalIgnoreCase))?.Status??"не обнаружена")+", ожидалось "+rule.Expected,rule.Severity,rule.DelayMinutes));
        }
        return findings.ToArray();
    }
}
