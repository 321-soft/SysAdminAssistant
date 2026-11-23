using System.Text.Json;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal sealed class WindowsUpdates
{
 private Task<OsUpdateStatus>? task;private DateTime next;private OsUpdateStatus current=new(){Detail="Ожидание первого чтения локального кэша Windows Update"};
 private static string SnapshotPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SysAdmin Assistant Agent","UpdateCache","updates.json");
 public OsUpdateStatus Read(){
 // Probe writes only beneath the admin-protected program directory; agent has read access.
 try{var path=SnapshotPath;if(File.Exists(path)&&new FileInfo(path).Length<=50000){var snapshot=JsonSerializer.Deserialize<WindowsUpdateSnapshot>(File.ReadAllText(path),AgentWire.Json);var value=WindowsUpdateResults.Snapshot(snapshot,DateTime.UtcNow);if(value!=null){AgentWire.Validate(new AgentReport{MachineName="probe",CpuCount=1,Updates=value});if(value.State!="Unknown"||!current.HasUpdates||value.HasUpdates){current=value;return current;}current.Detail=value.Detail;return current;}}}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException){current.Detail="Снимок обновлений не прочитан: "+e.GetType().Name+" 0x"+e.HResult.ToString("X8");}
 if(task?.IsCompleted==true){var result=task.GetAwaiter().GetResult();if(result.State!="Unknown"||!current.HasUpdates)current=result;else current.Detail=result.Detail;next=DateTime.UtcNow.AddMinutes(result.State=="Unknown"?5:15);task=null;}if(task==null&&DateTime.UtcNow>=next)task=Task.Factory.StartNew(WindowsUpdateQuery.Read,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);return current;
 }
}
