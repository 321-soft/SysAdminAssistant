using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.AgentLinux;

// Read-only collection; external execution is limited to the fixed systemctl query below.
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxCollector
{
    private ulong previousTotal, previousIdle;
    private Dictionary<string,(ulong rx,ulong tx)> previousNetwork=new(StringComparer.Ordinal);
    private long previousNetworkTime;
    public AgentReport Read(AgentPolicy policy)
    {
        var errors=new List<string>();
        var r=new AgentReport { MachineName=Environment.MachineName, Os="Linux "+RuntimeInformation.OSDescription,
            CpuName=CpuName(), CpuCount=Environment.ProcessorCount, CapturedUtc=DateTime.UtcNow,
            AppliedPolicyVersion=policy.Version, Linux=new LinuxTelemetry() };
        try {
            var line=File.ReadLines("/proc/stat").First().Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(line.Length<5||line[0]!="cpu")throw new InvalidDataException();
            var v=line.Skip(1).Take(8).Select(x=>ulong.Parse(x,CultureInfo.InvariantCulture)).ToArray();
            ulong total=0;foreach(var x in v)total+=x;
            var idle=v[3]+(v.Length>4?v[4]:0);
            if(previousTotal>0&&total>previousTotal&&idle>=previousIdle)r.CpuPercent=Math.Clamp(100d*(1d-(double)(idle-previousIdle)/(total-previousTotal)),0,100);
            previousTotal=total;previousIdle=idle;
        } catch(Exception e)when(e is IOException or FormatException or OverflowException or InvalidDataException or UnauthorizedAccessException){errors.Add("Не прочитана загрузка CPU");}
        try {
            var lines=File.ReadLines("/proc/meminfo").Select(x=>x.Split(new[]{':',' ','\t'},StringSplitOptions.RemoveEmptyEntries)).Where(x=>x.Length>1).ToDictionary(x=>x[0],x=>long.TryParse(x[1],out var n)?n:0);
            r.MemoryTotalBytes=checked(lines["MemTotal"]*1024);
            r.MemoryAvailableBytes=checked(lines.GetValueOrDefault("MemAvailable")*1024);
            r.SwapTotalBytes=checked(lines.GetValueOrDefault("SwapTotal")*1024);
            r.SwapFreeBytes=checked(lines.GetValueOrDefault("SwapFree")*1024);
        }catch(Exception e)when(e is IOException or OverflowException or KeyNotFoundException or UnauthorizedAccessException){errors.Add("Не прочитана память");}
        try{r.UptimeSeconds=(long)double.Parse(File.ReadAllText("/proc/uptime").Split(' ',StringSplitOptions.RemoveEmptyEntries)[0],CultureInfo.InvariantCulture);}
        catch(Exception e)when(e is IOException or FormatException or OverflowException or UnauthorizedAccessException){errors.Add("Не прочитан uptime");}
        try{r.Networks=Network();}catch(Exception e)when(e is IOException or UnauthorizedAccessException or FormatException or OverflowException){errors.Add("Не удалось получить сетевые интерфейсы");}
        try{r.Linux.Volumes=LinuxVolumes.Read();r.Disks=r.Linux.Volumes.Where(v=>v.Mount.Length<=64).Select(v=>new AgentDisk(v.Mount,v.TotalBytes,v.AvailableBytes,v.Error)).ToArray();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){errors.Add("Не прочитана таблица файловых систем Linux");}
        try{var systemd=LinuxSystemd.Read(policy);r.Services=systemd.Item1;r.Linux.Units=systemd.Item2;}
        catch(Exception e)when(e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException){errors.Add("Не удалось получить службы systemd");errors.Add("systemctl: "+new string(e.Message.Where(c=>!char.IsControl(c)).Take(220).ToArray()));}
        r.Errors=errors.Take(16).ToArray();return r;
    }
    AgentNetwork[] Network()
    {
        var now=Stopwatch.GetTimestamp();var seconds=previousNetworkTime==0?0:(double)(now-previousNetworkTime)/Stopwatch.Frequency;
        var gateway=DefaultRoute();
        var next=new Dictionary<string,(ulong rx,ulong tx)>(StringComparer.Ordinal);var rows=new List<AgentNetwork>();
        foreach(var line in File.ReadLines("/proc/net/dev").Skip(2)){
            var parts=line.Split(':',2);if(parts.Length!=2)continue;var name=parts[0].Trim();
            if(name=="lo"||name.Length is <1 or >64||name.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not('-' or '_' or '.' or '@')))continue;
            var values=parts[1].Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(values.Length<16||!ulong.TryParse(values[0],out var rx)||!ulong.TryParse(values[8],out var tx))continue;
            next[name]=(rx,tx);double receive=0,send=0;
            if(seconds>0&&previousNetwork.TryGetValue(name,out var old)){if(rx>=old.rx)receive=(rx-old.rx)/seconds;if(tx>=old.tx)send=(tx-old.tx)/seconds;}
            var root=Path.Combine("/sys/class/net",name);string state="unknown";long bits=0;
            try{state=File.ReadAllText(Path.Combine(root,"operstate")).Trim();if(long.TryParse(File.ReadAllText(Path.Combine(root,"speed")).Trim(),out var mbps)&&mbps>0&&mbps<=1_000_000)bits=mbps*1_000_000;}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
            if(state.Length>32)state="unknown";rows.Add(new(name,state,receive,send,bits,name==gateway));if(rows.Count>=64)break;
        }
        previousNetwork=next;previousNetworkTime=now;return rows.ToArray();
    }
    private static string? DefaultRoute()
    {
        try{
            string? selected=null;long metric=long.MaxValue;
            foreach(var line in File.ReadLines("/proc/net/route").Skip(1)){
                var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(fields.Length<8||fields[1]!="00000000"||fields[7]!="00000000"||
                    !int.TryParse(fields[3],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var flags)||(flags&1)==0||(flags&0x200)!=0||
                    !long.TryParse(fields[6],NumberStyles.None,CultureInfo.InvariantCulture,out var priority)||priority>=metric)continue;
                selected=fields[0];metric=priority;
            }
            return selected;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException){return null;}
    }
    static string CpuName()
    {
        try{return File.ReadLines("/proc/cpuinfo").Where(l=>l.StartsWith("model name",StringComparison.Ordinal)).Select(l=>l.Split(':',2)[1].Trim()).FirstOrDefault()??RuntimeInformation.ProcessArchitecture.ToString();}
        catch(IOException){return RuntimeInformation.ProcessArchitecture.ToString();}
    }
}
