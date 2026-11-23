using System.Runtime.InteropServices;
using System.Text;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal static class VolumeIdentity
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string path,StringBuilder name,uint length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern uint QueryDosDeviceW(string name,StringBuilder target,int length);
    public static StationVolume Read(string name)
    {
        var buffer=new StringBuilder(1024);
        if(QueryDosDeviceW(name.TrimEnd('\\'),buffer,buffer.Capacity)>0&&buffer.ToString().StartsWith(@"\??\",StringComparison.Ordinal))
            return new(name,"",buffer.ToString()[4..]);
        buffer.Clear();
        return GetVolumeNameForVolumeMountPointW(name,buffer,(uint)buffer.Capacity)?new(name,buffer.ToString(),""):new(name,name,"");
    }
}
