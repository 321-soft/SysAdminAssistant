namespace SysAdminAssistant.Core;
public static class HardwareValidation
{
    public static bool Valid(StationHardware h)
    {
        static bool T(string? s,int n)=>s!=null&&s.Length<=n&&!s.Any(char.IsControl);
        return (h.ScannedUtc==null||h.ScannedUtc.Value.Kind==DateTimeKind.Utc)
            &&(h.InstalledMemoryBytes is null or >=0 and <=1125899906842624L)
            &&(!h.MemoryComplete||h.MemoryModules!=null)&&(!h.DrivesComplete||h.Drives!=null)&&(!h.GraphicsComplete||h.Graphics!=null)
            &&(h.InstalledModules is null or >=0 and <=256)&&T(h.Cpu,256)&&h.MemoryBytes is >=0 and <=1125899906842624L&&h.DiskBytes is >=0 and <=9000000000000000000L
            &&T(h.MotherboardSerial,128)&&T(h.Serial,128)&&T(h.Manufacturer,128)&&T(h.Model,128)&&T(h.Motherboard,256)&&T(h.Bios,256)&&h.MemorySlots is null or >=0 and <=256
            &&(h.MemoryModules==null||h.MemoryModules.Length<=32&&h.MemoryModules.All(m=>m!=null&&T(m.Slot,128)&&T(m.Manufacturer,128)&&T(m.Model,128)&&T(m.Serial,128)&&T(m.Kind,32)&&T(m.FormFactor,32)&&m.Bytes is >=0 and <=1125899906842624L&&m.SpeedMhz is >=0 and <=100000))
            &&(h.Drives==null||h.Drives.Length<=32&&h.Drives.All(d=>d!=null&&T(d.Model,128)&&T(d.Serial,128)&&T(d.Interface,64)&&d.Bytes is >=0 and <=1125899906842624000L))
            &&(h.Volumes==null||h.Volumes.Length<=64&&h.Volumes.All(v=>v!=null&&T(v.Name,16)&&T(v.Identity,256)&&T(v.AliasOf,256)&&v.TotalBytes is null or >=0&&v.FreeBytes is null or >=0))
            &&(h.Graphics==null||h.Graphics.Length<=16&&h.Graphics.All(g=>T(g,256)));
    }
}
