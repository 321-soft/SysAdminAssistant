using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private static string HardwareText(StationHardware? hardware,string? fallbackSerial=null)=>HardwarePresentation.Text(hardware,fallbackSerial);
}
