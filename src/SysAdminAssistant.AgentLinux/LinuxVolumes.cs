using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public static class LinuxVolumes
{
    [StructLayout(LayoutKind.Sequential)] private struct Stat {public ulong Bsize,Frsize,Blocks,Bfree,Bavail,Files,Ffree,Favail,Fsid,Flags,Namemax;public int S0,S1,S2,S3,S4,S5;}
    [DllImport("libc",EntryPoint="statvfs",SetLastError=true)] private static extern int StatVfs(string path,out Stat value);
    public sealed record Mount(string Path,string Root,string Source,string Fs,string Device);
    public static Mount? ParseMount(string line)
    {
        var parts=line.Split(" - ",2);if(parts.Length!=2)return null;var left=parts[0].Split(' ',StringSplitOptions.RemoveEmptyEntries);var right=parts[1].Split(' ',StringSplitOptions.RemoveEmptyEntries);if(left.Length<6||right.Length<2)return null;
        string Decode(string value)=>Regex.Replace(value,@"\\([0-7]{3})",m=>((char)Convert.ToInt32(m.Groups[1].Value,8)).ToString(),RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        var mount=new Mount(Decode(left[4]),Decode(left[3]),Decode(right[1]),right[0],left[2]);if(!AgentWire.Text(mount.Path,512)||!AgentWire.Text(mount.Root,512)||!AgentWire.Text(mount.Source,512))return null;return mount;
    }
    public static LinuxVolume[] Read()
    {
        var result=new List<LinuxVolume>();var ignored=new HashSet<string>(["proc","sysfs","tmpfs","devtmpfs","devpts","cgroup","cgroup2","securityfs","pstore","debugfs","tracefs","hugetlbfs","mqueue","configfs","fusectl","autofs","binfmt_misc","rpc_pipefs"]);
        foreach(var m in File.ReadLines("/proc/self/mountinfo").Select(ParseMount).Where(m=>m!=null).Cast<Mount>().Where(m=>!ignored.Contains(m.Fs)).DistinctBy(m=>m.Path).Take(64)){
            string kind="файловая система";try{var sys="/sys/dev/block/"+m.Device;if(File.Exists(sys+"/dm/uuid")){var uuid=File.ReadAllText(sys+"/dm/uuid");kind=uuid.StartsWith("LVM-",StringComparison.Ordinal)?"LVM":uuid.StartsWith("CRYPT-",StringComparison.Ordinal)?"dm-crypt":"device-mapper";}else if(File.Exists(sys+"/partition"))kind="раздел";else if(Directory.Exists(sys))kind="блочное устройство";}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
            try{
                if(StatVfs(m.Path,out var s)!=0)throw new IOException("statvfs errno "+Marshal.GetLastPInvokeError());long bytes(ulong count)=>checked((long)count*(long)(s.Frsize==0?s.Bsize:s.Frsize));
                long? inodes=s.Files==0||s.Files>long.MaxValue?null:(long)s.Files;long? free=inodes==null?null:(long)Math.Min(s.Favail,(ulong)inodes);
                result.Add(new(m.Path,m.Source,m.Fs,kind,m.Device,m.Root,bytes(s.Blocks),bytes(s.Blocks-Math.Min(s.Blocks,s.Bfree)),bytes(Math.Min(s.Blocks,s.Bavail)),bytes(s.Bfree> s.Bavail?s.Bfree-s.Bavail:0),inodes,free));
            }catch(Exception e)when(e is IOException or OverflowException){result.Add(new(m.Path,m.Source,m.Fs,kind,m.Device,m.Root,0,0,0,0,null,null,"statvfs: "+new string(e.Message.Where(c=>!char.IsControl(c)).Take(160).ToArray())+"; проверьте монтирование и доступ"));}
        }return result.ToArray();
    }
}
