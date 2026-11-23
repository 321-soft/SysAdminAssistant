using System.Runtime.InteropServices;
namespace SysAdminAssistant.Core;
public static class InstallerTaskErrors
{
 public static bool Missing(Exception e)=>(e is COMException or FileNotFoundException or DirectoryNotFoundException)&&(e.HResult==unchecked((int)0x80070002)||e.HResult==unchecked((int)0x80070003));
}
