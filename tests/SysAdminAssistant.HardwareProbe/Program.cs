using System.Text.Json;
using SysAdminAssistant.Agent;
if(!OperatingSystem.IsWindows()){Console.Error.WriteLine("Run this hardware probe on Windows.");return 2;}
try{
    Console.WriteLine("SysAdmin Assistant hardware probe 0.46.15");
    Console.WriteLine("Read-only local collection; service configuration is not changed.");
    Console.WriteLine("WMI operating system: "+StationPeripheralCollector.ProbeWmi());
    var hardware=StationPeripheralCollector.ReadHardware();
    var peripherals=StationPeripheralCollector.Read();
    Console.WriteLine(JsonSerializer.Serialize(new{Hardware=hardware,Peripherals=peripherals},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
    return peripherals.Partial?1:0;
}catch(Exception error){Console.Error.WriteLine(error.ToString());return 1;}
