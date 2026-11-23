namespace SysAdminAssistant.Core;

public static class InventoryMonitoringData
{
    public static bool Apply(InventoryAsset asset,string name,string? os,string? serial,string? cpu,long memory,long disks,StationHardware? hardware=null)
    {
        var changed=false;
        void Set(string? observed,Func<string> read,Action<string> write){
            if(!string.IsNullOrWhiteSpace(observed)&&read()!=observed){write(observed);changed=true;}
        }
        Set(name,()=>asset.WindowsName,v=>asset.WindowsName=v);
        Set(os,()=>asset.OperatingSystem,v=>asset.OperatingSystem=v);
        var systemSerial=HardwareIdentity.Serial(serial);if(systemSerial.Length==0)systemSerial=HardwareIdentity.Serial(hardware?.Serial);
        Set(systemSerial,()=>asset.SerialNumber,v=>asset.SerialNumber=v);
        Set(cpu,()=>asset.Cpu,v=>asset.Cpu=v);
        if(hardware!=null){
            Set(hardware.Manufacturer,()=>asset.Manufacturer,v=>asset.Manufacturer=v);
            Set(hardware.Model,()=>asset.Model,v=>asset.Model=v);
            if(hardware.Graphics is {Length:>0})Set(string.Join("; ",hardware.Graphics),()=>asset.Gpu,v=>asset.Gpu=v);
            if(hardware.Drives is {Length:>0}){
                Set(string.Join("; ",hardware.Drives.Select(d=>$"{d.Model} · {d.Bytes/1000000000d:0.#} GB · SN: {d.Serial}")),()=>asset.DiskDetails,v=>asset.DiskDetails=v);
                disks=hardware.Drives.Sum(d=>d.Bytes);
            }
            Set(System.Text.Json.JsonSerializer.Serialize(hardware),()=>asset.HardwareJson,v=>asset.HardwareJson=v);
        }
        memory=hardware==null?0:SmbiosMemory.InstalledBytes(hardware)??0;
        if(memory>0){var gb=(int)Math.Clamp(Math.Round(memory/1073741824d),1,1048576);if(asset.RamGb!=gb){asset.RamGb=gb;changed=true;}}
        if(disks>0){var gb=(int)Math.Clamp(Math.Round(disks/1000000000d),1,int.MaxValue);if(asset.DiskGb!=gb){asset.DiskGb=gb;changed=true;}}
        return changed;
    }
}
