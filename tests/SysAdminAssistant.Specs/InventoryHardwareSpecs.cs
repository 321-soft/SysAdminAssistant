using SysAdminAssistant.Core;
using System.Text.Json;
internal static class InventoryHardwareSpecs
{
    public static void Run(Action<string,bool> check){
        var asset=new InventoryAsset{Cpu="Wrong CPU",RamGb=8,InventoryNumber="KEEP",Notes="Manual",Location="Office"};
        var h=new StationHardware("Actual CPU",32L*1073741824,1000000000000,"Vendor","PC-01","Board-01","BIOS-1",
            [new("DIMM0","RAM vendor","RAM-model","SN",16L*1073741824,3200,"DDR4"),new("DIMM1","RAM vendor","RAM-model","SN2",16L*1073741824,3200,"DDR4")],4,[new("SSD","SSD-SN",1000000000000,"SATA")],["GPU"],InstalledMemoryBytes:32L*1073741824,MemoryComplete:true,DrivesComplete:true,GraphicsComplete:true);
        check("Monitored hardware is bounded and round-trips memory model and slot count",HardwareValidation.Valid(h)&&JsonSerializer.Deserialize<StationHardware>(JsonSerializer.Serialize(h))!.MemoryModules![1].Model=="RAM-model");
        check("Observed hardware replaces incorrect values without touching accounting fields",
            InventoryMonitoringData.Apply(asset,"PC-01","Windows 11","SERIAL",h.Cpu,h.MemoryBytes,h.DiskBytes,h)&&asset.Cpu=="Actual CPU"&&asset.RamGb==32&&asset.Model=="PC-01"&&asset.InventoryNumber=="KEEP"&&asset.Notes=="Manual"&&asset.Location=="Office");
        InventoryMonitoringData.Apply(asset,"PC-01",null,null,null,0,0);
        check("Missing observations preserve last known hardware",asset.Cpu=="Actual CPU"&&asset.RamGb==32&&asset.SerialNumber=="SERIAL");
        check("Hardware synchronization is stable on repeated identical reports",!InventoryMonitoringData.Apply(asset,"PC-01","Windows 11","SERIAL",h.Cpu,h.MemoryBytes,h.DiskBytes,h));
        var volumes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        check("SUBST alias does not duplicate disk space or volume alerts",
            StorageVolumes.Include(new(@"C:\","volume-1",""),volumes)&&!StorageVolumes.Include(new(@"K:\","", @"C:\Folder"),volumes)&&!StorageVolumes.Include(new(@"L:\","VOLUME-1",""),volumes)&&StorageVolumes.Include(new(@"D:\","volume-2",""),volumes)&&volumes.Count==2);
        var longModules=Enumerable.Range(0,32).Select(i=>new StationMemoryModule(new string('Я',128),new string('Я',128),new string('Я',128),new string('Я',128),1073741824,3200)).ToArray();
        var large=new StationSample("Windows",1,null,[],null,null,null,null,"0.46.8",Hardware:h with{MemoryModules=longModules,InstalledModules=32});
        var encoded=StationPayload.Encode(large);
        var decoded=StationWire.Sample(encoded);
        check("Large hardware report is bounded while preserving installed memory count and metrics",
            encoded.Length<=40000&&decoded.Hardware!.Partial&&decoded.Hardware.InstalledModules==32&&decoded.Hardware.MemoryBytes==h.MemoryBytes);
        check("Oversized and malformed hardware reports are rejected",!HardwareValidation.Valid(h with{MemoryModules=new StationMemoryModule[33]})&&!HardwareValidation.Valid(h with{Cpu="bad\nvalue"}));
    }
}
