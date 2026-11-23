using System.ComponentModel.DataAnnotations;
namespace SysAdminAssistant.Core;

public enum AssetState { Stock, Assigned, Repair, WrittenOff }
public enum InventoryOperation { Issue, Return, WriteOff }
public sealed class Employee : Entity
{
    [MaxLength(200)] public string FullName { get; set; } = "";
    [MaxLength(100)] public string PersonnelNumber { get; set; } = "";
    public string Department { get; set; } = "";
    public string Position { get; set; } = "";
    public string Contact { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Revision { get; set; }
    public override string ToString() => FullName + (Department.Length > 0 ? " · " + Department : "");
}
public sealed class InventoryAsset : Entity
{
    [Range(typeof(long), "0", "99999999999999")] public long? ValueKopecks { get; set; }
    [MaxLength(100)] public string InventoryNumber { get; set; } = "";
    public string Category { get; set; } = "Компьютер";
    public string WindowsName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string HardwareJson { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int RamGb { get; set; }
    public int DiskGb { get; set; }
    public string DiskType { get; set; } = "SSD";
    public string DiskDetails { get; set; } = "";
    public string Gpu { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Location { get; set; } = "";
    public string Condition { get; set; } = "Исправно";
    public string Notes { get; set; } = "";
    public DateTime? PurchasedAt { get; set; }
    public DateTime? WarrantyUntil { get; set; }
    public AssetState State { get; set; }
    public int? EmployeeId { get; set; }
    public int? DeviceId { get; set; }
    public Guid? WorkstationId { get; set; }
    public int Revision { get; set; }
}
public sealed class InventoryAct : Entity
{
    public string RepresentativePosition { get; set; } = "";
    public DateTime? DismissalDate { get; set; }
    [MaxLength(100)] public string Number { get; set; } = "";
    public InventoryOperation Operation { get; set; }
    public DateTime Date { get; set; } = AppTime.Today;
    public DateTime CreatedAt { get; set; } = AppTime.Now;
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string Department { get; set; } = "";
    public string Position { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Representative { get; set; } = "";
    public string Place { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Commission { get; set; } = "";
}
public sealed class InventoryActLine : Entity
{
    [Range(typeof(long), "0", "99999999999999")] public long? ValueKopecks { get; set; }
    public int ActId { get; set; }
    public int AssetId { get; set; }
    public string InventoryNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string WindowsName { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Specifications { get; set; } = "";
    public string Condition { get; set; } = "";
    public AssetState PreviousState { get; set; }
}
public static class InventoryDisplay
{
    public static string State(AssetState s) => s switch { AssetState.Stock => "На складе", AssetState.Assigned => "Выдано", AssetState.Repair => "В ремонте", _ => "Списано" };
    public static string Operation(InventoryOperation s) => s switch { InventoryOperation.Issue => "Выдача", InventoryOperation.Return => "Возврат", _ => "Списание" };
    public static string Specs(InventoryAsset a) => string.Join("; ", new[] { a.Cpu, a.RamGb > 0 ? $"ОЗУ {a.RamGb} ГБ" : "", a.DiskGb > 0 ? $"Диски {a.DiskGb} ГБ ({a.DiskType})" : "", a.DiskDetails, a.Gpu, a.OperatingSystem }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
public static class HardwareCatalog
{
    public static readonly int[] Ram = [0, 1, 2, 4, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 2048];
    public static readonly int[] Disk = [0, 64, 120, 128, 240, 250, 256, 480, 500, 512, 960, 1000, 1024, 1920, 2000, 2048, 3840, 4000, 4096, 8000, 10000, 12000, 16000, 18000, 20000, 24000];
    public static readonly string[] CpuFamilies = ["Intel Core i3", "Intel Core i5", "Intel Core i7", "Intel Core i9", "Intel Core Ultra 5", "Intel Core Ultra 7", "Intel Core Ultra 9", "Intel Xeon", "Intel Pentium", "Intel Celeron", "Intel Atom", "Intel Core 2 Duo", "Intel Core 2 Quad", "AMD Ryzen 3", "AMD Ryzen 5", "AMD Ryzen 7", "AMD Ryzen 9", "AMD Ryzen PRO", "AMD Ryzen Threadripper", "AMD EPYC", "AMD Athlon", "AMD FX", "AMD Phenom", "AMD A-series", "AMD Opteron", "Apple M1", "Apple M2", "Apple M3", "Apple M4", "Qualcomm Snapdragon X Plus", "Qualcomm Snapdragon X Elite", "Другой / ручной ввод"];
    public static IEnumerable<string> CpuModels => CpuFamilies.Concat(new[]
    {
        "Intel Core i3-6100", "Intel Core i3-7100", "Intel Core i3-8100", "Intel Core i3-9100", "Intel Core i3-10100", "Intel Core i3-12100", "Intel Core i3-13100", "Intel Core i3-14100",
        "Intel Core i5-2400", "Intel Core i5-3470", "Intel Core i5-4570", "Intel Core i5-6500", "Intel Core i5-7500", "Intel Core i5-8400", "Intel Core i5-9400", "Intel Core i5-10400", "Intel Core i5-11400", "Intel Core i5-12400", "Intel Core i5-13400", "Intel Core i5-14400", "Intel Core i5-14600K",
        "Intel Core i7-3770", "Intel Core i7-4770", "Intel Core i7-6700", "Intel Core i7-7700", "Intel Core i7-8700", "Intel Core i7-9700", "Intel Core i7-10700", "Intel Core i7-11700", "Intel Core i7-12700", "Intel Core i7-13700", "Intel Core i7-14700",
        "Intel Core i9-9900K", "Intel Core i9-10900K", "Intel Core i9-11900K", "Intel Core i9-12900K", "Intel Core i9-13900K", "Intel Core i9-14900K",
        "Intel Core Ultra 5 125H", "Intel Core Ultra 5 225", "Intel Core Ultra 7 155H", "Intel Core Ultra 7 265K", "Intel Core Ultra 9 185H", "Intel Core Ultra 9 285K",
        "Intel Pentium G4560", "Intel Pentium Gold G6400", "Intel Celeron G3900", "Intel Celeron G5905", "Intel Processor N100", "Intel Processor N200", "Intel Xeon E3-1230 v2", "Intel Xeon E3-1230 v3", "Intel Xeon E5-2620 v4", "Intel Xeon E5-2680 v4", "Intel Xeon Silver 4210", "Intel Xeon Gold 6230",
        "AMD Ryzen 3 2200G", "AMD Ryzen 3 3200G", "AMD Ryzen 3 4100", "AMD Ryzen 3 4300G", "AMD Ryzen 5 1600", "AMD Ryzen 5 2600", "AMD Ryzen 5 3600", "AMD Ryzen 5 4600G", "AMD Ryzen 5 5500", "AMD Ryzen 5 5600", "AMD Ryzen 5 5600G", "AMD Ryzen 5 7600", "AMD Ryzen 5 8600G", "AMD Ryzen 5 9600X",
        "AMD Ryzen 7 2700X", "AMD Ryzen 7 3700X", "AMD Ryzen 7 5700G", "AMD Ryzen 7 5700X", "AMD Ryzen 7 5800X", "AMD Ryzen 7 7700", "AMD Ryzen 7 7800X3D", "AMD Ryzen 7 8700G", "AMD Ryzen 7 9700X", "AMD Ryzen 7 9800X3D",
        "AMD Ryzen 9 3900X", "AMD Ryzen 9 5900X", "AMD Ryzen 9 5950X", "AMD Ryzen 9 7900", "AMD Ryzen 9 7950X", "AMD Ryzen 9 9900X", "AMD Ryzen 9 9950X", "AMD Ryzen Threadripper 3970X", "AMD EPYC 7302", "AMD Athlon 3000G", "AMD FX-8350"
    });
}
