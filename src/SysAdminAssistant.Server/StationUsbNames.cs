using SysAdminAssistant.Core;

namespace SysAdminAssistant.Server;

internal static class StationUsbNames
{
    private static readonly Lazy<UsbCatalog?> Catalog = new(()=>
    {
        var path=Path.Combine(AppContext.BaseDirectory,"HardwareCatalog","usb.ids");
        try{return File.Exists(path)&&new FileInfo(path).Length<=4*1024*1024?new UsbCatalog(File.ReadLines(path)):null;}
        catch(IOException){return null;}
        catch(UnauthorizedAccessException){return null;}
    });
    internal static StationView[] Annotate(StationView[] stations)
    {
        if(Catalog.Value is not {} catalog)return stations;
        return stations.Select(s=>s.Sample is {} sample&&sample.Peripherals is {} scan
            ?s with {Sample=sample with {Peripherals=scan with {Devices=scan.Devices.Select(catalog.Resolve).ToArray()}}}:s).ToArray();
    }
}
