using System.Text.Json.Serialization;
namespace SysAdminAssistant.Core;
public sealed record AgentUpdateDelivery(string State,string Code,string Message,DateTime FirstUtc,DateTime LastUtc,int Count,string Signature,string Version="",string PackageHash="");
public sealed class LinuxTelemetry
{
    public LinuxVolume[] Volumes {get;set;}=[];
    public LinuxUnit[] Units {get;set;}=[];
    public AgentUpdateDelivery? UpdateDelivery {get;set;}
}
public sealed record LinuxVolume(string Mount,string Source,string FileSystem,string DeviceKind,string DeviceId,string MountRoot,long TotalBytes,long UsedBytes,long AvailableBytes,long ReservedBytes,long? Inodes,long? FreeInodes,string Error="");
public sealed record LinuxUnit(string Name,string Type,string Active,string Sub,string Result,int ExitCode,int ExitStatus,DateTime? LastRunUtc,DateTime? NextRunUtc,string Trigger="",bool Dependency=false,string Error="");
public sealed class LinuxOptions
{
    public List<LinuxDiskRule> Disks {get;set;}=[];
    public int BootFreePercent {get;set;}=10;public int BootFreeMiB {get;set;}=128;
    public int EfiFreePercent {get;set;}=5;public int EfiFreeMiB {get;set;}=32;
    public int InodeFreePercent {get;set;}=5;
    public int SwapUsedPercent {get;set;}=0;public int SwapDelayMinutes {get;set;}=5;
}
public sealed record LinuxDiskRule(string Mount,int FreePercent=10,int FreeMiB=0,int InodeFreePercent=5,int InodeMinimum=0,bool Enabled=true);
