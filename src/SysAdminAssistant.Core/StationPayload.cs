using System.Text.Json;
namespace SysAdminAssistant.Core;
public static class StationPayload
{
    public static string Encode(StationSample sample)
    {
        while(true){
            var payload=JsonSerializer.Serialize(sample,AgentWire.Json);
            if(payload.Length<=40000)return payload;
            if(sample.Peripherals is {Devices.Length:>0} p){sample=sample with{Peripherals=p with{Devices=p.Devices[..^1],Partial=true}};continue;}
            if(sample.Hardware is {} h){
                if(h.MemoryModules is {Length:>0} modules){sample=sample with{Hardware=h with{MemoryModules=modules[..^1],InstalledModules=h.InstalledModules??modules.Length,InstalledMemoryBytes=SmbiosMemory.InstalledBytes(h),MemoryComplete=false,Partial=true}};continue;}
                if(h.Drives is {Length:>0} drives){sample=sample with{Hardware=h with{Drives=drives[..^1],DrivesComplete=false,Partial=true}};continue;}
                if(h.Volumes is {Length:>0} volumes){sample=sample with{Hardware=h with{Volumes=volumes[..^1],Partial=true}};continue;}
                if(h.Graphics is {Length:>0} graphics){sample=sample with{Hardware=h with{Graphics=graphics[..^1],GraphicsComplete=false,Partial=true}};continue;}
            }
            if(sample.Addresses.Length>1){sample=sample with{Addresses=sample.Addresses[..^1]};continue;}
            throw new InvalidDataException("Отчёт станции превышает допустимый размер.");
        }
    }
}
