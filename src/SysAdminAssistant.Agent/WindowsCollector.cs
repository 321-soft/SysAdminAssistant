using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
public sealed class WindowsCollector
{
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("kernel32.dll")]private static extern ulong GetTickCount64();
    [StructLayout(LayoutKind.Sequential)]private struct MemoryStatus {public uint Length,Load;public ulong TotalPhys,AvailPhys,TotalPage,AvailPage,TotalVirtual,AvailVirtual,AvailExtended;}
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    private readonly Dictionary<string,(long Rx,long Tx,long Time)> networkSamples=new();
    private long idle,kernel,user;private bool sampled;
    public AgentReport Read(int policyVersion,bool infrastructure=true)
    {
        var errors=new List<string>();var report=new AgentReport{MachineName=Environment.MachineName,Os=RuntimeInformation.OSDescription,CpuCount=Environment.ProcessorCount,UptimeSeconds=(long)(GetTickCount64()/1000),CapturedUtc=DateTime.UtcNow,AppliedPolicyVersion=policyVersion};
        if(GetSystemTimes(out var nextIdle,out var nextKernel,out var nextUser)){
            var total=nextKernel-kernel+nextUser-user;if(sampled&&total>0)report.CpuPercent=Math.Clamp(100d*(total-(nextIdle-idle))/total,0,100);sampled=true;idle=nextIdle;kernel=nextKernel;user=nextUser;
        }else errors.Add("Не прочитана загрузка CPU");
        var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};if(GlobalMemoryStatusEx(ref memory)){report.MemoryTotalBytes=(long)memory.TotalPhys;report.MemoryAvailableBytes=(long)memory.AvailPhys;}else errors.Add("Не прочитана память");
        try{using var cpu=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");report.CpuName=(cpu?.GetValue("ProcessorNameString") as string??"").Trim();}catch(Exception e)when(e is UnauthorizedAccessException or System.Security.SecurityException){errors.Add("Нет доступа к описанию CPU");}
        var disks=new List<AgentDisk>();var volumes=new List<StationVolume>();var volumeIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);try{foreach(var drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed).Take(64)){
            try{
                var volume=VolumeIdentity.Read(drive.Name);
                if(volume.AliasOf.Length>0){volumes.Add(volume);continue;}
                if(!StorageVolumes.Include(volume,volumeIds)){volumes.Add(volume with{AliasOf=volumes.First(v=>v.Identity==volume.Identity&&v.AliasOf.Length==0).Name});continue;}
                volumes.Add(volume with{TotalBytes=drive.TotalSize,FreeBytes=drive.TotalFreeSpace});disks.Add(new(drive.Name,drive.TotalSize,drive.TotalFreeSpace));
            }catch(Exception e)when(e is IOException or UnauthorizedAccessException){disks.Add(new(drive.Name,0,0,"Том не прочитан"));}
        }}catch(IOException){errors.Add("Не прочитан список томов");}report.Disks=disks.ToArray();
        report.Hardware=HardwareCache.Get(report.CpuName,report.MemoryTotalBytes,report.Disks.Sum(d=>d.TotalBytes)) with{Volumes=volumes.Take(64).ToArray()};
        if(!infrastructure){report.Errors=errors.Take(16).ToArray();return report;}
        try{var tick=Stopwatch.GetTimestamp();var networks=new List<AgentNetwork>();var seen=new HashSet<string>();foreach(var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&!AgentNetworkNames.TechnicalLayer(n.Name)&&!AgentNetworkNames.TechnicalLayer(n.Description)).Take(64)){try{var stats=nic.GetIPStatistics();double rx=0,tx=0;seen.Add(nic.Id);if(networkSamples.TryGetValue(nic.Id,out var old)){var seconds=(tick-old.Time)/(double)Stopwatch.Frequency;if(seconds>0){rx=Math.Max(0,stats.BytesReceived-old.Rx)/seconds;tx=Math.Max(0,stats.BytesSent-old.Tx)/seconds;}}networkSamples[nic.Id]=(stats.BytesReceived,stats.BytesSent,tick);networks.Add(new(AgentNetworkNames.WireName(nic.Name),nic.OperationalStatus==OperationalStatus.Up?"up":"down",rx,tx,Math.Max(0,nic.Speed),HasGateway(nic)));}catch(NetworkInformationException){errors.Add("Не прочитан сетевой интерфейс "+AgentNetworkNames.WireName(nic.Name));}}foreach(var key in networkSamples.Keys.Where(k=>!seen.Contains(k)).ToArray())networkSamples.Remove(key);report.Networks=networks.ToArray();}catch(NetworkInformationException){errors.Add("Не удалось получить сетевые интерфейсы Windows");}
        var services=new List<Core.AgentService>();try{
            var all=ServiceController.GetServices();try{foreach(var service in all.Take(1024)){try{var start="Unknown";try{start=service.StartType.ToString();}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}services.Add(new(service.ServiceName,service.DisplayName,service.Status.ToString(),start));}catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){services.Add(new(service.ServiceName,"","Unknown"));}}
            if(all.Length>1024)errors.Add("Список служб усечён до 1024");}finally{foreach(var service in all)service.Dispose();}
        }catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){errors.Add("Нет доступа к перечислению служб");}
        report.Services=services.ToArray();report.Errors=errors.Take(16).ToArray();return report;
    }
    private static bool HasGateway(NetworkInterface nic)
    {
        try{return nic.GetIPProperties().GatewayAddresses.Any(g=>!g.Address.Equals(System.Net.IPAddress.Any)&&!g.Address.Equals(System.Net.IPAddress.IPv6Any));}
        catch(NetworkInformationException){return false;}
    }
}
