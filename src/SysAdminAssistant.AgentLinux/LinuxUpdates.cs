using System.Diagnostics;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.AgentLinux;
internal sealed class LinuxUpdates
{
    private OsUpdateStatus current=new(){Detail="Ожидание проверки локального кэша APT"};
    private DateTime next=DateTime.MinValue;
    private Task<OsUpdateStatus>? task;
    public OsUpdateStatus Read(){
        if(task?.IsCompleted==true){var value=task.GetAwaiter().GetResult();current=OsUpdateStatus.Merge(current,value);next=DateTime.UtcNow.AddMinutes(value.State=="Unknown"?5:15);task=null;}
        if(task==null&&DateTime.UtcNow>=next)task=Task.Factory.StartNew(Query,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        return current;
    }
    private static OsUpdateStatus Query(){
        var now=DateTime.UtcNow;
        try{
            if(!File.Exists("/usr/bin/apt")||!Directory.Exists("/var/lib/apt/lists")||!Directory.EnumerateFiles("/var/lib/apt/lists").Any())
                return new(){State="Unknown",CheckedUtc=now,Detail="Нет локальных индексов APT; обновите их штатными средствами"};
            var pi=new ProcessStartInfo("/usr/bin/apt"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};pi.ArgumentList.Add("list");pi.ArgumentList.Add("--upgradable");pi.Environment["LANG"]="C";pi.Environment["LC_ALL"]="C";
            using var p=Process.Start(pi)??throw new InvalidOperationException();var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(20000)){p.Kill(true);throw new TimeoutException();}if(p.ExitCode!=0)throw new InvalidOperationException("APT query failed");
            var lines=stdout.GetAwaiter().GetResult().Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(l=>l.Contains("[upgradable from:",StringComparison.Ordinal)).Take(100001).ToArray();
            if(lines.Length>100000)return new(){State="Unknown",CheckedUtc=now,Detail="Список обновлений слишком велик"};
            var items=lines.Take(20).Select(l=>l.Trim()[..Math.Min(l.Trim().Length,200)]).ToArray();
            var reboot=File.Exists("/var/run/reboot-required");
            return new(){State=lines.Length>0||reboot?"Available":"Current",CheckedUtc=now,PendingCount=lines.Length,Items=items,RebootRequired=reboot,Detail="По локальным индексам APT; агент их не обновляет"};
        }catch(Exception e)when(e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException){return new(){State="Unknown",CheckedUtc=now,Detail="Не удалось прочитать локальный кэш APT: "+e.GetType().Name};}
    }
}
