using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal static class LinuxUpdateStatus
{
    private const string PathName="/opt/sysadminassistant-agent/update-status.json";
    private sealed record Status(DateTime TimeUtc,string Message);
    public static void Write(string message){
        var temp=PathName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new Status(DateTime.UtcNow,message)));File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.OtherRead);File.Move(temp,PathName,true);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){}finally{try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}}
    }
    public static AgentUpdateDelivery? ReadOutcome(){try{var p="/opt/sysadminassistant-agent/update-result.json";if(!File.Exists(p)||new FileInfo(p).Length>10000)return null;return JsonSerializer.Deserialize<AgentUpdateDelivery>(File.ReadAllBytes(p),AgentWire.Json);}catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return null;}}
    public static string Read(){
        try{if(!File.Exists(PathName)||new FileInfo(PathName).Length>4096)return "";var value=JsonSerializer.Deserialize<Status>(File.ReadAllText(PathName));if(value==null||value.TimeUtc<DateTime.UtcNow.AddDays(-1)||value.TimeUtc>DateTime.UtcNow.AddMinutes(5)||value.Message.Length>800||value.Message.Any(char.IsControl))return "";return value.Message;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return "";}
    }
}
