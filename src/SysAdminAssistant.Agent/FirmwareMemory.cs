using System.Runtime.InteropServices;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal static class FirmwareMemory
{
    [DllImport("kernel32.dll",ExactSpelling=true)]private static extern uint GetSystemFirmwareTable(uint provider,uint table,byte[]? bytes,uint size);
    [DllImport("kernel32.dll",SetLastError=true,ExactSpelling=true)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);
    public static SmbiosMemoryResult Read()
    {
        const uint provider=0x52534d42;
        var length=GetSystemFirmwareTable(provider,0,null,0);
        if(length is <8 or >1048576)return new([],null,"",false);
        var bytes=new byte[length];
        return GetSystemFirmwareTable(provider,0,bytes,length)==length?SmbiosMemory.Read(bytes):new([],null,"",false);
    }
    public static long? InstalledBytes()=>GetPhysicallyInstalledSystemMemory(out var kb)&&kb is >0 and <=1099511627776?(long)kb*1024:null;
}
