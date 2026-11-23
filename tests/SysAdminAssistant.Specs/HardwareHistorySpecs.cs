using System.Buffers.Binary;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
internal static class HardwareHistorySpecs
{
    public static void Run(Action<string,bool> check)
    {
        check("Firmware placeholders are not computer serial numbers",new[]{"Default string","To Be Filled By O.E.M.","System Serial Number","00000000"}.All(value=>HardwareIdentity.Serial(value)=="")&&HardwareIdentity.Serial("ABC-123")=="ABC-123");
        var folder=Directory.CreateTempSubdirectory("hardware-history-");
        try{
            var store=new HardwareHistoryStore(folder.FullName);var source=HardwareHistoryStore.Station(Guid.NewGuid());
            var time=new DateTime(2026,11,20,12,0,0,DateTimeKind.Utc);
            var h=new StationHardware("CPU-A",7L*1073741824,1000000000,
                MemoryModules:[new("DIMM0","Vendor","Model","RAM1",8L*1073741824,3200,"DDR4","SO-DIMM")],MemorySlots:2,
                Drives:[new("Disk-A","D1",1000000000,"SATA")],MemoryComplete:true,DrivesComplete:true,ScannedUtc:time);
            store.Observe(source,h,time);
            check("First hardware snapshot is one initial configuration event",store.History(source) is [{Title:"Исходная конфигурация"}]);
            check("Physical RAM comes from modules rather than usable Windows RAM",SmbiosMemory.InstalledBytes(h)==8L*1073741824);
            var biosSource=HardwareHistoryStore.Station(Guid.NewGuid());
            store.Observe(biosSource,h with{Bios="Firmware A"},time);
            store.Observe(biosSource,h with{Bios="Firmware B",ScannedUtc=time.AddMinutes(1)},time.AddMinutes(1));
            store.Observe(biosSource,h with{Bios="Firmware B",ScannedUtc=time.AddMinutes(2)},time.AddMinutes(2));
            check("Automatic BIOS updates never create hardware upgrade history",store.History(biosSource).Length==1&&!store.History(biosSource)[0].After.Contains("Firmware")&&!HardwareChanges.Sections(h with{Bios="Firmware B"}).ContainsKey("BIOS"));
            var legacySource=HardwareHistoryStore.Station(Guid.NewGuid());var legacyPath=Path.Combine(folder.FullName,"hardware-history",legacySource+".json");
            File.WriteAllText(legacyPath,System.Text.Json.JsonSerializer.Serialize(new HardwareHistoryState{Events=[new(legacySource,1,time,"Исходная конфигурация","","Процессор\nCPU-A\n\nBIOS\nFirmware A\nFirmware A"),new(legacySource,2,time,"Изменено: BIOS","Firmware A","Firmware B"),new(legacySource,3,time,"Добавлен накопитель","","Disk D2")]},RemoteData.Json));
            store.Link(99,legacySource);
            check("Existing BIOS entries are hidden for devices and linked inventory without deleting stored history",store.History(legacySource).Length==2&&store.AssetHistory(99).Length==2&&!store.History(legacySource)[0].After.Contains("Firmware")&&File.ReadAllText(legacyPath).Contains("Firmware B"));
            store.Observe(source,h with{ScannedUtc=time.AddMinutes(1),MemoryComplete=false,DrivesComplete=false,MemoryModules=[],Drives=[]},time.AddMinutes(1));
            check("Failed or partial hardware capture cannot remove components",store.History(source).Length==1);
            var added=h with{ScannedUtc=time.AddMinutes(2),Drives=[..h.Drives!,new("Disk-B","D2",2000000000000,"NVMe")]};
            store.Observe(source,added,time.AddMinutes(2));
            check("Disk addition records model capacity and serial",store.History(source).Last() is {Title:"Добавлен накопитель",After:var after}&&after.Contains("D2")&&after.Contains("2 ТБ"));
            var replacement=added with{ScannedUtc=time.AddMinutes(3),Drives=[new("Disk-C","D3",3000000000000,"NVMe"),added.Drives![1]]};
            store.Observe(source,replacement,time.AddMinutes(3));
            store.Observe(source,replacement,time.AddMinutes(4));
            check("One snapshot resent twice cannot confirm hardware removal",store.History(source).Length==2);
            store=new HardwareHistoryStore(folder.FullName);
            store.Observe(source,replacement with{ScannedUtc=time.AddMinutes(5)},time.AddMinutes(5));
            check("Replacement confirmation survives process restart",store.History(source).Last().Title=="Заменён накопитель"&&store.History(source).Length==3);
            store.Link(1,source);store.Link(1,null);var frozen=store.AssetHistory(1).Length;
            store.Observe(source,replacement with{Cpu="CPU-B",ScannedUtc=time.AddMinutes(6)},time.AddMinutes(6));
            store.Observe(source,replacement with{Cpu="CPU-B",ScannedUtc=time.AddMinutes(7)},time.AddMinutes(7));
            check("Detached inventory retains past history but receives no future events",store.AssetHistory(1).Length==frozen&&store.History(source).Length>frozen);
            store.Manual(1,"16 ГБ","8 ГБ","operator",time.AddMinutes(8));
            check("Manual inventory edits are separated from agent observations",store.AssetHistory(1).First() is {Actor:"operator",Title:"Карточка изменена пользователем"});
            var h2=replacement with{MemoryModules=[new("DIMM0","Vendor","Model","RAM2",8L*1073741824,3200,"DDR4","SO-DIMM")],ScannedUtc=time.AddMinutes(9)};
            store.Observe(source,h2,time.AddMinutes(9));store.Observe(source,h2 with{ScannedUtc=time.AddMinutes(10)},time.AddMinutes(10));
            check("Same model same capacity RAM replacement is detected by serial",store.History(source).Any(e=>e.Title=="Изменены модули оперативной памяти"&&e.Before.Contains("RAM1")&&e.After.Contains("RAM2")));
            var sectionBefore=new HardwareSection("Disk",new(){{"unknown","Disk · 1 ГБ"}});
            var sectionAfter=new HardwareSection("Disk",new(){{"serial","Disk · 1 ГБ · SN: SERIAL"}});
            check("Newly discovered serial is metadata enrichment rather than disk replacement",
                HardwareChanges.Title("Накопители",sectionBefore,sectionAfter).StartsWith("Уточнены")&&!HardwareChanges.Removes(sectionBefore,sectionAfter));
            check("Missing serial cannot erase previously known device identity",HardwareChanges.Equal(sectionAfter,HardwareChanges.PreserveKnownSerials(sectionAfter,sectionBefore)));
            var raw=new List<byte>(new byte[8]);
            void Add(byte type,int length,Action<byte[]> fill,params string[] strings){
                var bytes=new byte[length];bytes[0]=type;bytes[1]=(byte)length;fill(bytes);raw.AddRange(bytes);
                foreach(var value in strings){raw.AddRange(System.Text.Encoding.UTF8.GetBytes(value));raw.Add(0);}
                raw.Add(0);if(strings.Length==0)raw.Add(0);
            }
            Add(16,15,b=>BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(13),2));
            Add(17,34,b=>{BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(12),8192);b[14]=13;b[16]=1;b[18]=26;b[23]=2;b[24]=3;b[26]=4;BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(32),3200);},"DIMM0","Vendor","SERIAL","RAM-MODEL");
            Add(2,8,b=>b[7]=1,"BOARD-123");
            Add(127,4,_=>{});
            var data=raw.ToArray();BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4),(uint)(data.Length-8));
            var memory=SmbiosMemory.Read(data);
            check("Motherboard serial remains separate from the computer serial",memory.MotherboardSerial=="BOARD-123"&&memory.Serial=="");
            check("SMBIOS reads RAM quantity model serial SO-DIMM without WMI",memory.Complete&&memory.Modules.Length==1&&memory.Slots==2&&memory.Modules[0] is {FormFactor:"SO-DIMM",Model:"RAM-MODEL",Serial:"SERIAL",Bytes:8589934592});
            var dimm=(byte[])data.Clone();dimm[8+15+2+14]=9;
            check("SMBIOS uses its own DIMM code rather than the WMI enumeration",SmbiosMemory.Read(dimm).Modules[0].FormFactor=="DIMM");
            var asset=new InventoryAsset{SerialNumber="REAL-123"};
            InventoryMonitoringData.Apply(asset,"pc",null,"Default string",null,0,0);
            check("Missing system serial cannot erase an existing real inventory serial",asset.SerialNumber=="REAL-123");
            InventoryMonitoringData.Apply(asset,"pc",null,"Default string",null,0,0,h with{Serial="PC-456"});
            check("Valid hardware serial overrides a placeholder in the lightweight sample",asset.SerialNumber=="PC-456");
            var unknown=h with{MemoryModules=[h.MemoryModules![0] with{FormFactor=""}]};
            using(ProductText.Scope("ru"))check("Missing RAM attributes identify the exact field",HardwarePresentation.Text(unknown).Contains("Форм-фактор: не сообщается")&&!HardwarePresentation.Text(unknown).Contains("Не получено"));
            using(ProductText.Scope("en"))check("Workstation actions and missing RAM fields translate",ProductText.Get("Рабочие станции")=="Workstations"&&ProductText.Get("Создать установщик MSI")=="Create MSI installer"&&HardwarePresentation.Text(unknown).Contains("Form factor: not reported"));
            check("Truncated SMBIOS is rejected as incomplete",!SmbiosMemory.Read(data[..^2]).Complete);
        }finally{folder.Delete(true);}
    }
}
