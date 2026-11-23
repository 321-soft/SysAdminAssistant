using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal static class HardwareCache
{
    private static readonly object Gate=new();
    private static Task<StationHardware>? scan;
    private static StationHardware? last;
    private static DateTime next;
    public static StationHardware Get(string cpu,long memory,long disk)
    {
        lock(Gate){
            if(scan is {IsCompleted:true}){
                if(scan.IsCompletedSuccessfully)last=scan.Result;
                scan=null;
            }
            if(scan==null&&DateTime.UtcNow>=next){
                next=DateTime.UtcNow.AddHours(6);
                scan=Task.Run(StationPeripheralCollector.ReadHardware);
            }
            return (last??new StationHardware("",0,0)) with{Cpu=cpu,MemoryBytes=memory,DiskBytes=last?.Drives is {Length:>0}?last.Drives.Sum(d=>d.Bytes):disk};
        }
    }
}
