namespace SysAdminAssistant.Core;

public sealed record StationPeripheral(string Kind, string Name, string Placement, string Connection,
    string Manufacturer="", string ModelCode="", string Serial="");

public sealed record StationPeripheralScan(DateTime CapturedUtc, StationPeripheral[] Devices, bool Partial=false,string[]? Errors=null,DateTime? AttemptedUtc=null);

public static class StationPeripherals
{
    public const string CapabilityHeader="X-SAA-Station-Peripherals";
    public const int Limit=48;

    public static (string Placement,string Connection) DisplayLink(uint technology) => technology switch {
        6 => ("BuiltIn","LVDS"), 11 => ("BuiltIn","eDP"), 13 => ("BuiltIn","UDI"), 0x80000000 => ("BuiltIn","Внутренний"),
        0 => ("External","VGA"), 4 => ("External","DVI"), 5 => ("External","HDMI"), 10 => ("External","DisplayPort"),
        12 => ("External","UDI"), 15 => ("External","Miracast"), 16 => ("Unknown","Indirect wired"),
        17 => ("Virtual","Indirect virtual"), 18 => ("External","DisplayPort USB"), _ => ("Unknown","")
    };

    public static string Placement(bool? localContainer, bool removableUsb, bool bluetooth, bool virtualDevice) =>
        virtualDevice?"Virtual":bluetooth?"External":localContainer==true?"BuiltIn":removableUsb?"External":"Unknown";

    public static bool Valid(StationPeripheralScan scan)
    {
        static bool Text(string? value,int max)=>value!=null&&value.Length<=max&&!value.Any(char.IsControl);
        return (scan.AttemptedUtc==null||scan.AttemptedUtc.Value.Kind==DateTimeKind.Utc)&&(scan.Errors==null||scan.Errors.Length<=8&&scan.Errors.All(e=>Text(e,200))) && scan.CapturedUtc.Kind==DateTimeKind.Utc && scan.Devices is {Length:<=Limit} &&
            scan.Devices.All(d=>d!=null && d.Kind is ("Display" or "Keyboard" or "Mouse" or "Touch" or "Printer" or "PrintQueue" or "Scanner" or "Camera" or "Audio" or "Dock" or "Storage" or "SmartCard" or "GameController" or "Ups" or "UsbPeripheral") &&
                d.Placement is ("BuiltIn" or "External" or "Virtual" or "Network" or "Unknown") &&
                Text(d.Name,120)&&d.Name.Length>0&&Text(d.Connection,32)&&Text(d.Manufacturer,50)&&Text(d.ModelCode,32)&&Text(d.Serial,64));
    }
}
