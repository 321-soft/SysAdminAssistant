using System.Text.Json;
using SysAdminAssistant.Core;

internal static class StationPeripheralSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var old=new StationSample("Windows 11",0,null,[],null,null,null,null,"0.46.0");
        var plain=JsonSerializer.Serialize(old,AgentWire.Json);
        check("Old station reports and negotiated legacy payload omit peripherals",
            StationWire.Sample(plain).Peripherals==null&&!plain.Contains("Peripherals",StringComparison.Ordinal));
        var scan=new StationPeripheralScan(DateTime.UtcNow,
            [new("Display","Internal panel","BuiltIn","eDP"),new("Camera","Logitech camera","External","USB")]);
        var data=old with {Peripherals=scan};
        check("Station report round-trips peripheral snapshot",StationWire.Sample(JsonSerializer.Serialize(data,AgentWire.Json))
            .Peripherals is { Devices.Length:2 } result && result.Devices.SequenceEqual(scan.Devices));
        check("Internal monitor, HDMI monitor and virtual displays differ",
            StationPeripherals.DisplayLink(11).Placement=="BuiltIn"&&StationPeripherals.DisplayLink(5).Placement=="External"&&
            StationPeripherals.DisplayLink(17).Placement=="Virtual");
        check("USB is not classified as external merely because its bus is USB",
            StationPeripherals.Placement(null,false,false,false)=="Unknown"&&
            StationPeripherals.Placement(true,true,false,false)=="BuiltIn"&&
            StationPeripherals.Placement(false,true,false,false)=="External"&&
            StationPeripherals.Placement(true,false,false,true)=="Virtual");
        bool rejected=false;
        try { StationWire.Sample(JsonSerializer.Serialize(old with {Peripherals=scan with {Devices=[new("Display","Panel","Spoof","eDP")]}},AgentWire.Json)); }
        catch(ArgumentException){rejected=true;}
        check("Malformed peripheral classifications are rejected",rejected);
    }
}
